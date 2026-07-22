using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace twodog.generators;

/// <summary>
/// Emits member registration and signal sugar for user classes:
/// - [Export] properties/fields -> __BindMembers registering ClassDB
///   properties (engine Set/Get, inspector, scene serialization)
/// - [Signal] delegate XEventHandler -> registration + C# event + EmitSignalX
/// Discovery is semantic (ForAttributeWithMetadataName), so aliases and
/// qualified attribute spellings work; one emission per type regardless of
/// how many partial declarations carry attributes. Generated code is safe
/// C# (no unsafe) - pointer work happens in Godot.NativeInterop.SignalArg /
/// MemberRegistry.
/// </summary>
[Generator]
public sealed class ExportSignalGenerator : IIncrementalGenerator
{
    private enum ObjectBase { None, Node, Resource, Other }

    private sealed record ExportInfo(string MemberName, string GdName, string TypeKey, string? ObjectType,
        string GdClassName, long Hint, string HintString);

    private sealed record SignalInfo(string Accessibility, string DelegateName, string EventName, string GdName,
        List<(string ParamName, string TypeKey, string? ObjectType, string GdClassName, string CsType)> Params);

    private sealed record DiagInfo(string Id, string Message, Location? Location);

    private sealed record ClassInfo(string? Namespace, string ClassName, bool ShouldEmit,
        List<ExportInfo> Exports, List<SignalInfo> Signals, List<DiagInfo> Diagnostics);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var exportOwners = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Godot.ExportAttribute",
            static (_, _) => true,
            static (ctx, _) => ctx.TargetSymbol.ContainingType);
        var signalOwners = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Godot.SignalAttribute",
            static (_, _) => true,
            static (ctx, _) => ctx.TargetSymbol.ContainingType);

        // Dedupe across markers and across partial declarations: each marked
        // type collects (and emits) exactly once per compilation.
        var classes = exportOwners.Collect().Combine(signalOwners.Collect())
            .SelectMany(static (pair, _) =>
            {
                var owners = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
                foreach (var s in pair.Left) if (s is not null) owners.Add(s);
                foreach (var s in pair.Right) if (s is not null) owners.Add(s);
                var infos = new List<ClassInfo>();
                foreach (var owner in owners)
                {
                    if (Collect(owner) is { } info) infos.Add(info);
                }
                return infos;
            });

        context.RegisterSourceOutput(classes, static (spc, info) => Emit(spc, info));
    }

    private static ClassInfo? Collect(INamedTypeSymbol symbol)
    {
        var exports = new List<ExportInfo>();
        var signals = new List<SignalInfo>();
        var diagnostics = new List<DiagInfo>();
        var classLoc = symbol.Locations.FirstOrDefault();

        var shapeOk = true;
        if (symbol.ContainingType is not null)
        {
            diagnostics.Add(new DiagInfo("TDOG002",
                $"'{symbol.Name}' has [Export]/[Signal] members but is a nested class; Godot extension classes must be top-level.", classLoc));
            shapeOk = false;
        }
        else if (!symbol.DeclaringSyntaxReferences.All(static r =>
                     r.GetSyntax() is ClassDeclarationSyntax cds && cds.Modifiers.Any(SyntaxKind.PartialKeyword)))
        {
            diagnostics.Add(new DiagInfo("TDOG002",
                $"'{symbol.Name}' has [Export]/[Signal] members but is not declared partial; no registration code is generated.", classLoc));
            shapeOk = false;
        }

        foreach (var member in symbol.GetMembers())
        {
            switch (member)
            {
                case IPropertySymbol prop when HasAttribute(prop, "Godot.ExportAttribute"):
                    if (ValidateExport(prop, prop.IsStatic, writable: prop.SetMethod is { IsInitOnly: false },
                            readable: prop.GetMethod is not null, diagnostics))
                    {
                        AddExport(exports, diagnostics, prop, prop.Type, GetExportArgs(prop));
                    }
                    break;
                case IFieldSymbol { IsImplicitlyDeclared: false } field when HasAttribute(field, "Godot.ExportAttribute"):
                    if (ValidateExport(field, field.IsStatic, writable: field is { IsReadOnly: false, IsConst: false },
                            readable: true, diagnostics))
                    {
                        AddExport(exports, diagnostics, field, field.Type, GetExportArgs(field));
                    }
                    break;
                case INamedTypeSymbol { TypeKind: TypeKind.Delegate } del when HasAttribute(del, "Godot.SignalAttribute"):
                    AddSignal(signals, diagnostics, del);
                    break;
            }
        }

        if (exports.Count == 0 && signals.Count == 0 && diagnostics.Count == 0) return null;

        return new ClassInfo(
            symbol.ContainingNamespace.IsGlobalNamespace ? null : symbol.ContainingNamespace.ToDisplayString(),
            symbol.Name,
            shapeOk && (exports.Count > 0 || signals.Count > 0),
            exports, signals, diagnostics);
    }

    private static bool ValidateExport(ISymbol member, bool isStatic, bool writable, bool readable,
        List<DiagInfo> diagnostics)
    {
        string? problem = null;
        if (isStatic) problem = "static members cannot be exported";
        else if (!readable) problem = "exported properties must have a getter";
        else if (!writable) problem = "exported members must be writable (no readonly/const fields, no getter-only or init-only properties)";
        if (problem is null) return true;
        diagnostics.Add(new DiagInfo("TDOG004", $"[Export] {member.Name}: {problem}.", member.Locations.FirstOrDefault()));
        return false;
    }

    private static bool HasAttribute(ISymbol symbol, string fullName) =>
        symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == fullName);

    private static (long hint, string hintString) GetExportArgs(ISymbol symbol)
    {
        var attr = symbol.GetAttributes().First(a => a.AttributeClass?.ToDisplayString() == "Godot.ExportAttribute");
        long hint = 0;
        var hintString = "";
        if (attr.ConstructorArguments.Length >= 1 && attr.ConstructorArguments[0].Value is not null)
            hint = Convert.ToInt64(attr.ConstructorArguments[0].Value);
        if (attr.ConstructorArguments.Length >= 2 && attr.ConstructorArguments[1].Value is string s)
            hintString = s;
        return (hint, hintString);
    }

    private static void AddExport(List<ExportInfo> exports, List<DiagInfo> diagnostics,
        ISymbol member, ITypeSymbol type, (long hint, string hintString) args)
    {
        var (key, objectType, gdClass, objBase) = ClassifyType(type);
        if (key is null)
        {
            diagnostics.Add(new DiagInfo("TDOG001",
                $"[Export] {member.Name}: type '{type.ToDisplayString()}' is not supported by the twodog source generator yet.",
                member.Locations.FirstOrDefault()));
            return;
        }

        // GodotSharp compat: enums with no explicit hint get PropertyHint.Enum
        // and a member-name hint string (inspector dropdown); Node/Resource
        // exports get NodeType/ResourceType so the inspector filters by class.
        var (hint, hintString) = args;
        if (key == "enum" && hint == 0)
        {
            hint = 2; // PropertyHint.Enum
            hintString = string.Join(",", type.GetMembers().OfType<IFieldSymbol>()
                .Where(f => f.HasConstantValue)
                .Select(f => f.Name));
        }
        else if (key == "object" && hint == 0 && objBase is ObjectBase.Node or ObjectBase.Resource)
        {
            hint = objBase == ObjectBase.Node ? 34 : 17; // PropertyHint.NodeType / ResourceType
            hintString = gdClass;
        }
        exports.Add(new ExportInfo(member.Name, ToSnake(member.Name), key, objectType, gdClass, hint, hintString));
    }

    private static void AddSignal(List<SignalInfo> signals, List<DiagInfo> diagnostics, INamedTypeSymbol del)
    {
        var loc = del.Locations.FirstOrDefault();
        if (!del.Name.EndsWith("EventHandler", StringComparison.Ordinal))
        {
            diagnostics.Add(new DiagInfo("TDOG003", $"[Signal] {del.Name}: signal delegates must be named *EventHandler.", loc));
            return;
        }
        var invoke = del.DelegateInvokeMethod;
        if (invoke is null || !invoke.ReturnsVoid)
        {
            diagnostics.Add(new DiagInfo("TDOG003", $"[Signal] {del.Name}: signal delegates must return void.", loc));
            return;
        }

        var eventName = del.Name.Substring(0, del.Name.Length - "EventHandler".Length);
        var ps = new List<(string, string, string?, string, string)>();
        foreach (var p in invoke.Parameters)
        {
            if (p.RefKind != RefKind.None)
            {
                diagnostics.Add(new DiagInfo("TDOG003",
                    $"[Signal] {del.Name}: parameter '{p.Name}' must be passed by value (no ref/out/in).", loc));
                return;
            }
            var (key, objectType, gdClass, _) = ClassifyType(p.Type);
            if (key is null)
            {
                diagnostics.Add(new DiagInfo("TDOG001",
                    $"[Signal] {del.Name}: parameter '{p.Name}' type '{p.Type.ToDisplayString()}' is not supported yet.", loc));
                return;
            }
            ps.Add((p.Name, key, objectType, gdClass, p.Type.ToDisplayString()));
        }
        signals.Add(new SignalInfo(AccessKeyword(del.DeclaredAccessibility), del.Name, eventName, ToSnake(eventName), ps));
    }

    private static string AccessKeyword(Accessibility a) => a switch
    {
        Accessibility.Public => "public",
        Accessibility.Protected => "protected",
        Accessibility.Private => "private",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        _ => "internal",
    };

    /// <summary>Maps a C# type to a marshalling key (+ carried C# and ClassDB type names where needed).</summary>
    private static (string? key, string? objectType, string gdClassName, ObjectBase objBase) ClassifyType(ITypeSymbol type)
    {
        switch (type.ToDisplayString().TrimEnd('?'))
        {
            case "bool": return ("bool", null, "", ObjectBase.None);
            case "int": return ("int32", null, "", ObjectBase.None);
            case "long": return ("int64", null, "", ObjectBase.None);
            case "float": return ("single", null, "", ObjectBase.None);
            case "double": return ("double", null, "", ObjectBase.None);
            case "string": return ("string", null, "", ObjectBase.None);
            case "Godot.Vector2": return ("vector2", null, "", ObjectBase.None);
            case "Godot.Vector3": return ("vector3", null, "", ObjectBase.None);
            case "Godot.Color": return ("color", null, "", ObjectBase.None);
            case "Godot.Variant": return ("variant", null, "", ObjectBase.None);
            case "Godot.StringName": return ("stringname", null, "", ObjectBase.None);
            case "Godot.NodePath": return ("nodepath", null, "", ObjectBase.None);
            case "Godot.Collections.Array": return ("garray", null, "", ObjectBase.None);
            case "Godot.Collections.Dictionary": return ("gdict", null, "", ObjectBase.None);
        }
        if (type.TypeKind == TypeKind.Enum)
            return ("enum", type.ToDisplayString().TrimEnd('?'), "", ObjectBase.None);
        var objBase = ObjectBase.None;
        for (var t = type; t is not null; t = t.BaseType)
        {
            // The exported type itself may carry a nullable annotation.
            switch (t.ToDisplayString().TrimEnd('?'))
            {
                case "Godot.Node": objBase = ObjectBase.Node; break;
                case "Godot.Resource": objBase = ObjectBase.Resource; break;
                case "Godot.GodotObject":
                    var gdClass = type.ToDisplayString().TrimEnd('?') == "Godot.GodotObject" ? "Object" : type.Name;
                    return ("object", type.ToDisplayString().TrimEnd('?'), gdClass,
                        objBase == ObjectBase.None ? ObjectBase.Other : objBase);
            }
        }
        return (null, null, "", ObjectBase.None);
    }

    private static string ToSnake(string pascal)
    {
        var sb = new StringBuilder(pascal.Length + 8);
        for (var i = 0; i < pascal.Length; i++)
        {
            var c = pascal[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && (!char.IsUpper(pascal[i - 1]) || (i + 1 < pascal.Length && char.IsLower(pascal[i + 1]))))
                    sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static string VariantTypeOf(string key) => key switch
    {
        "bool" => "Godot.VariantType.Bool",
        "int32" or "int64" or "enum" => "Godot.VariantType.Int",
        "single" or "double" => "Godot.VariantType.Float",
        "string" => "Godot.VariantType.String",
        "stringname" => "Godot.VariantType.StringName",
        "nodepath" => "Godot.VariantType.NodePath",
        "vector2" => "Godot.VariantType.Vector2",
        "vector3" => "Godot.VariantType.Vector3",
        "color" => "Godot.VariantType.Color",
        "object" => "Godot.VariantType.Object",
        "variant" => "Godot.VariantType.Nil",
        "garray" => "Godot.VariantType.Array",
        "gdict" => "Godot.VariantType.Dictionary",
        _ => throw new InvalidOperationException(key),
    };

    private static string ToVariantExpr(string key, string expr) => key switch
    {
        "object" or "stringname" or "nodepath" or "garray" or "gdict" => $"Godot.Variant.From({expr})",
        "enum" => $"(Godot.Variant)(long){expr}",
        "variant" => $"{expr}.Copy()",
        _ => $"(Godot.Variant){expr}",
    };

    private static string FromVariantExpr(string key, string? objectType, string variantExpr) => key switch
    {
        "bool" => $"{variantExpr}.AsBool()",
        "int32" => $"{variantExpr}.AsInt32()",
        "int64" => $"{variantExpr}.AsInt64()",
        "single" => $"(float){variantExpr}.AsDouble()",
        "double" => $"{variantExpr}.AsDouble()",
        "string" => $"{variantExpr}.AsString()",
        "stringname" => $"{variantExpr}.AsStringName()",
        "nodepath" => $"{variantExpr}.AsNodePath()",
        "vector2" => $"{variantExpr}.AsVector2()",
        "vector3" => $"{variantExpr}.AsVector3()",
        "color" => $"{variantExpr}.AsColor()",
        "object" => $"({objectType}?){variantExpr}.AsGodotObject()",
        "enum" => $"({objectType}){variantExpr}.AsInt64()",
        "variant" => $"{variantExpr}.Copy()",
        "garray" => $"{variantExpr}.AsGodotArray()",
        "gdict" => $"{variantExpr}.AsGodotDictionary()",
        _ => throw new InvalidOperationException(key),
    };

    private static string SignalArgExpr(string key, string? objectType, int index) => key switch
    {
        "bool" => $"Godot.NativeInterop.SignalArg.Bool(__a, {index})",
        "int32" => $"Godot.NativeInterop.SignalArg.Int32(__a, {index})",
        "int64" => $"Godot.NativeInterop.SignalArg.Int64(__a, {index})",
        "single" => $"Godot.NativeInterop.SignalArg.Single(__a, {index})",
        "double" => $"Godot.NativeInterop.SignalArg.Double(__a, {index})",
        "string" => $"Godot.NativeInterop.SignalArg.StringOf(__a, {index})",
        "stringname" => $"Godot.NativeInterop.SignalArg.StringNameAt(__a, {index})",
        "nodepath" => $"Godot.NativeInterop.SignalArg.NodePathAt(__a, {index})",
        "vector2" => $"Godot.NativeInterop.SignalArg.Vector2At(__a, {index})",
        "vector3" => $"Godot.NativeInterop.SignalArg.Vector3At(__a, {index})",
        "color" => $"Godot.NativeInterop.SignalArg.ColorAt(__a, {index})",
        "object" => $"Godot.NativeInterop.SignalArg.Object<{objectType}>(__a, {index})",
        "enum" => $"({objectType})Godot.NativeInterop.SignalArg.Int64(__a, {index})",
        "variant" => $"Godot.NativeInterop.SignalArg.VariantAt(__a, {index})",
        "garray" => $"Godot.NativeInterop.SignalArg.ArrayAt(__a, {index})",
        "gdict" => $"Godot.NativeInterop.SignalArg.DictionaryAt(__a, {index})",
        _ => throw new InvalidOperationException(key),
    };

    private static readonly Dictionary<string, DiagnosticDescriptor> Descriptors = new()
    {
        ["TDOG001"] = new("TDOG001", "Unsupported export or signal parameter type", "{0}", "twodog", DiagnosticSeverity.Warning, true),
        ["TDOG002"] = new("TDOG002", "Godot class must be a top-level partial class", "{0}", "twodog", DiagnosticSeverity.Warning, true),
        ["TDOG003"] = new("TDOG003", "Invalid [Signal] delegate", "{0}", "twodog", DiagnosticSeverity.Warning, true),
        ["TDOG004"] = new("TDOG004", "Invalid [Export] member", "{0}", "twodog", DiagnosticSeverity.Warning, true),
    };

    private static void Emit(SourceProductionContext spc, ClassInfo info)
    {
        foreach (var d in info.Diagnostics)
        {
            spc.ReportDiagnostic(Diagnostic.Create(Descriptors[d.Id], d.Location ?? Location.None, d.Message));
        }
        if (!info.ShouldEmit) return;

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated by twodog.generators/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        if (info.Namespace is not null) sb.AppendLine($"namespace {info.Namespace};");
        sb.AppendLine();
        sb.AppendLine($"partial class {info.ClassName}");
        sb.AppendLine("{");

        // __BindMembers: invoked by ClassRegistry.Register<T> via reflection.
        sb.AppendLine("    internal static void __BindMembers(Godot.NativeInterop.MemberRegistry r)");
        sb.AppendLine("    {");
        foreach (var e in info.Exports)
        {
            var get = ToVariantExpr(e.TypeKey, $"(({info.ClassName})__o).{e.MemberName}");
            var set = FromVariantExpr(e.TypeKey, e.ObjectType, "__v");
            // Variant-typed properties need NIL_IS_VARIANT so the engine treats
            // the NIL declared type as "any variant".
            var usage = e.TypeKey == "variant"
                ? "(long)(Godot.PropertyUsageFlags.Storage | Godot.PropertyUsageFlags.Editor | Godot.PropertyUsageFlags.NilIsVariant)"
                : "6";
            sb.AppendLine($"        r.Property(\"{e.GdName}\", {VariantTypeOf(e.TypeKey)},");
            sb.AppendLine($"            static __o => {get},");
            sb.AppendLine($"            static (__o, __v) => (({info.ClassName})__o).{e.MemberName} = {set},");
            sb.AppendLine($"            {e.Hint}, \"{Escape(e.HintString)}\", {usage}, \"{Escape(e.GdClassName)}\");");
        }
        foreach (var s in info.Signals)
        {
            var args = string.Join(", ",
                s.Params.Select(p => $"(\"{ToSnake(p.ParamName)}\", {VariantTypeOf(p.TypeKey)}, \"{Escape(p.GdClassName)}\")"));
            sb.AppendLine($"        r.Signal(\"{s.GdName}\"{(s.Params.Count > 0 ? ", " + args : "")});");
        }
        sb.AppendLine("    }");

        // Signal sugar: event + EmitSignalX, matching the delegate's accessibility.
        foreach (var s in info.Signals)
        {
            var decodes = string.Join(", ", s.Params.Select((p, i) => SignalArgExpr(p.TypeKey, p.ObjectType, i)));
            var trampoline = $"static (__d, __a, __n) => (({s.DelegateName})__d)({decodes})";
            sb.AppendLine();
            sb.AppendLine($"    {s.Accessibility} event {s.DelegateName} {s.EventName}");
            sb.AppendLine("    {");
            sb.AppendLine($"        add => Connect(\"{s.GdName}\", Godot.Callable.FromSignalHandler(value, {trampoline}));");
            sb.AppendLine($"        remove => Disconnect(\"{s.GdName}\", Godot.Callable.FromSignalHandler(value, {trampoline}));");
            sb.AppendLine("    }");

            var paramList = string.Join(", ", s.Params.Select(p => $"{p.CsType} {p.ParamName}"));
            sb.AppendLine();
            sb.AppendLine($"    {s.Accessibility} void EmitSignal{s.EventName}({paramList})");
            sb.AppendLine("    {");
            for (var i = 0; i < s.Params.Count; i++)
            {
                sb.AppendLine($"        using Godot.Variant __v{i} = {ToVariantExpr(s.Params[i].TypeKey, s.Params[i].ParamName)};");
            }
            var emitArgs = string.Join(", ", s.Params.Select((_, i) => $"__v{i}"));
            sb.AppendLine($"        EmitSignal(\"{s.GdName}\"{(s.Params.Count > 0 ? ", " + emitArgs : "")});");
            sb.AppendLine("    }");
        }

        sb.AppendLine("}");

        var hint = (info.Namespace is null ? "" : info.Namespace.Replace('.', '_') + "_") + info.ClassName;
        spc.AddSource($"{hint}.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
