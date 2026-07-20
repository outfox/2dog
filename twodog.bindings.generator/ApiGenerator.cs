using System.Text;
using System.Text.Json;

namespace twodog.bindings.generator;

/// <summary>
/// Generates the typed Godot API (classes, enums, singletons, registry) from
/// extension_api.json. V1 emits every non-virtual, non-vararg method whose
/// signature uses only supported types; coverage grows as the type support
/// grows. Unsupported: Variant, Array/Dictionary/Packed*, Callable, Signal,
/// NodePath, typed arrays, native-struct pointers, builtin-class enums.
/// </summary>
public static class ApiGenerator
{
    private static readonly Dictionary<string, string> MathMap = new()
    {
        ["Vector2"] = "Vector2", ["Vector2i"] = "Vector2I", ["Rect2"] = "Rect2", ["Rect2i"] = "Rect2I",
        ["Vector3"] = "Vector3", ["Vector3i"] = "Vector3I", ["Transform2D"] = "Transform2D",
        ["Vector4"] = "Vector4", ["Vector4i"] = "Vector4I", ["Plane"] = "Plane",
        ["Quaternion"] = "Quaternion", ["AABB"] = "Aabb", ["Basis"] = "Basis",
        ["Transform3D"] = "Transform3D", ["Projection"] = "Projection", ["Color"] = "Color", ["RID"] = "Rid",
    };

    private static readonly HashSet<string> Keywords =
    [
        "string", "object", "base", "params", "event", "class", "struct", "enum", "interface",
        "ref", "out", "in", "int", "uint", "long", "ulong", "byte", "sbyte", "short", "ushort",
        "float", "double", "bool", "char", "void", "default", "operator", "lock", "fixed", "internal",
        "new", "this", "checked", "override", "static", "delegate",
    ];

    // Members that clash with System.Object / the handwritten GodotObject baseline.
    private static readonly HashSet<string> ReservedMembers =
    [
        "ToString", "GetType", "Equals", "GetHashCode", "Finalize", "MemberwiseClone",
        "Dispose", "Free", "NativePtr", "InstanceId", "IsRefCounted", "IsValid", "Singleton",
    ];

    private sealed record ClassInfo(string GdName, string CsName, string BaseCs, bool RefCounted, bool Instantiable);

    private sealed record TypeRef(string Kind, string Cs)
    {
        public static readonly TypeRef Void = new("void", "void");
    }

    private static Dictionary<string, ClassInfo> _classes = [];
    private static Dictionary<string, string> _enumMap = [];   // godot ref ("Error", "Node.InternalMode") -> cs name
    private static int _emitted, _skipped;

    public static void Run(string apiJsonPath, string outDir)
    {
        var root = JsonDocument.Parse(File.ReadAllText(apiJsonPath)).RootElement;
        Directory.CreateDirectory(outDir);

        // ---- collect classes ----
        _classes = [];
        foreach (var c in root.GetProperty("classes").EnumerateArray())
        {
            var gd = c.GetProperty("name").GetString()!;
            var cs = gd == "Object" ? "GodotObject" : gd;
            var baseGd = c.TryGetProperty("inherits", out var inh) ? inh.GetString()! : "";
            var baseCs = baseGd == "" ? "" : baseGd == "Object" ? "GodotObject" : baseGd;
            _classes[gd] = new ClassInfo(gd, cs, baseCs,
                c.GetProperty("is_refcounted").GetBoolean(),
                c.GetProperty("is_instantiable").GetBoolean());
        }

        // ---- enum name map ----
        _enumMap = [];
        foreach (var e in root.GetProperty("global_enums").EnumerateArray())
        {
            var name = e.GetProperty("name").GetString()!;
            _enumMap[name] = name.Replace(".", ""); // Variant.Type -> VariantType
        }
        foreach (var c in root.GetProperty("classes").EnumerateArray())
        {
            if (!c.TryGetProperty("enums", out var enums)) continue;
            var owner = _classes[c.GetProperty("name").GetString()!];
            foreach (var e in enums.EnumerateArray())
            {
                var en = e.GetProperty("name").GetString()!;
                _enumMap[$"{owner.GdName}.{en}"] = $"{owner.CsName}.{en}";
            }
        }

        // ---- singleton map: type -> registered name ----
        var singletons = new Dictionary<string, string>();
        foreach (var s in root.GetProperty("singletons").EnumerateArray())
            singletons[s.GetProperty("type").GetString()!] = s.GetProperty("name").GetString()!;

        // ---- global enums file ----
        var sbGlobal = NewFile();
        foreach (var e in root.GetProperty("global_enums").EnumerateArray())
            EmitEnum(sbGlobal, e, indent: "", nested: false);
        Write(outDir, "GlobalEnums.gen.cs", sbGlobal);

        // ---- classes, chunked by first letter ----
        _emitted = 0;
        _skipped = 0;
        var chunks = new Dictionary<char, StringBuilder>();
        foreach (var c in root.GetProperty("classes").EnumerateArray())
        {
            var info = _classes[c.GetProperty("name").GetString()!];
            var chunk = chunks.TryGetValue(info.CsName[0], out var sb) ? sb : chunks[info.CsName[0]] = NewFile();
            EmitClass(chunk, c, info, singletons);
        }
        foreach (var (letter, sb) in chunks)
            Write(outDir, $"Api_{letter}.gen.cs", sb);

        // ---- registry ----
        var sbReg = NewFile();
        sbReg.AppendLine("public static class ApiRegistry");
        sbReg.AppendLine("{");
        sbReg.AppendLine("    internal static readonly Dictionary<string, (Func<nint, bool, GodotObject> Factory, bool RefCounted)> Entries = new(1200);");
        sbReg.AppendLine();
        var groups = _classes.Values.GroupBy(i => i.CsName[0]).OrderBy(g => g.Key).ToList();
        sbReg.AppendLine("    static ApiRegistry()");
        sbReg.AppendLine("    {");
        foreach (var g in groups)
            sbReg.AppendLine($"        Init{g.Key}();");
        sbReg.AppendLine("    }");
        foreach (var g in groups)
        {
            sbReg.AppendLine();
            sbReg.AppendLine($"    private static void Init{g.Key}()");
            sbReg.AppendLine("    {");
            foreach (var i in g)
                sbReg.AppendLine($"        Entries[\"{i.GdName}\"] = (static (p, rc) => new {i.CsName}(p, rc), {Bool(i.RefCounted)});");
            sbReg.AppendLine("    }");
        }
        sbReg.AppendLine("}");
        Write(outDir, "ApiRegistry.gen.cs", sbReg);

        Console.WriteLine($"Generated typed API: {_classes.Count} classes, {_emitted} methods emitted, {_skipped} skipped (unsupported types), into {outDir}");
    }

    // ------------------------------------------------------------- class --

    private static void EmitClass(StringBuilder sb, JsonElement c, ClassInfo info, Dictionary<string, string> singletons)
    {
        var isObject = info.CsName == "GodotObject";
        sb.AppendLine(isObject || info.BaseCs == ""
            ? $"public unsafe partial class {info.CsName}"
            : $"public unsafe partial class {info.CsName} : {info.BaseCs}");
        sb.AppendLine("{");

        var used = new HashSet<string> { info.CsName };

        if (!isObject)
        {
            sb.AppendLine($"    internal {info.CsName}(nint ptr, bool rc) : base(ptr, rc) {{ }}");
            if (info.Instantiable)
            {
                sb.AppendLine();
                sb.AppendLine($"    public {info.CsName}() : this(InstanceBindings.ConstructRaw(\"{info.GdName}\"), {Bool(info.RefCounted)})");
                sb.AppendLine("    {");
                sb.AppendLine("        InstanceBindings.Attach(this);");
                sb.AppendLine("    }");
            }
        }

        if (singletons.TryGetValue(info.GdName, out var singletonName))
        {
            sb.AppendLine();
            sb.AppendLine($"    private static {info.CsName}? _singleton;");
            sb.AppendLine($"    public static {info.CsName} Singleton => _singleton ??= ({info.CsName})InstanceBindings.GetOrCreate(InstanceBindings.GetSingletonPtr(\"{singletonName}\"), adoptRef: false)!;");
            used.Add("_singleton");
        }

        if (c.TryGetProperty("enums", out var enums))
        {
            foreach (var e in enums.EnumerateArray())
            {
                sb.AppendLine();
                EmitEnum(sb, e, indent: "    ", nested: true);
                used.Add(e.GetProperty("name").GetString()!);
            }
        }

        if (c.TryGetProperty("methods", out var methods))
        {
            foreach (var m in methods.EnumerateArray())
                EmitMethod(sb, m, info, used);
        }

        sb.AppendLine("}");
        sb.AppendLine();
    }

    // ------------------------------------------------------------- enums --

    private static void EmitEnum(StringBuilder sb, JsonElement e, string indent, bool nested)
    {
        var gd = e.GetProperty("name").GetString()!;
        var cs = nested ? gd : gd.Replace(".", "");
        var flags = e.TryGetProperty("is_bitfield", out var bf) && bf.GetBoolean();
        if (flags) sb.AppendLine($"{indent}[Flags]");
        sb.AppendLine($"{indent}public enum {cs} : long");
        sb.AppendLine($"{indent}{{");
        foreach (var v in e.GetProperty("values").EnumerateArray())
            sb.AppendLine($"{indent}    {v.GetProperty("name").GetString()} = {v.GetProperty("value").GetInt64()},");
        sb.AppendLine($"{indent}}}");
    }

    // ----------------------------------------------------------- methods --

    private static void EmitMethod(StringBuilder sb, JsonElement m, ClassInfo info, HashSet<string> used)
    {
        var gdName = m.GetProperty("name").GetString()!;
        if ((m.TryGetProperty("is_virtual", out var v) && v.GetBoolean()) ||
            (m.TryGetProperty("is_vararg", out var va) && va.GetBoolean()))
        {
            return; // virtuals arrive in phase 3; varargs need variant calls
        }

        var csName = Pascal(gdName);
        if (ReservedMembers.Contains(csName) || !used.Add(csName)) { _skipped++; return; }

        var ret = m.TryGetProperty("return_value", out var rv)
            ? Map(rv.GetProperty("type").GetString()!, rv.TryGetProperty("meta", out var rm) ? rm.GetString() : null)
            : TypeRef.Void;
        if (ret is null) { _skipped++; return; }

        var args = new List<(string name, TypeRef type)>();
        if (m.TryGetProperty("arguments", out var margs))
        {
            foreach (var a in margs.EnumerateArray())
            {
                var t = Map(a.GetProperty("type").GetString()!, a.TryGetProperty("meta", out var am) ? am.GetString() : null);
                if (t is null) { _skipped++; return; }
                args.Add((Camel(a.GetProperty("name").GetString()!), t));
            }
        }

        var isStatic = m.TryGetProperty("is_static", out var st) && st.GetBoolean();
        var hash = m.GetProperty("hash").GetInt64();
        var mbField = $"__mb_{gdName}";
        _emitted++;

        sb.AppendLine();
        sb.AppendLine($"    private static nint {mbField};");
        var paramList = string.Join(", ", args.Select(a => $"{ParamType(a.type)} {a.name}"));
        sb.AppendLine($"    public {(isStatic ? "static " : "")}{RetType(ret)} {csName}({paramList})");
        sb.AppendLine("    {");
        sb.AppendLine($"        var __mb = {mbField};");
        sb.AppendLine("        if (__mb == 0)");
        sb.AppendLine("        {");
        sb.AppendLine($"            __mb = MethodBinds.Resolve(\"{info.GdName}\", \"{gdName}\", {hash});");
        sb.AppendLine($"            if (__mb == 0) throw new MissingMethodException(\"{info.GdName}.{gdName} is not available in this engine build.\");");
        sb.AppendLine($"            {mbField} = __mb;");
        sb.AppendLine("        }");

        // encode args
        for (var i = 0; i < args.Count; i++)
        {
            var (name, t) = args[i];
            sb.AppendLine("        " + t.Kind switch
            {
                "bool" => $"byte __a{i} = {name} ? (byte)1 : (byte)0;",
                "int" => $"long __a{i} = unchecked((long){name});",
                "float" => $"double __a{i} = {name};",
                "enum" => $"long __a{i} = (long){name};",
                "string" => $"ulong __a{i} = NativeString.Create({name});",
                "stringname" => $"ulong __a{i} = StringNames.Get({name}).Opaque;",
                "math" => $"var __a{i} = {name};",
                "class" => $"nint __a{i} = {name}?.NativePtr ?? 0;",
                _ => throw new InvalidOperationException(t.Kind),
            });
        }

        if (args.Count > 0)
        {
            sb.AppendLine($"        var __args = stackalloc nint[{args.Count}];");
            for (var i = 0; i < args.Count; i++)
                sb.AppendLine($"        __args[{i}] = (nint)(&__a{i});");
        }

        var self = isStatic ? "0" : "NativePtr";
        var argsPtr = args.Count > 0 ? "(nint)__args" : "0";

        // return slot + call + decode
        switch (ret.Kind)
        {
            case "void":
                sb.AppendLine($"        GdExtensionInterface.ObjectMethodBindPtrcall(__mb, {self}, {argsPtr}, 0);");
                break;
            case "bool":
                sb.AppendLine("        byte __ret = 0;");
                sb.AppendLine($"        GdExtensionInterface.ObjectMethodBindPtrcall(__mb, {self}, {argsPtr}, (nint)(&__ret));");
                break;
            case "int":
            case "enum":
                sb.AppendLine("        long __ret = 0;");
                sb.AppendLine($"        GdExtensionInterface.ObjectMethodBindPtrcall(__mb, {self}, {argsPtr}, (nint)(&__ret));");
                break;
            case "float":
                sb.AppendLine("        double __ret = 0;");
                sb.AppendLine($"        GdExtensionInterface.ObjectMethodBindPtrcall(__mb, {self}, {argsPtr}, (nint)(&__ret));");
                break;
            case "string":
            case "stringname":
                sb.AppendLine("        ulong __ret = 0;");
                sb.AppendLine($"        GdExtensionInterface.ObjectMethodBindPtrcall(__mb, {self}, {argsPtr}, (nint)(&__ret));");
                break;
            case "math":
                sb.AppendLine($"        var __ret = default({ret.Cs});");
                sb.AppendLine($"        GdExtensionInterface.ObjectMethodBindPtrcall(__mb, {self}, {argsPtr}, (nint)(&__ret));");
                break;
            case "class":
                // MUST be zero-initialized: for RefCounted returns the engine
                // assigns into this slot as a Ref<T> (unrefs previous content).
                sb.AppendLine("        nint __ret = 0;");
                sb.AppendLine($"        GdExtensionInterface.ObjectMethodBindPtrcall(__mb, {self}, {argsPtr}, (nint)(&__ret));");
                break;
        }

        // release owned string args
        for (var i = 0; i < args.Count; i++)
            if (args[i].type.Kind == "string")
                sb.AppendLine($"        NativeString.Destroy(ref __a{i});");

        switch (ret.Kind)
        {
            case "bool": sb.AppendLine("        return __ret != 0;"); break;
            case "int": sb.AppendLine($"        return unchecked(({ret.Cs})__ret);"); break;
            case "enum": sb.AppendLine($"        return ({ret.Cs})__ret;"); break;
            case "float": sb.AppendLine($"        return ({ret.Cs})__ret;"); break;
            case "string": sb.AppendLine("        return NativeString.ReadAndDestroy(ref __ret);"); break;
            case "stringname": sb.AppendLine("        return StringNames.ReadAndDestroy(ref __ret);"); break;
            case "math": sb.AppendLine("        return __ret;"); break;
            case "class":
                var refCounted = _classes.TryGetValue(GdOf(ret.Cs), out var rc2) && rc2.RefCounted;
                sb.AppendLine($"        return ({ret.Cs}?)InstanceBindings.GetOrCreate(__ret, adoptRef: {Bool(refCounted)});");
                break;
        }

        sb.AppendLine("    }");
    }

    private static string GdOf(string cs) => cs == "GodotObject" ? "Object" : cs;

    // -------------------------------------------------------- type mapping --

    private static TypeRef? Map(string t, string? meta)
    {
        if (t == "void") return TypeRef.Void;
        if (t == "bool") return new TypeRef("bool", "bool");
        if (t == "int")
        {
            var cs = meta switch
            {
                "int8" => "sbyte", "int16" => "short", "int32" => "int", "int64" => "long",
                "uint8" => "byte", "uint16" => "ushort", "uint32" => "uint", "uint64" => "ulong",
                "char16" => "char", "char32" => "int",
                _ => "long",
            };
            return new TypeRef("int", cs);
        }
        if (t == "float")
            return new TypeRef("float", meta == "float" ? "float" : "double");
        if (t == "String") return new TypeRef("string", "string");
        if (t == "StringName") return new TypeRef("stringname", "string");
        if (t.StartsWith("enum::") || t.StartsWith("bitfield::"))
        {
            var refName = t[(t.IndexOf(':') + 2)..];
            return _enumMap.TryGetValue(refName, out var cs) ? new TypeRef("enum", cs) : null;
        }
        if (MathMap.TryGetValue(t, out var math)) return new TypeRef("math", math);
        if (_classes.TryGetValue(t, out var cls)) return new TypeRef("class", cls.CsName);
        return null; // Variant, containers, callables, node paths, pointers, ...
    }

    private static string ParamType(TypeRef t) => t.Kind == "class" ? t.Cs + "?" : t.Cs;
    private static string RetType(TypeRef t) => t.Kind == "class" ? t.Cs + "?" : t.Cs;

    // ---------------------------------------------------------- utilities --

    private static string Bool(bool b) => b ? "true" : "false";

    private static string Pascal(string snake) =>
        string.Concat(snake.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

    private static string Camel(string snake)
    {
        var p = Pascal(snake);
        var c = char.ToLowerInvariant(p[0]) + p[1..];
        return Keywords.Contains(c) ? "@" + c : c;
    }

    private static StringBuilder NewFile()
    {
        var sb = new StringBuilder(1 << 20);
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine("// Generated by twodog.bindings.generator (api mode) from extension_api.json.");
        sb.AppendLine("// Do not edit by hand - regenerate instead.");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine();
        sb.AppendLine("// ReSharper disable All");
        sb.AppendLine("#pragma warning disable CS0109, CS0108, CS1591, CA1069");
        sb.AppendLine();
        sb.AppendLine("using Godot.NativeInterop;");
        sb.AppendLine();
        sb.AppendLine("namespace Godot;");
        sb.AppendLine();
        return sb;
    }

    private static void Write(string outDir, string file, StringBuilder sb) =>
        File.WriteAllText(Path.Combine(outDir, file), sb.ToString());
}
