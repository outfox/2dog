namespace Godot.NativeInterop;

/// <summary>
/// ResourceFormatLoader for res://*.cs script paths. Unlike the language/script
/// classes this must NOT claim all virtuals - ResourceFormatLoader's virtuals
/// have real C++ defaults (e.g. _recognize_path's extension matching) that
/// blanket-claiming would break. Only _load (Variant-returning, not covered by
/// the generated dispatch) is a custom virtual.
/// </summary>
internal sealed unsafe class CSharpScriptLoader : ResourceFormatLoader
{
    // Every implemented virtual must be listed: the reflection-based override
    // detection treats bindings-assembly declarations as generated stubs, so
    // this class's own overrides would otherwise report as not-overridden.
    internal static readonly string[] LoadCustomVirtuals =
        ["_load", "_handles_type", "_get_recognized_extensions", "_get_resource_type"];

    public override string[] _GetRecognizedExtensions() => ["cs"];

    public override bool _HandlesType(StringName type)
    {
        var t = type.ToString();
        return t is "Script" or "CSharpScript";
    }

    public override string _GetResourceType(string path) =>
        path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ? "CSharpScript" : "";

    private static ulong __vsnLoad;

    internal override bool __CallVirtual(ulong nameSn, nint* args, nint ret)
    {
        if (__vsnLoad == 0) __vsnLoad = StringNames.Get("_load").Opaque;
        if (nameSn == __vsnLoad)
        {
            // _load(path, original_path, use_sub_threads, cache_mode) -> Variant
            var path = NativeString.Read(PayloadSlot.Read(args[0]));
            var script = CSharpScript.ForPath(path);
            var v = Variant.From(script);
            Variants.Destroy(ref *(NativeVariant*)ret);
            *(NativeVariant*)ret = v.Native; // move: engine owns the result
            return true;
        }
        return base.__CallVirtual(nameSn, args, ret);
    }
}
