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
/// Generated code is safe C# (no unsafe) - pointer work happens in
/// Godot.NativeInterop.SignalArg / MemberRegistry.
/// </summary>
[Generator]
public sealed class ExportSignalGenerator : IIncrementalGenerator
{
    private sealed record ExportInfo(string MemberName, string GdName, string TypeKey, string? ObjectType, long Hint, string HintString);

    private sealed record SignalInfo(string DelegateName, string EventName, string GdName,
        List<(string ParamName, string TypeKey, string? ObjectType, string CsType)> Params);

    private sealed record ClassInfo(string? Namespace, string ClassName, string Accessibility,
        List<ExportInfo> Exports, List<SignalInfo> Signals, List<string> Diagnostics);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classes = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { AttributeLists.Count: >= 0 } cds &&
                                    cds.Modifiers.Any(SyntaxKind.PartialKeyword) &&
                                    MightHaveMarkers(cds),
                static (ctx, _) => Collect(ctx))
            .Where(static c => c is not null);

        context.RegisterSourceOutput(classes, static (spc, info) => Emit(spc, info!));
    }

    private static bool MightHaveMarkers(ClassDeclarationSyntax cds)
    {
        foreach (var member in cds.Members)
        {
            foreach (var list in member.AttributeLists)
            {
                foreach (var attr in list.Attributes)
                {
                    var name = attr.Name.ToString();
                    if (name is "Export" or "ExportAttribute" or "Godot.Export" or
                        "Signal" or "SignalAttribute" or "Godot.Signal")
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private static ClassInfo? Collect(GeneratorSyntaxContext ctx)
    {
        var cds = (ClassDeclarationSyntax)ctx.Node;
        if (ctx.SemanticModel.GetDeclaredSymbol(cds) is not INamedTypeSymbol symbol) return null;

        var exports = new List<ExportInfo>();
        var signals = new List<SignalInfo>();
        var diagnostics = new List<string>();

        foreach (var member in symbol.GetMembers())
        {
            switch (member)
            {
                case IPropertySymbol { IsStatic: false } prop when HasAttribute(prop, "Godot.ExportAttribute"):
                    AddExport(exports, diagnostics, prop.Name, prop.Type, GetExportArgs(prop));
                    break;
                case IFieldSymbol { IsStatic: false, IsImplicitlyDeclared: false } field when HasAttribute(field, "Godot.ExportAttribute"):
                    AddExport(exports, diagnostics, field.Name, field.Type, GetExportArgs(field));
                    break;
                case INamedTypeSymbol { TypeKind: TypeKind.Delegate } del when HasAttribute(del, "Godot.SignalAttribute"):
                    AddSignal(signals, diagnostics, del);
                    break;
            }
        }

        if (exports.Count == 0 && signals.Count == 0) return null;

        return new ClassInfo(
            symbol.ContainingNamespace.IsGlobalNamespace ? null : symbol.ContainingNamespace.ToDisplayString(),
            symbol.Name,
            symbol.DeclaredAccessibility == Accessibility.Public ? "public" : "internal",
            exports, signals, diagnostics);
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

    private static void AddExport(List<ExportInfo> exports, List<string> diagnostics,
        string name, ITypeSymbol type, (long hint, string hintString) args)
    {
        var (key, objectType) = ClassifyType(type);
        if (key is null)
        {
            diagnostics.Add($"[Export] {name}: type '{type.ToDisplayString()}' is not supported by the twodog source generator yet.");
            return;
        }
        exports.Add(new ExportInfo(name, ToSnake(name), key, objectType, args.hint, args.hintString));
    }

    private static void AddSignal(List<SignalInfo> signals, List<string> diagnostics, INamedTypeSymbol del)
    {
        if (!del.Name.EndsWith("EventHandler", StringComparison.Ordinal))
        {
            diagnostics.Add($"[Signal] {del.Name}: signal delegates must be named *EventHandler.");
            return;
        }
        var invoke = del.DelegateInvokeMethod;
        if (invoke is null || !invoke.ReturnsVoid)
        {
            diagnostics.Add($"[Signal] {del.Name}: signal delegates must return void.");
            return;
        }

        var eventName = del.Name.Substring(0, del.Name.Length - "EventHandler".Length);
        var ps = new List<(string, string, string?, string)>();
        foreach (var p in invoke.Parameters)
        {
            var (key, objectType) = ClassifyType(p.Type);
            if (key is null)
            {
                diagnostics.Add($"[Signal] {del.Name}: parameter '{p.Name}' type '{p.Type.ToDisplayString()}' is not supported yet.");
                return;
            }
            ps.Add((p.Name, key, objectType, p.Type.ToDisplayString()));
        }
        signals.Add(new SignalInfo(del.Name, eventName, ToSnake(eventName), ps));
    }

    /// <summary>Maps a C# type to a marshalling key (+ object type for GodotObject-derived).</summary>
    private static (string? key, string? objectType) ClassifyType(ITypeSymbol type)
    {
        switch (type.ToDisplayString().TrimEnd('?'))
        {
            case "bool": return ("bool", null);
            case "int": return ("int32", null);
            case "long": return ("int64", null);
            case "float": return ("single", null);
            case "double": return ("double", null);
            case "string": return ("string", null);
            case "Godot.Vector2": return ("vector2", null);
            case "Godot.Vector3": return ("vector3", null);
            case "Godot.Color": return ("color", null);
        }
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (t.ToDisplayString() == "Godot.GodotObject")
                return ("object", type.ToDisplayString().TrimEnd('?'));
        }
        return (null, null);
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
        "int32" or "int64" => "Godot.VariantType.Int",
        "single" or "double" => "Godot.VariantType.Float",
        "string" => "Godot.VariantType.String",
        "vector2" => "Godot.VariantType.Vector2",
        "vector3" => "Godot.VariantType.Vector3",
        "color" => "Godot.VariantType.Color",
        "object" => "Godot.VariantType.Object",
        _ => throw new InvalidOperationException(key),
    };

    private static string ToVariantExpr(string key, string expr) => key switch
    {
        "object" => $"Godot.Variant.From({expr})",
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
        "vector2" => $"{variantExpr}.AsVector2()",
        "vector3" => $"{variantExpr}.AsVector3()",
        "color" => $"{variantExpr}.AsColor()",
        "object" => $"({objectType}?){variantExpr}.AsGodotObject()",
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
        "vector2" => $"Godot.NativeInterop.SignalArg.Vector2At(__a, {index})",
        "vector3" => $"Godot.NativeInterop.SignalArg.Vector3At(__a, {index})",
        "color" => $"Godot.NativeInterop.SignalArg.ColorAt(__a, {index})",
        "object" => $"Godot.NativeInterop.SignalArg.Object<{objectType}>(__a, {index})",
        _ => throw new InvalidOperationException(key),
    };

    private static void Emit(SourceProductionContext spc, ClassInfo info)
    {
        foreach (var d in info.Diagnostics)
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor("TDOG001", "Unsupported member", d, "twodog", DiagnosticSeverity.Warning, true),
                Location.None));
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated by twodog.generators/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        if (info.Namespace is not null) sb.AppendLine($"namespace {info.Namespace};");
        sb.AppendLine();
        sb.AppendLine($"{info.Accessibility} partial class {info.ClassName}");
        sb.AppendLine("{");

        // __BindMembers: invoked by ClassRegistry.Register<T> via reflection.
        sb.AppendLine("    internal static void __BindMembers(Godot.NativeInterop.MemberRegistry r)");
        sb.AppendLine("    {");
        foreach (var e in info.Exports)
        {
            var get = ToVariantExpr(e.TypeKey, $"(({info.ClassName})__o).{e.MemberName}");
            var set = FromVariantExpr(e.TypeKey, e.ObjectType, "__v");
            sb.AppendLine($"        r.Property(\"{e.GdName}\", {VariantTypeOf(e.TypeKey)},");
            sb.AppendLine($"            static __o => {get},");
            sb.AppendLine($"            static (__o, __v) => (({info.ClassName})__o).{e.MemberName} = {set},");
            sb.AppendLine($"            {e.Hint}, \"{Escape(e.HintString)}\");");
        }
        foreach (var s in info.Signals)
        {
            var args = string.Join(", ", s.Params.Select(p => $"(\"{ToSnake(p.ParamName)}\", {VariantTypeOf(p.TypeKey)})"));
            sb.AppendLine($"        r.Signal(\"{s.GdName}\"{(s.Params.Count > 0 ? ", " + args : "")});");
        }
        sb.AppendLine("    }");

        // Signal sugar: event + EmitSignalX.
        foreach (var s in info.Signals)
        {
            var decodes = string.Join(", ", s.Params.Select((p, i) => SignalArgExpr(p.TypeKey, p.ObjectType, i)));
            var trampoline = $"static (__d, __a, __n) => (({s.DelegateName})__d)({decodes})";
            sb.AppendLine();
            sb.AppendLine($"    public event {s.DelegateName} {s.EventName}");
            sb.AppendLine("    {");
            sb.AppendLine($"        add => Connect(\"{s.GdName}\", Godot.Callable.FromSignalHandler(value, {trampoline}));");
            sb.AppendLine($"        remove => Disconnect(\"{s.GdName}\", Godot.Callable.FromSignalHandler(value, {trampoline}));");
            sb.AppendLine("    }");

            var paramList = string.Join(", ", s.Params.Select(p => $"{p.CsType} {p.ParamName}"));
            sb.AppendLine();
            sb.AppendLine($"    public void EmitSignal{s.EventName}({paramList})");
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
