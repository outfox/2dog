namespace Godot.NativeInterop;

/// <summary>
/// Cached accessors for builtin-class (Variant-type) operations: constructors
/// by index, destructors, and hashed builtin methods. Hashes come from the
/// builtin_classes section of extension_api.json.
/// </summary>
internal static unsafe class Builtins
{
    private static readonly Dictionary<(GDExtensionVariantType, string), nint> MethodCache = [];
    private static readonly object Gate = new();

    /// <summary>GDExtensionPtrBuiltInMethod: (base, args, ret, argCount).</summary>
    public static delegate* unmanaged<nint, nint*, nint, int, void> Method(GDExtensionVariantType type, string name, long hash)
    {
        lock (Gate)
        {
            if (!MethodCache.TryGetValue((type, name), out var fn))
            {
                var sn = StringNames.Get(name).Opaque;
                fn = GdExtensionInterface.VariantGetPtrBuiltinMethod((int)type, (nint)(&sn), hash);
                if (fn == 0) throw new MissingMethodException($"builtin {type}.{name} (hash {hash}) not found");
                MethodCache[(type, name)] = fn;
            }
            return (delegate* unmanaged<nint, nint*, nint, int, void>)fn;
        }
    }

    /// <summary>GDExtensionPtrConstructor: (uninitialized base, args).</summary>
    public static delegate* unmanaged<nint, nint*, void> Constructor(GDExtensionVariantType type, int index) =>
        (delegate* unmanaged<nint, nint*, void>)GdExtensionInterface.VariantGetPtrConstructor((int)type, index);

    /// <summary>GDExtensionPtrDestructor: (base).</summary>
    public static delegate* unmanaged<nint, void> Destructor(GDExtensionVariantType type) =>
        (delegate* unmanaged<nint, void>)GdExtensionInterface.VariantGetPtrDestructor((int)type);
}
