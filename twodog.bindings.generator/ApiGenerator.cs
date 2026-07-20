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

    private sealed record TypeRef(string Kind, string Cs, string? PackedElem = null, string? PackedGd = null)
    {
        public static readonly TypeRef Void = new("void", "void");
    }

    // GodotSharp compat: Packed*Array marshals as a plain C# array (copies at
    // the boundary). Element cs-type per packed class.
    private static readonly Dictionary<string, string> PackedElemMap = new()
    {
        ["PackedByteArray"] = "byte", ["PackedInt32Array"] = "int", ["PackedInt64Array"] = "long",
        ["PackedFloat32Array"] = "float", ["PackedFloat64Array"] = "double", ["PackedStringArray"] = "string",
        ["PackedVector2Array"] = "Vector2", ["PackedVector3Array"] = "Vector3",
        ["PackedColorArray"] = "Color", ["PackedVector4Array"] = "Vector4",
    };

    private static string PackedVt(string gd) =>
        "GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_" +
        string.Concat(gd.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + c : c.ToString())).ToUpperInvariant();

    private static string PackedOpIndex(string gd) => $"GdExtensionInterface.{gd}OperatorIndex";

    // cs type -> GDExtensionVariantType constant suffix, for variant coercion
    // in signal trampolines.
    private static readonly Dictionary<string, string> VariantTypeOfCs = new()
    {
        ["Vector2"] = "VECTOR2", ["Vector2I"] = "VECTOR2I", ["Rect2"] = "RECT2", ["Rect2I"] = "RECT2I",
        ["Vector3"] = "VECTOR3", ["Vector3I"] = "VECTOR3I", ["Transform2D"] = "TRANSFORM2D",
        ["Vector4"] = "VECTOR4", ["Vector4I"] = "VECTOR4I", ["Plane"] = "PLANE", ["Quaternion"] = "QUATERNION",
        ["Aabb"] = "AABB", ["Basis"] = "BASIS", ["Transform3D"] = "TRANSFORM3D", ["Projection"] = "PROJECTION",
        ["Color"] = "COLOR", ["Rid"] = "RID",
        ["Godot.Collections.Array"] = "ARRAY", ["Godot.Collections.Dictionary"] = "DICTIONARY", ["NodePath"] = "NODE_PATH",
    };

    private static string VtOf(string cs) =>
        $"GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_{VariantTypeOfCs[cs]}";

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
    private static readonly HashSet<string> _staticSingletons = [];
    private static int _emitted, _skipped, _virtualsEmitted, _virtualsSkipped, _propsEmitted, _propsSkipped,
        _signalsEmitted, _signalsSkipped;

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

        // ---- pure singletons become static classes (GodotSharp shape:
        //      Engine.GetMainLoop(), Input.IsActionPressed(...)). A singleton
        //      type must stay a normal class when something inherits it
        //      (PhysicsServer2DExtension : PhysicsServer2D) or when it appears
        //      as a type in any signature (EditorInterface). ----
        _staticSingletons.Clear();
        var inheritedBy = new HashSet<string>();
        var usedAsType = new HashSet<string>();
        foreach (var (gd, c) in _classJson)
        {
            if (c.TryGetProperty("inherits", out var inh)) inheritedBy.Add(inh.GetString()!);
            if (!c.TryGetProperty("methods", out var ms)) continue;
            foreach (var m in ms.EnumerateArray())
            {
                if (m.TryGetProperty("return_value", out var rv)) usedAsType.Add(rv.GetProperty("type").GetString()!);
                if (!m.TryGetProperty("arguments", out var margs)) continue;
                foreach (var a in margs.EnumerateArray()) usedAsType.Add(a.GetProperty("type").GetString()!);
            }
        }
        foreach (var (type, _) in singletons)
        {
            // Instantiability is irrelevant (Engine/OS are nominally
            // instantiable but never sanely constructed; GodotSharp
            // staticizes them too).
            if (_classes.ContainsKey(type) && !inheritedBy.Contains(type) && !usedAsType.Contains(type))
            {
                _staticSingletons.Add(type);
            }
        }

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
            {
                // Static singleton classes have no wrapper type: their engine
                // instance wraps as a plain GodotObject (matches the Singleton
                // property's type).
                var factory = _staticSingletons.Contains(i.GdName) ? "GodotObject" : i.CsName;
                sbReg.AppendLine($"        Entries[\"{i.GdName}\"] = (static (p, rc) => new {factory}(p, rc), {Bool(i.RefCounted)});");
            }
            sbReg.AppendLine("    }");
        }
        sbReg.AppendLine("}");
        Write(outDir, "ApiRegistry.gen.cs", sbReg);

        // ---- GD statics (GodotSharp's GD surface, from utility functions) ----
        EmitGd(root, outDir);

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
                          $"{_signalsEmitted} signal events ({_signalsSkipped} skipped), " +
                          $"{_virtualsEmitted} virtual stubs ({_virtualsSkipped} skipped), into {outDir}");
    }

    // ------------------------------------------------------------- class --

    private static void EmitClass(StringBuilder sb, JsonElement c, ClassInfo info, Dictionary<string, string> singletons)
    {
        var isObject = info.CsName == "GodotObject";
        var isStaticSingleton = _staticSingletons.Contains(info.GdName);

        sb.AppendLine(isStaticSingleton
            ? $"public static unsafe partial class {info.CsName}"
            : isObject || info.BaseCs == ""
                ? $"public unsafe partial class {info.CsName}"
                : $"public unsafe partial class {info.CsName} : {info.BaseCs}");
        sb.AppendLine("{");

        var used = new HashSet<string> { info.CsName };

        if (isStaticSingleton)
        {
            // GodotSharp shape: pure singletons are static classes
            // (Engine.GetMainLoop()); Singleton stays available for
            // Connect/signal use, typed GodotObject.
            sb.AppendLine("    private static nint _singletonPtr;");
            sb.AppendLine();
            sb.AppendLine("    internal static nint SingletonPtr =>");
            sb.AppendLine($"        _singletonPtr != 0 ? _singletonPtr : _singletonPtr = InstanceBindings.GetSingletonPtr(\"{singletons[info.GdName]}\");");
            sb.AppendLine();
            sb.AppendLine("    public static GodotObject Singleton => InstanceBindings.GetOrCreate(SingletonPtr, adoptRef: false)!;");
        }
        else if (!isObject)
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

        if (!isStaticSingleton && singletons.TryGetValue(info.GdName, out var singletonName))
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

        EmitProperties(sb, info, used, isStaticSingleton);
        EmitSignals(sb, c, used, isStaticSingleton);

        var virtuals = new List<VirtualInfo>();
        if (c.TryGetProperty("methods", out var methods))
        {
            foreach (var m in methods.EnumerateArray())
                EmitMethod(sb, m, info, used, virtuals, isObject || isStaticSingleton, isStaticSingleton);
        }

        EmitVirtuals(sb, virtuals);

        sb.AppendLine("}");
        sb.AppendLine();
    }

    // ----------------------------------------------------------- signals --

    /// <summary>Decode expression from a borrowed signal-arg variant.</summary>
    private static string? FromVariantExpr(TypeRef t, string v) => t.Kind switch
    {
        "bool" => $"Variants.ToBool({v})",
        "int" => $"unchecked(({t.Cs})Variants.ToInt({v}))",
        "enum" => $"({t.Cs})Variants.ToInt({v})",
        "float" => $"({t.Cs})Variants.ToFloat({v})",
        "string" => $"Variants.ToManagedString({v})",
        "stringname" => $"StringName.Intern(Variants.ToManagedString({v}))",
        "class" => $"({t.Cs}?)InstanceBindings.GetOrCreate(Variants.ToObject({v}), adoptRef: false)",
        "variant" => $"new Variant(Variants.NewCopy({v}))",
        "math" => $"Variants.ToStruct<{t.Cs}>({VtOf(t.Cs)}, {v})",
        "builtinref" => $"new {t.Cs}(Variants.ToStruct<ulong>({VtOf(t.Cs)}, {v}))",
        _ => null, // packed / opaque16 signal args: not yet
    };

    /// <summary>
    /// GodotSharp-style signal events: a typed XEventHandler delegate plus an
    /// event whose add/remove Connect/Disconnect through a custom callable
    /// keyed on the handler delegate (equality makes -= match +=).
    /// </summary>
    private static void EmitSignals(StringBuilder sb, JsonElement c, HashSet<string> used, bool staticClass)
    {
        if (!c.TryGetProperty("signals", out var signals)) return;

        foreach (var s in signals.EnumerateArray())
        {
            var gd = s.GetProperty("name").GetString()!;
            var p = Pascal(gd);
            var delegateName = p + "EventHandler";
            if (ReservedMembers.Contains(p) || used.Contains(p) || used.Contains(delegateName))
            {
                _signalsSkipped++;
                continue;
            }

            var args = new List<(string name, TypeRef type)>();
            var supported = true;
            if (s.TryGetProperty("arguments", out var sargs))
            {
                foreach (var a in sargs.EnumerateArray())
                {
                    var t = Map(a.GetProperty("type").GetString()!, a.TryGetProperty("meta", out var am) ? am.GetString() : null);
                    if (t is null || FromVariantExpr(t, "_") is null) { supported = false; break; }
                    args.Add((Camel(a.GetProperty("name").GetString()!), t));
                }
            }
            if (!supported) { _signalsSkipped++; continue; }

            used.Add(p);
            used.Add(delegateName);
            _signalsEmitted++;

            var paramList = string.Join(", ", args.Select(a => $"{ParamType(a.type)} {a.name}"));
            var decodes = string.Join(", ", args.Select((a, i) =>
                FromVariantExpr(a.type, $"*((NativeVariant**)__a)[{i}]")));
            var trampoline = $"static (__d, __a, __n) => (({delegateName})__d)({decodes})";
            var target = staticClass ? "Singleton." : "";
            var mod = staticClass ? "static " : "";

            sb.AppendLine();
            sb.AppendLine($"    public delegate void {delegateName}({paramList});");
            sb.AppendLine();
            sb.AppendLine($"    public {mod}event {delegateName} {p}");
            sb.AppendLine("    {");
            sb.AppendLine($"        add => {target}Connect(\"{gd}\", Callable.FromSignalHandler(value, {trampoline}));");
            sb.AppendLine($"        remove => {target}Disconnect(\"{gd}\", Callable.FromSignalHandler(value, {trampoline}));");
            sb.AppendLine("    }");
        }
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
        foreach (var (name, value) in RenamedEnumMembers(e))
            sb.AppendLine($"{indent}    {name} = {value},");
        sb.AppendLine($"{indent}}}");
        return cs;
    }

    /// <summary>
    /// GodotSharp's enum-member renaming: strip the longest common
    /// underscore-word prefix shared by all members, backing off per member
    /// when stripping would leave an empty or digit-leading name, then
    /// PascalCase what remains (PROCESS_MODE_DISABLED -> Disabled).
    /// </summary>
    private static List<(string name, long value)> RenamedEnumMembers(JsonElement e)
    {
        var members = e.GetProperty("values").EnumerateArray()
            .Select(v => (name: v.GetProperty("name").GetString()!, value: v.GetProperty("value").GetInt64()))
            .ToList();
        var parts = members.Select(m => m.name.Split('_')).ToList();

        var prefixLen = 0;
        if (members.Count > 1)
        {
            while (parts.All(p => p.Length > prefixLen + 1 && p[prefixLen] == parts[0][prefixLen]))
                prefixLen++;
        }

        var result = new List<(string, long)>(members.Count);
        var seen = new HashSet<string>();
        foreach (var p in parts.Select((p, i) => (words: p, i)))
        {
            var strip = Math.Min(prefixLen, p.words.Length - 1);
            string cs;
            while (true)
            {
                cs = string.Concat(p.words.Skip(strip).Select(EnumWord));
                if (cs.Length > 0 && !char.IsDigit(cs[0])) break;
                if (strip == 0) break;
                strip--;
            }
            if (!seen.Add(cs)) cs = string.Concat(p.words.Select(EnumWord)); // collision: keep full name
            seen.Add(cs);
            result.Add((cs, members[p.i].value));
        }
        return result;

        static string EnumWord(string w) =>
            w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant();
    }

    // ----------------------------------------------------------- methods --

    private static void EmitMethod(StringBuilder sb, JsonElement m, ClassInfo info, HashSet<string> used,
        List<VirtualInfo> virtuals, bool noVirtuals, bool staticClass)
    {
        var gdName = m.GetProperty("name").GetString()!;
        if (m.TryGetProperty("is_vararg", out var va) && va.GetBoolean())
        {
            EmitVarargMethod(sb, m, info, used, staticClass, gdName);
            return;
        }
        if (m.TryGetProperty("is_virtual", out var v) && v.GetBoolean())
        {
            // Object's virtuals (_get/_set/_get_property_list...) are all
            // Variant-typed and the handwritten GodotObject roots the
            // __CallVirtual chain; static singletons cannot be inherited.
            if (!noVirtuals) CollectVirtual(m, gdName, used, virtuals);
            return;
        }

        var csName = Pascal(gdName);
        if (ReservedMembers.Contains(csName) || !used.Add(csName)) { _skipped++; return; }

        var ret = m.TryGetProperty("return_value", out var rv)
            ? Map(rv.GetProperty("type").GetString()!, rv.TryGetProperty("meta", out var rm) ? rm.GetString() : null)
            : TypeRef.Void;
        if (ret is null) { _skipped++; return; }

        var args = new List<(string name, TypeRef type)>();
        var defaults = new List<string?>();
        if (m.TryGetProperty("arguments", out var margs))
        {
            foreach (var a in margs.EnumerateArray())
            {
                var t = Map(a.GetProperty("type").GetString()!, a.TryGetProperty("meta", out var am) ? am.GetString() : null);
                if (t is null) { _skipped++; return; }
                args.Add((Camel(a.GetProperty("name").GetString()!), t));
                defaults.Add(a.TryGetProperty("default_value", out var dv) ? DefaultExpr(t, dv.GetString()!) : null);
            }
        }

        // C# defaults must be trailing-contiguous: keep the longest expressible
        // tail, drop the rest.
        for (var i = defaults.Count - 1; i >= 0; i--)
        {
            if (defaults[i] is null)
            {
                for (var j = 0; j < i; j++) defaults[j] = null;
                break;
            }
        }

        var jsonStatic = m.TryGetProperty("is_static", out var st) && st.GetBoolean();
        var isStatic = staticClass || jsonStatic;
        var hash = m.GetProperty("hash").GetInt64();
        var mbField = $"__mb_{gdName}";
        _emitted++;

        // GodotSharp compat: property accessor methods are hidden from the
        // public surface (the property is the API); internal keeps them
        // callable by generated property bodies across the class hierarchy.
        var visibility = _internalAccessors.Contains((info.GdName, gdName)) ? "internal" : "public";

        sb.AppendLine();
        sb.AppendLine($"    private static nint {mbField};");
        var paramList = string.Join(", ", args.Select((a, i) =>
            $"{ParamType(a.type)} {a.name}{(defaults[i] is null ? "" : $" = {defaults[i]}")}"));
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
                "stringname" => $"ulong __a{i} = {name}.NativeValue;",
                "math" => $"var __a{i} = {name};",
                "class" => $"nint __a{i} = {name}?.NativePtr ?? 0;",
                "variant" => $"var __a{i} = {name}.Native;",
                "builtinref" => $"ulong __a{i} = {name}.Native;",
                "opaque16" => $"var __a{i} = {name}.Native;",
                "packed" => t.PackedElem == "string"
                    ? $"var __a{i} = Packed.CreateStrings({name});"
                    : $"var __a{i} = Packed.CreatePod<{t.PackedElem}>({PackedVt(t.PackedGd!)}, {PackedOpIndex(t.PackedGd!)}, {name});",
                _ => throw new InvalidOperationException(t.Kind),
            });
        }

        if (args.Count > 0)
        {
            sb.AppendLine($"        var __args = stackalloc nint[{args.Count}];");
            for (var i = 0; i < args.Count; i++)
                sb.AppendLine($"        __args[{i}] = (nint)(&__a{i});");
        }

        var self = jsonStatic ? "0" : staticClass ? "SingletonPtr" : "NativePtr";
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
            case "opaque16" or "packed":
                sb.AppendLine("        Opaque16 __ret = default;");
                sb.AppendLine($"        GdExtensionInterface.ObjectMethodBindPtrcall(__mb, {self}, {argsPtr}, (nint)(&__ret));");
                break;
        }

        // release owned temporaries created for args
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i].type.Kind == "string")
                sb.AppendLine($"        NativeString.Destroy(ref __a{i});");
            else if (args[i].type.Kind == "packed")
                sb.AppendLine($"        Packed.Destroy({PackedVt(args[i].type.PackedGd!)}, ref __a{i});");
        }

        switch (ret.Kind)
        {
            case "bool": sb.AppendLine("        return __ret != 0;"); break;
            case "int": sb.AppendLine($"        return unchecked(({ret.Cs})__ret);"); break;
            case "enum": sb.AppendLine($"        return ({ret.Cs})__ret;"); break;
            case "float": sb.AppendLine($"        return ({ret.Cs})__ret;"); break;
            case "string": sb.AppendLine("        return NativeString.ReadAndDestroy(ref __ret);"); break;
            case "stringname": sb.AppendLine("        return StringName.Intern(StringNames.ReadAndDestroy(ref __ret));"); break;
            case "math": sb.AppendLine("        return __ret;"); break;
            case "class":
                var refCounted = _classes.TryGetValue(GdOf(ret.Cs), out var rc2) && rc2.RefCounted;
                sb.AppendLine($"        return ({ret.Cs}?)InstanceBindings.GetOrCreate(__ret, adoptRef: {Bool(refCounted)});");
                break;
            case "variant": sb.AppendLine("        return new Variant(__ret);"); break;
            case "builtinref": sb.AppendLine($"        return new {ret.Cs}(__ret);"); break;
            case "opaque16": sb.AppendLine($"        return new {ret.Cs}(__ret);"); break;
            case "packed":
                sb.AppendLine(ret.PackedElem == "string"
                    ? "        return Packed.ToStringArray(ref __ret);"
                    : $"        return Packed.ToPodArray<{ret.PackedElem}>({PackedVt(ret.PackedGd!)}, {PackedOpIndex(ret.PackedGd!)}, ref __ret);");
                break;
        }

        sb.AppendLine("    }");
    }

    // ------------------------------------------------------------ varargs --

    private static readonly HashSet<string> VarargLeadingKinds =
        ["bool", "int", "enum", "float", "string", "stringname", "class", "variant"];

    private static readonly HashSet<string> VarargRetKinds =
        ["void", "bool", "int", "enum", "float", "string", "variant", "class"];

    private static string ToVariantExpr(TypeRef t, string name) => t.Kind switch
    {
        "bool" => $"Variants.FromBool({name})",
        "int" => $"Variants.FromInt(unchecked((long){name}))",
        "enum" => $"Variants.FromInt((long){name})",
        "float" => $"Variants.FromFloat({name})",
        "string" => $"Variants.FromString({name})",
        "stringname" => $"Variants.FromStruct(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_STRING_NAME, {name}.NativeValue)",
        "class" => $"Variants.FromObject({name}?.NativePtr ?? 0)",
        "variant" => $"Variants.NewCopy(in {name}.Native)",
        _ => throw new InvalidOperationException(t.Kind),
    };

    /// <summary>
    /// Vararg methods (emit_signal, call, rpc, ...) go through the variant
    /// call path (object_method_bind_call) with a `params Variant[]` tail -
    /// GodotSharp's exact shape (Call(method, params Variant[] args)).
    /// </summary>
    private static void EmitVarargMethod(StringBuilder sb, JsonElement m, ClassInfo info, HashSet<string> used,
        bool staticClass, string gdName)
    {
        var csName = Pascal(gdName);
        if (ReservedMembers.Contains(csName) || !used.Add(csName)) { _skipped++; return; }

        var ret = m.TryGetProperty("return_value", out var rv)
            ? Map(rv.GetProperty("type").GetString()!, rv.TryGetProperty("meta", out var rm) ? rm.GetString() : null)
            : TypeRef.Void;
        if (ret is null || !VarargRetKinds.Contains(ret.Kind)) { _skipped++; return; }

        var lead = new List<(string name, TypeRef type)>();
        if (m.TryGetProperty("arguments", out var margs))
        {
            foreach (var a in margs.EnumerateArray())
            {
                var t = Map(a.GetProperty("type").GetString()!, a.TryGetProperty("meta", out var am) ? am.GetString() : null);
                if (t is null || !VarargLeadingKinds.Contains(t.Kind)) { _skipped++; return; }
                lead.Add((Camel(a.GetProperty("name").GetString()!), t));
            }
        }

        var jsonStatic = m.TryGetProperty("is_static", out var st) && st.GetBoolean();
        var hash = m.GetProperty("hash").GetInt64();
        var mbField = $"__mb_{gdName}";
        var self = jsonStatic ? "0" : staticClass ? "SingletonPtr" : "NativePtr";
        _emitted++;

        var visibility = _internalAccessors.Contains((info.GdName, gdName)) ? "internal" : "public";
        var k = lead.Count;

        sb.AppendLine();
        sb.AppendLine($"    private static nint {mbField};");
        var paramList = string.Join(", ", lead.Select(a => $"{ParamType(a.type)} {a.name}").Append("params Variant[] args"));
        sb.AppendLine($"    {visibility} {(staticClass || jsonStatic ? "static " : "")}{RetType(ret)} {csName}({paramList})");
        sb.AppendLine("    {");
        sb.AppendLine($"        var __mb = {mbField};");
        sb.AppendLine("        if (__mb == 0)");
        sb.AppendLine("        {");
        sb.AppendLine($"            __mb = MethodBinds.Resolve(\"{info.GdName}\", \"{gdName}\", {hash});");
        sb.AppendLine($"            if (__mb == 0) throw new MissingMethodException(\"{info.GdName}.{gdName} is not available in this engine build.\");");
        sb.AppendLine($"            {mbField} = __mb;");
        sb.AppendLine("        }");
        sb.AppendLine($"        var __n = {k} + args.Length;");
        sb.AppendLine("        var __ptrs = stackalloc nint[Math.Max(__n, 1)];");
        if (k > 0)
        {
            sb.AppendLine($"        var __lead = stackalloc NativeVariant[{k}];");
            for (var i = 0; i < k; i++)
            {
                sb.AppendLine($"        __lead[{i}] = {ToVariantExpr(lead[i].type, lead[i].name)};");
                sb.AppendLine($"        __ptrs[{i}] = (nint)(__lead + {i});");
            }
        }
        sb.AppendLine("        var __tail = stackalloc NativeVariant[Math.Max(args.Length, 1)];");
        sb.AppendLine("        for (var __i = 0; __i < args.Length; __i++)");
        sb.AppendLine("        {");
        sb.AppendLine("            __tail[__i] = args[__i].Native;");
        sb.AppendLine($"            __ptrs[{k} + __i] = (nint)(__tail + __i);");
        sb.AppendLine("        }");
        sb.AppendLine("        NativeVariant __ret = default;");
        sb.AppendLine("        GDExtensionCallError __err = default;");
        sb.AppendLine($"        GdExtensionInterface.ObjectMethodBindCall(__mb, {self}, (nint)__ptrs, __n, (nint)(&__ret), (nint)(&__err));");
        if (k > 0)
        {
            sb.AppendLine($"        for (var __i = 0; __i < {k}; __i++) Variants.Destroy(ref __lead[__i]);");
        }
        sb.AppendLine("        if ((int)__err.error != 0)");
        sb.AppendLine("        {");
        sb.AppendLine("            Variants.Destroy(ref __ret);");
        sb.AppendLine($"            throw new InvalidOperationException($\"{info.GdName}.{gdName} call failed: error={{(int)__err.error}} argument={{__err.argument}}\");");
        sb.AppendLine("        }");
        switch (ret.Kind)
        {
            case "void":
                sb.AppendLine("        Variants.Destroy(ref __ret);");
                break;
            case "bool":
                sb.AppendLine("        var __v = Variants.ToBool(in __ret);");
                sb.AppendLine("        Variants.Destroy(ref __ret);");
                sb.AppendLine("        return __v;");
                break;
            case "int" or "enum":
                sb.AppendLine("        var __v = Variants.ToInt(in __ret);");
                sb.AppendLine("        Variants.Destroy(ref __ret);");
                sb.AppendLine($"        return unchecked(({ret.Cs})__v);");
                break;
            case "float":
                sb.AppendLine("        var __v = Variants.ToFloat(in __ret);");
                sb.AppendLine("        Variants.Destroy(ref __ret);");
                sb.AppendLine($"        return ({ret.Cs})__v;");
                break;
            case "string":
                sb.AppendLine("        var __v = Variants.ToManagedString(in __ret);");
                sb.AppendLine("        Variants.Destroy(ref __ret);");
                sb.AppendLine("        return __v;");
                break;
            case "class":
                sb.AppendLine("        var __obj = Variants.ToObject(in __ret);");
                sb.AppendLine("        Variants.Destroy(ref __ret);");
                sb.AppendLine($"        return ({ret.Cs}?)InstanceBindings.GetOrCreate(__obj, adoptRef: false);");
                break;
            case "variant":
                sb.AppendLine("        return new Variant(__ret);");
                break;
        }
        sb.AppendLine("    }");
    }

    private static string GdOf(string cs) => cs == "GodotObject" ? "Object" : cs;

    // ------------------------------------------------------- GD statics --

    // The subset of utility functions GodotSharp exposes on GD (math utilities
    // live on Mathf there, so they are deliberately not emitted here).
    private static readonly HashSet<string> GdUtilityAllowlist =
    [
        "print", "print_rich", "print_verbose", "printerr", "printraw", "prints", "printt",
        "push_error", "push_warning",
        "randf", "randi", "randf_range", "randi_range", "randfn", "randomize", "seed",
        "error_string", "type_string", "var_to_str", "str_to_var", "hash",
        "is_instance_valid", "is_instance_id_valid", "instance_from_id",
    ];

    private static readonly Dictionary<string, string> GdNameOverrides = new()
    {
        ["printerr"] = "PrintErr",
        ["printraw"] = "PrintRaw",
    };

    private static void EmitGd(JsonElement root, string outDir)
    {
        var sb = NewFile();
        sb.AppendLine("/// <summary>GodotSharp-compatible GD statics over Godot's utility functions.</summary>");
        sb.AppendLine("public static unsafe partial class GD");
        sb.AppendLine("{");
        sb.AppendLine("    private static nint Utility(ref nint cache, string name, long hash)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (cache != 0) return cache;");
        sb.AppendLine("        var sn = StringNames.Get(name).Opaque;");
        sb.AppendLine("        return cache = GdExtensionInterface.VariantGetPtrUtilityFunction((nint)(&sn), hash);");
        sb.AppendLine("    }");

        foreach (var uf in root.GetProperty("utility_functions").EnumerateArray())
        {
            var gdName = uf.GetProperty("name").GetString()!;
            if (!GdUtilityAllowlist.Contains(gdName)) continue;

            var csName = GdNameOverrides.TryGetValue(gdName, out var ov) ? ov : Pascal(gdName);
            var hash = uf.GetProperty("hash").GetInt64();
            var isVararg = uf.TryGetProperty("is_vararg", out var va) && va.GetBoolean();
            var ret = uf.TryGetProperty("return_type", out var rt)
                ? Map(rt.GetString()!, null)
                : TypeRef.Void;
            if (ret is null) continue;
            var ufField = $"__uf_{gdName}";

            if (isVararg)
            {
                if (ret.Kind != "void") continue; // vararg-with-return not needed for this subset
                sb.AppendLine();
                sb.AppendLine($"    private static nint {ufField};");
                sb.AppendLine($"    public static void {csName}(params Variant[] what)");
                sb.AppendLine("    {");
                sb.AppendLine($"        var __fn = (delegate* unmanaged<nint, nint*, int, void>)Utility(ref {ufField}, \"{gdName}\", {hash});");
                sb.AppendLine("        var __n = what.Length;");
                sb.AppendLine("        var __natives = stackalloc NativeVariant[Math.Max(__n, 1)];");
                sb.AppendLine("        var __ptrs = stackalloc nint[Math.Max(__n, 1)];");
                sb.AppendLine("        for (var __i = 0; __i < __n; __i++)");
                sb.AppendLine("        {");
                sb.AppendLine("            __natives[__i] = what[__i].Native;");
                sb.AppendLine("            __ptrs[__i] = (nint)(__natives + __i);");
                sb.AppendLine("        }");
                sb.AppendLine("        __fn(0, __ptrs, __n);");
                sb.AppendLine("    }");
                continue;
            }

            var args = new List<(string name, TypeRef type)>();
            var supported = true;
            if (uf.TryGetProperty("arguments", out var margs))
            {
                foreach (var a in margs.EnumerateArray())
                {
                    var t = Map(a.GetProperty("type").GetString()!, null);
                    if (t is null) { supported = false; break; }
                    args.Add((Camel(a.GetProperty("name").GetString()!), t));
                }
            }
            if (!supported) continue;

            sb.AppendLine();
            sb.AppendLine($"    private static nint {ufField};");
            var paramList = string.Join(", ", args.Select(a => $"{ParamType(a.type)} {a.name}"));
            sb.AppendLine($"    public static {RetType(ret)} {csName}({paramList})");
            sb.AppendLine("    {");
            sb.AppendLine($"        var __fn = (delegate* unmanaged<nint, nint*, int, void>)Utility(ref {ufField}, \"{gdName}\", {hash});");

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
                    "stringname" => $"ulong __a{i} = {name}.NativeValue;",
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
            var argsPtr = args.Count > 0 ? "__args" : "null";

            switch (ret.Kind)
            {
                case "void":
                    sb.AppendLine($"        __fn(0, {argsPtr}, {args.Count});");
                    break;
                case "bool":
                    sb.AppendLine("        byte __ret = 0;");
                    sb.AppendLine($"        __fn((nint)(&__ret), {argsPtr}, {args.Count});");
                    break;
                case "int" or "enum":
                    sb.AppendLine("        long __ret = 0;");
                    sb.AppendLine($"        __fn((nint)(&__ret), {argsPtr}, {args.Count});");
                    break;
                case "float":
                    sb.AppendLine("        double __ret = 0;");
                    sb.AppendLine($"        __fn((nint)(&__ret), {argsPtr}, {args.Count});");
                    break;
                case "string" or "stringname":
                    sb.AppendLine("        ulong __ret = 0;");
                    sb.AppendLine($"        __fn((nint)(&__ret), {argsPtr}, {args.Count});");
                    break;
                case "math":
                    sb.AppendLine($"        var __ret = default({ret.Cs});");
                    sb.AppendLine($"        __fn((nint)(&__ret), {argsPtr}, {args.Count});");
                    break;
                case "class":
                    sb.AppendLine("        nint __ret = 0;");
                    sb.AppendLine($"        __fn((nint)(&__ret), {argsPtr}, {args.Count});");
                    break;
                case "variant":
                    sb.AppendLine("        NativeVariant __ret = default;");
                    sb.AppendLine($"        __fn((nint)(&__ret), {argsPtr}, {args.Count});");
                    break;
                case "builtinref":
                    sb.AppendLine("        ulong __ret = 0;");
                    sb.AppendLine($"        __fn((nint)(&__ret), {argsPtr}, {args.Count});");
                    break;
            }

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
                case "stringname": sb.AppendLine("        return StringName.Intern(StringNames.ReadAndDestroy(ref __ret));"); break;
                case "math": sb.AppendLine("        return __ret;"); break;
                case "class": sb.AppendLine($"        return ({ret.Cs}?)InstanceBindings.GetOrCreate(__ret, adoptRef: false);"); break;
                case "variant": sb.AppendLine("        return new Variant(__ret);"); break;
                case "builtinref": sb.AppendLine($"        return new {ret.Cs}(__ret);"); break;
            }

            sb.AppendLine("    }");
        }

        sb.AppendLine("}");
        Write(outDir, "GD.gen.cs", sb);
    }

    // ------------------------------------------------------ default args --

    /// <summary>
    /// Maps a Godot default-value expression string to a C# constant default,
    /// or null when inexpressible (caller drops the default chain there).
    /// </summary>
    private static string? DefaultExpr(TypeRef t, string dv)
    {
        switch (t.Kind)
        {
            case "bool":
                return dv is "true" or "false" ? dv : null;
            case "int":
                return long.TryParse(dv, out _) || ulong.TryParse(dv, out _)
                    ? $"unchecked(({t.Cs})({dv}))"
                    : null;
            case "enum":
                return long.TryParse(dv, out _) ? $"({t.Cs})({dv})" : null;
            case "float":
                return dv switch
                {
                    "inf" => t.Cs == "float" ? "float.PositiveInfinity" : "double.PositiveInfinity",
                    "-inf" => t.Cs == "float" ? "float.NegativeInfinity" : "double.NegativeInfinity",
                    "nan" => t.Cs == "float" ? "float.NaN" : "double.NaN",
                    _ => double.TryParse(dv, System.Globalization.CultureInfo.InvariantCulture, out _)
                        ? t.Cs == "float" ? $"{dv}f" : dv
                        : null,
                };
            case "string":
            {
                // String defaults arrive quoted ("...").
                if (dv.Length < 2 || dv[0] != '"' || dv[^1] != '"') return null;
                return '"' + dv[1..^1].Replace("\\", "\\\\").Replace("\"", "\\\"") + '"';
            }
            case "class":
                return dv == "null" ? "null" : null;
            case "variant":
                // default(Variant) is all-zero storage = a valid NIL variant.
                return dv == "null" ? "default" : null;
            case "math":
            {
                // Only all-zero constructor patterns are expressible (= default).
                var open = dv.IndexOf('(');
                if (open < 0 || !dv.EndsWith(')')) return null;
                var inner = dv[(open + 1)..^1].Replace(" ", "");
                return inner.Length == 0 || inner.Split(',').All(p => p is "0" or "0.0" or "-0.0")
                    ? "default"
                    : null;
            }
            default:
                return null; // builtinref and friends: no expressible default yet
        }
    }

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

    private static void EmitProperties(StringBuilder sb, ClassInfo info, HashSet<string> used, bool staticClass)
    {
        var mod = staticClass ? "static " : "";
        foreach (var pi in _propsByClass[info.GdName])
        {
            if (!used.Add(pi.CsName)) { _propsSkipped++; continue; }
            _propsEmitted++;

            var getCall = pi.IndexCast is null ? $"{pi.GetterCs}()" : $"{pi.GetterCs}({pi.IndexCast})";
            sb.AppendLine();
            if (pi.SetterCs is null)
            {
                sb.AppendLine($"    public {mod}{RetType(pi.Type)} {pi.CsName} => {getCall};");
                continue;
            }

            var valueExpr = pi.SetterCast is null ? "value" : $"{pi.SetterCast}value";
            var setCall = pi.IndexCast is null
                ? $"{pi.SetterCs}({valueExpr})"
                : $"{pi.SetterCs}({pi.IndexCast}, {valueExpr})";
            sb.AppendLine($"    public {mod}{RetType(pi.Type)} {pi.CsName}");
            sb.AppendLine("    {");
            sb.AppendLine($"        get => {getCall};");
            sb.AppendLine($"        set => {setCall};");
            sb.AppendLine("    }");
        }
    }

    // ---------------------------------------------------------- virtuals --

    // Virtual dispatch does not yet marshal Variant/collection/callable kinds
    // (borrowed args would need copy semantics distinct from method calls);
    // packed arrays work (copy-in/copy-out like methods).
    private static bool VirtualKindOk(TypeRef t) => t.Kind is not ("variant" or "builtinref" or "opaque16");

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
                "stringname" => $"StringName.Intern(StringNames.Read(*(ulong*)args[{i}]))",
                "math" => $"*({a.type.Cs}*)args[{i}]",
                "class" => $"({a.type.Cs}?)InstanceBindings.GetOrCreate(*(nint*)args[{i}], adoptRef: false)",
                "packed" => a.type.PackedElem == "string"
                    ? $"Packed.ReadStrings((Opaque16*)args[{i}])"
                    : $"Packed.ReadPod<{a.type.PackedElem}>({PackedVt(a.type.PackedGd!)}, {PackedOpIndex(a.type.PackedGd!)}, (Opaque16*)args[{i}])",
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
                    sb.AppendLine($"            *(ulong*)ret = StringNames.CreateOwned({call}?.ToString() ?? \"\");");
                    break;
                case "packed":
                    // Ownership transfers into the engine-destructed ret slot
                    // (default-constructed packed there is empty - no leak).
                    sb.AppendLine(vi.Ret.PackedElem == "string"
                        ? $"            *(Opaque16*)ret = Packed.CreateStrings({call} ?? []);"
                        : $"            *(Opaque16*)ret = Packed.CreatePod<{vi.Ret.PackedElem}>({PackedVt(vi.Ret.PackedGd!)}, {PackedOpIndex(vi.Ret.PackedGd!)}, {call} ?? []);");
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
        if (t == "StringName") return new TypeRef("stringname", "StringName");
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
        if (t == "Callable") return new TypeRef("opaque16", "Callable");
        if (t == "Signal") return new TypeRef("opaque16", "Signal");
        if (PackedElemMap.TryGetValue(t, out var elem)) return new TypeRef("packed", elem + "[]", elem, t);
        return null; // native-struct pointers, rid arrays, ...
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
