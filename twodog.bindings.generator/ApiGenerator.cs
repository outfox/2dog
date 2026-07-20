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

    private sealed record VirtualInfo(string GdName, string CsName, TypeRef Ret, List<(string name, TypeRef type)> Args);

    private sealed record PropInfo(string CsName, string GetterCs, string? SetterCs, string? IndexCast, TypeRef Type, string? SetterCast);

    private static Dictionary<string, ClassInfo> _classes = [];
    private static Dictionary<string, string> _enumMap = [];   // godot ref ("Error", "Node.InternalMode") -> cs name
    private static Dictionary<string, JsonElement> _classJson = [];
    private static Dictionary<string, Dictionary<string, JsonElement>> _methodsByClass = [];
    private static Dictionary<string, List<PropInfo>> _propsByClass = [];
    private static readonly HashSet<(string cls, string method)> _internalAccessors = [];
    private static readonly Dictionary<(string cls, string enumName), string> _enumRenames = [];
    private static readonly SortedDictionary<string, string> _virtualNameMap = []; // gd virtual name -> cs stub name
    private static int _emitted, _skipped, _virtualsEmitted, _virtualsSkipped, _propsEmitted, _propsSkipped;

    public static void Run(string apiJsonPath, string outDir)
    {
        var root = JsonDocument.Parse(File.ReadAllText(apiJsonPath)).RootElement;
        Directory.CreateDirectory(outDir);

        // ---- collect classes ----
        _classes = [];
        _classJson = [];
        _methodsByClass = [];
        foreach (var c in root.GetProperty("classes").EnumerateArray())
        {
            var gd = c.GetProperty("name").GetString()!;
            var cs = gd == "Object" ? "GodotObject" : gd;
            var baseGd = c.TryGetProperty("inherits", out var inh) ? inh.GetString()! : "";
            var baseCs = baseGd == "" ? "" : baseGd == "Object" ? "GodotObject" : baseGd;
            _classes[gd] = new ClassInfo(gd, cs, baseCs,
                c.GetProperty("is_refcounted").GetBoolean(),
                c.GetProperty("is_instantiable").GetBoolean());
            _classJson[gd] = c;
            var methods = new Dictionary<string, JsonElement>();
            if (c.TryGetProperty("methods", out var ms))
            {
                foreach (var m in ms.EnumerateArray())
                    methods[m.GetProperty("name").GetString()!] = m;
            }
            _methodsByClass[gd] = methods;
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

        // ---- properties: enum renames first (before any Map() call), then
        //      full resolution against getter/setter signatures ----
        _propsByClass = [];
        _internalAccessors.Clear();
        _enumRenames.Clear();
        _propsEmitted = 0;
        _propsSkipped = 0;
        PrescanEnumRenames();
        ResolveProperties();

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
        _virtualsEmitted = 0;
        _virtualsSkipped = 0;
        _virtualNameMap.Clear();
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

        // ---- global virtual-name map (gd name -> generated stub name) ----
        var sbVirt = NewFile();
        sbVirt.AppendLine("public static class GeneratedVirtualNames");
        sbVirt.AppendLine("{");
        sbVirt.AppendLine($"    public static readonly Dictionary<string, string> Map = new({_virtualNameMap.Count + 64});");
        sbVirt.AppendLine();
        var virtChunks = _virtualNameMap.Chunk(250).ToList();
        sbVirt.AppendLine("    static GeneratedVirtualNames()");
        sbVirt.AppendLine("    {");
        for (var i = 0; i < virtChunks.Count; i++)
            sbVirt.AppendLine($"        Init{i}();");
        sbVirt.AppendLine("    }");
        for (var i = 0; i < virtChunks.Count; i++)
        {
            sbVirt.AppendLine();
            sbVirt.AppendLine($"    private static void Init{i}()");
            sbVirt.AppendLine("    {");
            foreach (var kv in virtChunks[i])
                sbVirt.AppendLine($"        Map[\"{kv.Key}\"] = \"{kv.Value}\";");
            sbVirt.AppendLine("    }");
        }
        sbVirt.AppendLine("}");
        Write(outDir, "GeneratedVirtualNames.gen.cs", sbVirt);

        Console.WriteLine($"Generated typed API: {_classes.Count} classes, {_emitted} methods emitted, {_skipped} skipped (unsupported types), " +
                          $"{_propsEmitted} properties ({_propsSkipped} skipped, {_enumRenames.Count} enum renames), " +
                          $"{_virtualsEmitted} virtual stubs ({_virtualsSkipped} skipped), into {outDir}");
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
                sb.AppendLine($"    public {info.CsName}() : this(0, {Bool(info.RefCounted)})");
                sb.AppendLine("    {");
                sb.AppendLine($"        ClassRegistry.AttachNew(this, \"{info.GdName}\");");
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
                used.Add(EmitEnum(sb, e, indent: "    ", nested: true, ownerGd: info.GdName));
            }
        }

        EmitProperties(sb, info, used);

        var virtuals = new List<VirtualInfo>();
        if (c.TryGetProperty("methods", out var methods))
        {
            foreach (var m in methods.EnumerateArray())
                EmitMethod(sb, m, info, used, virtuals, isObject);
        }

        EmitVirtuals(sb, virtuals);

        sb.AppendLine("}");
        sb.AppendLine();
    }

    // ------------------------------------------------------------- enums --

    private static string EmitEnum(StringBuilder sb, JsonElement e, string indent, bool nested, string? ownerGd = null)
    {
        var gd = e.GetProperty("name").GetString()!;
        var cs = nested
            ? ownerGd is not null && _enumRenames.TryGetValue((ownerGd, gd), out var renamed) ? renamed : gd
            : gd.Replace(".", "");
        var flags = e.TryGetProperty("is_bitfield", out var bf) && bf.GetBoolean();
        if (flags) sb.AppendLine($"{indent}[Flags]");
        sb.AppendLine($"{indent}public enum {cs} : long");
        sb.AppendLine($"{indent}{{");
        foreach (var v in e.GetProperty("values").EnumerateArray())
            sb.AppendLine($"{indent}    {v.GetProperty("name").GetString()} = {v.GetProperty("value").GetInt64()},");
        sb.AppendLine($"{indent}}}");
        return cs;
    }

    // ----------------------------------------------------------- methods --

    private static void EmitMethod(StringBuilder sb, JsonElement m, ClassInfo info, HashSet<string> used,
        List<VirtualInfo> virtuals, bool isObject)
    {
        var gdName = m.GetProperty("name").GetString()!;
        if (m.TryGetProperty("is_vararg", out var va) && va.GetBoolean())
        {
            return; // varargs need variant calls
        }
        if (m.TryGetProperty("is_virtual", out var v) && v.GetBoolean())
        {
            // Object's virtuals (_get/_set/_get_property_list...) are all
            // Variant-typed and the handwritten GodotObject roots the
            // __CallVirtual chain - skip them there.
            if (!isObject) CollectVirtual(m, gdName, used, virtuals);
            return;
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

        // GodotSharp compat: property accessor methods are hidden from the
        // public surface (the property is the API); internal keeps them
        // callable by generated property bodies across the class hierarchy.
        var visibility = _internalAccessors.Contains((info.GdName, gdName)) ? "internal" : "public";

        sb.AppendLine();
        sb.AppendLine($"    private static nint {mbField};");
        var paramList = string.Join(", ", args.Select(a => $"{ParamType(a.type)} {a.name}"));
        sb.AppendLine($"    {visibility} {(isStatic ? "static " : "")}{RetType(ret)} {csName}({paramList})");
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
                "variant" => $"var __a{i} = {name}.Native;",
                "builtinref" => $"ulong __a{i} = {name}.Native;",
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
            case "variant":
                // Zero-init = NIL; the engine assigns over it (destroys prior).
                sb.AppendLine("        NativeVariant __ret = default;");
                sb.AppendLine($"        GdExtensionInterface.ObjectMethodBindPtrcall(__mb, {self}, {argsPtr}, (nint)(&__ret));");
                break;
            case "builtinref":
                // Zero-init = empty/null handle; engine assigns a ref into it.
                sb.AppendLine("        ulong __ret = 0;");
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
            case "variant": sb.AppendLine("        return new Variant(__ret);"); break;
            case "builtinref": sb.AppendLine($"        return new {ret.Cs}(__ret);"); break;
        }

        sb.AppendLine("    }");
    }

    private static string GdOf(string cs) => cs == "GodotObject" ? "Object" : cs;

    // -------------------------------------------------------- properties --

    private static string PropCsName(string gdPropName) => Pascal(gdPropName.Replace('/', '_'));

    /// <summary>
    /// GodotSharp compat: a nested enum whose name collides with a property of
    /// the same class gets an "Enum" suffix (Node.ProcessMode property vs
    /// Node.ProcessModeEnum). Must run before ANY Map() call resolves enum refs.
    /// </summary>
    private static void PrescanEnumRenames()
    {
        foreach (var (gd, c) in _classJson)
        {
            if (!c.TryGetProperty("enums", out var enums) || !c.TryGetProperty("properties", out var props)) continue;
            var propNames = props.EnumerateArray()
                .Select(p => PropCsName(p.GetProperty("name").GetString()!))
                .ToHashSet();
            foreach (var e in enums.EnumerateArray())
            {
                var en = e.GetProperty("name").GetString()!;
                if (!propNames.Contains(en)) continue;
                _enumRenames[(gd, en)] = en + "Enum";
                _enumMap[$"{gd}.{en}"] = $"{_classes[gd].CsName}.{en}Enum";
            }
        }
    }

    /// <summary>Finds the class in the inheritance chain that declares a method.</summary>
    private static (string cls, JsonElement m)? ResolveMethod(string startClass, string methodName)
    {
        for (var cls = startClass; !string.IsNullOrEmpty(cls); cls = _classJson[cls].TryGetProperty("inherits", out var i) ? i.GetString()! : "")
        {
            if (_methodsByClass.TryGetValue(cls, out var methods) && methods.TryGetValue(methodName, out var m))
                return (cls, m);
            if (!_classJson.ContainsKey(cls)) break;
        }
        return null;
    }

    private static void ResolveProperties()
    {
        foreach (var (gd, c) in _classJson)
        {
            var list = new List<PropInfo>();
            _propsByClass[gd] = list;
            if (!c.TryGetProperty("properties", out var props)) continue;

            foreach (var p in props.EnumerateArray())
            {
                var csName = PropCsName(p.GetProperty("name").GetString()!);
                if (ReservedMembers.Contains(csName)) { _propsSkipped++; continue; }

                var getterName = p.TryGetProperty("getter", out var g) ? g.GetString() : null;
                if (string.IsNullOrEmpty(getterName)) { _propsSkipped++; continue; }
                // Accessors whose Pascal name is reserved (GetType, ToString...)
                // are never emitted, so properties depending on them can't be either.
                if (ReservedMembers.Contains(Pascal(getterName))) { _propsSkipped++; continue; }
                var getter = ResolveMethod(gd, getterName);
                if (getter is null) { _propsSkipped++; continue; }

                long? index = p.TryGetProperty("index", out var ix) ? ix.GetInt64() : null;
                var (getterCls, getterEl) = getter.Value;
                var getterArgs = getterEl.TryGetProperty("arguments", out var ga) ? ga.GetArrayLength() : 0;
                if (getterArgs != (index.HasValue ? 1 : 0)) { _propsSkipped++; continue; }

                var type = getterEl.TryGetProperty("return_value", out var rv)
                    ? Map(rv.GetProperty("type").GetString()!, rv.TryGetProperty("meta", out var rm) ? rm.GetString() : null)
                    : null;
                if (type is null || type.Kind == "void") { _propsSkipped++; continue; }

                string? indexCast = null;
                if (index.HasValue)
                {
                    var idxType = Map(ga[0].GetProperty("type").GetString()!,
                        ga[0].TryGetProperty("meta", out var im) ? im.GetString() : null);
                    if (idxType is null) { _propsSkipped++; continue; }
                    indexCast = $"(({idxType.Cs})({index.Value}))";
                }

                string? setterCs = null;
                string? setterCast = null;
                var setterName = p.TryGetProperty("setter", out var s) ? s.GetString() : null;
                if (!string.IsNullOrEmpty(setterName) && ResolveMethod(gd, setterName) is { } setter)
                {
                    var (setterCls, setterEl) = setter;
                    var sa = setterEl.TryGetProperty("arguments", out var sargs) ? sargs : default;
                    var setterArgCount = sa.ValueKind == JsonValueKind.Array ? sa.GetArrayLength() : 0;
                    if (setterArgCount == (index.HasValue ? 2 : 1))
                    {
                        var valueType = Map(sa[setterArgCount - 1].GetProperty("type").GetString()!,
                            sa[setterArgCount - 1].TryGetProperty("meta", out var vm) ? vm.GetString() : null);
                        if (valueType is not null)
                        {
                            if (valueType.Cs == type.Cs)
                            {
                                setterCs = Pascal(setterName!);
                            }
                            else if (valueType.Kind is "int" or "enum" && type.Kind is "int" or "enum")
                            {
                                setterCs = Pascal(setterName!);
                                setterCast = $"({valueType.Cs})";
                            }
                            // else: type mismatch beyond numeric/enum - emit read-only.
                        }
                    }
                    if (setterCs is not null) _internalAccessors.Add((setterCls, setterName!));
                }

                _internalAccessors.Add((getterCls, getterName!));
                list.Add(new PropInfo(csName, Pascal(getterName!), setterCs, indexCast, type, setterCast));
            }
        }
    }

    private static void EmitProperties(StringBuilder sb, ClassInfo info, HashSet<string> used)
    {
        foreach (var pi in _propsByClass[info.GdName])
        {
            if (!used.Add(pi.CsName)) { _propsSkipped++; continue; }
            _propsEmitted++;

            var getCall = pi.IndexCast is null ? $"{pi.GetterCs}()" : $"{pi.GetterCs}({pi.IndexCast})";
            sb.AppendLine();
            if (pi.SetterCs is null)
            {
                sb.AppendLine($"    public {RetType(pi.Type)} {pi.CsName} => {getCall};");
                continue;
            }

            var valueExpr = pi.SetterCast is null ? "value" : $"{pi.SetterCast}value";
            var setCall = pi.IndexCast is null
                ? $"{pi.SetterCs}({valueExpr})"
                : $"{pi.SetterCs}({pi.IndexCast}, {valueExpr})";
            sb.AppendLine($"    public {RetType(pi.Type)} {pi.CsName}");
            sb.AppendLine("    {");
            sb.AppendLine($"        get => {getCall};");
            sb.AppendLine($"        set => {setCall};");
            sb.AppendLine("    }");
        }
    }

    // ---------------------------------------------------------- virtuals --

    // Virtual dispatch does not yet marshal Variant/collection kinds (borrowed
    // args would need copy semantics distinct from method calls).
    private static bool VirtualKindOk(TypeRef t) => t.Kind is not ("variant" or "builtinref");

    private static void CollectVirtual(JsonElement m, string gdName, HashSet<string> used, List<VirtualInfo> virtuals)
    {
        var ret = m.TryGetProperty("return_value", out var rv)
            ? Map(rv.GetProperty("type").GetString()!, rv.TryGetProperty("meta", out var rm) ? rm.GetString() : null)
            : TypeRef.Void;
        if (ret is null || !VirtualKindOk(ret)) { _virtualsSkipped++; return; }

        var args = new List<(string name, TypeRef type)>();
        if (m.TryGetProperty("arguments", out var margs))
        {
            foreach (var a in margs.EnumerateArray())
            {
                var t = Map(a.GetProperty("type").GetString()!, a.TryGetProperty("meta", out var am) ? am.GetString() : null);
                if (t is null || !VirtualKindOk(t)) { _virtualsSkipped++; return; }
                args.Add((Camel(a.GetProperty("name").GetString()!), t));
            }
        }

        var csName = VirtualCsName(gdName);
        if (ReservedMembers.Contains(csName) || !used.Add(csName)) { _virtualsSkipped++; return; }

        virtuals.Add(new VirtualInfo(gdName, csName, ret, args));
        _virtualNameMap[gdName] = csName;
        _virtualsEmitted++;
    }

    private static string VirtualCsName(string gdName) =>
        "_" + Pascal(gdName.TrimStart('_'));

    private static void EmitVirtuals(StringBuilder sb, List<VirtualInfo> virtuals)
    {
        if (virtuals.Count == 0) return;

        // No-op stubs users override GodotSharp-style; the engine only calls
        // in when ClassRegistry reported the virtual as overridden.
        foreach (var vi in virtuals)
        {
            var paramList = string.Join(", ", vi.Args.Select(a => $"{ParamType(a.type)} {a.name}"));
            sb.AppendLine();
            sb.AppendLine($"    public virtual {RetType(vi.Ret)} {vi.CsName}({paramList}){(vi.Ret.Kind == "void" ? " { }" : " => default!;")}");
        }

        sb.AppendLine();
        foreach (var vi in virtuals)
            sb.AppendLine($"    private static ulong __vsn{vi.GdName};");

        sb.AppendLine();
        sb.AppendLine("    internal override bool __CallVirtual(ulong nameSn, nint* args, nint ret)");
        sb.AppendLine("    {");
        foreach (var vi in virtuals)
        {
            var vsn = $"__vsn{vi.GdName}";
            sb.AppendLine($"        if ({vsn} == 0) {vsn} = StringNames.Get(\"{vi.GdName}\").Opaque;");
            sb.AppendLine($"        if (nameSn == {vsn})");
            sb.AppendLine("        {");

            var argExprs = vi.Args.Select((a, i) => a.type.Kind switch
            {
                "bool" => $"*(byte*)args[{i}] != 0",
                "int" => $"unchecked(({a.type.Cs})(*(long*)args[{i}]))",
                "float" => a.type.Cs == "float" ? $"(float)(*(double*)args[{i}])" : $"*(double*)args[{i}]",
                "enum" => $"({a.type.Cs})(*(long*)args[{i}])",
                "string" => $"NativeString.Read(*(ulong*)args[{i}])",
                "stringname" => $"StringNames.Read(*(ulong*)args[{i}])",
                "math" => $"*({a.type.Cs}*)args[{i}]",
                "class" => $"({a.type.Cs}?)InstanceBindings.GetOrCreate(*(nint*)args[{i}], adoptRef: false)",
                _ => throw new InvalidOperationException(a.type.Kind),
            }).ToList();
            var call = $"{vi.CsName}({string.Join(", ", argExprs)})";

            switch (vi.Ret.Kind)
            {
                case "void":
                    sb.AppendLine($"            {call};");
                    break;
                case "bool":
                    sb.AppendLine($"            *(byte*)ret = {call} ? (byte)1 : (byte)0;");
                    break;
                case "int":
                    sb.AppendLine($"            *(long*)ret = unchecked((long){call});");
                    break;
                case "float":
                    sb.AppendLine($"            *(double*)ret = {call};");
                    break;
                case "enum":
                    sb.AppendLine($"            *(long*)ret = (long){call};");
                    break;
                case "math":
                    sb.AppendLine($"            *({vi.Ret.Cs}*)ret = {call};");
                    break;
                case "class":
                    sb.AppendLine($"            *(nint*)ret = {call}?.NativePtr ?? 0;");
                    break;
                case "string":
                    // Ownership transfers: the engine destructs the ret slot
                    // after copying out of it.
                    sb.AppendLine($"            *(ulong*)ret = NativeString.Create({call} ?? \"\");");
                    break;
                case "stringname":
                    sb.AppendLine($"            *(ulong*)ret = StringNames.CreateOwned({call} ?? \"\");");
                    break;
            }

            sb.AppendLine("            return true;");
            sb.AppendLine("        }");
        }
        sb.AppendLine("        return base.__CallVirtual(nameSn, args, ret);");
        sb.AppendLine("    }");
    }

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
        if (t == "Variant") return new TypeRef("variant", "Variant");
        if (t == "Array" || t.StartsWith("typedarray::")) return new TypeRef("builtinref", "Godot.Collections.Array");
        if (t == "Dictionary" || t.StartsWith("typeddictionary::")) return new TypeRef("builtinref", "Godot.Collections.Dictionary");
        if (t == "NodePath") return new TypeRef("builtinref", "NodePath");
        return null; // callables, signals, packed arrays, native-struct pointers, ...
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
