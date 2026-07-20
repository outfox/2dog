namespace Godot.NativeInterop;

/// <summary>16-byte opaque builtin storage (Callable, Signal, Packed*Array in float_64).</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct Opaque16
{
    public ulong A, B;
}

/// <summary>
/// Packed-array marshalling: GodotSharp compat maps Packed*Array to plain C#
/// arrays (byte[], Vector2[], ...), copying at the boundary. POD elements are
/// contiguous engine-side, so bulk copies go through operator_index(0).
/// size/resize builtin-method hashes are signature-based and identical across
/// all packed types.
/// </summary>
public static unsafe class Packed
{
    private const long SizeHash = 3173160232;
    private const long ResizeHash = 848867239;

    public static Opaque16 CreatePod<T>(GDExtensionVariantType type, delegate* unmanaged<nint, long, nint> opIndex, ReadOnlySpan<T> src)
        where T : unmanaged
    {
        Opaque16 native = default;
        Builtins.Constructor(type, 0)((nint)(&native), null);
        if (src.Length > 0)
        {
            Resize(type, ref native, src.Length);
            var dst = (T*)opIndex((nint)(&native), 0);
            src.CopyTo(new Span<T>(dst, src.Length));
        }
        return native;
    }

    /// <summary>Reads a packed array without consuming it (borrowed, e.g. virtual-call args).</summary>
    public static T[] ReadPod<T>(GDExtensionVariantType type, delegate* unmanaged<nint, long, nint> opIndex, Opaque16* native)
        where T : unmanaged
    {
        var n = Size(type, native);
        if (n == 0) return [];
        var result = new T[n];
        var src = (T*)opIndex((nint)native, 0);
        new ReadOnlySpan<T>(src, n).CopyTo(result);
        return result;
    }

    /// <summary>Reads and destroys an OWNED packed array (ptrcall returns).</summary>
    public static T[] ToPodArray<T>(GDExtensionVariantType type, delegate* unmanaged<nint, long, nint> opIndex, ref Opaque16 native)
        where T : unmanaged
    {
        fixed (Opaque16* p = &native)
        {
            var result = ReadPod<T>(type, opIndex, p);
            Destroy(type, ref native);
            return result;
        }
    }

    public static Opaque16 CreateStrings(ReadOnlySpan<string> src)
    {
        const GDExtensionVariantType type = GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_PACKED_STRING_ARRAY;
        Opaque16 native = default;
        Builtins.Constructor(type, 0)((nint)(&native), null);
        if (src.Length > 0)
        {
            Resize(type, ref native, src.Length);
            var slots = (ulong*)GdExtensionInterface.PackedStringArrayOperatorIndex((nint)(&native), 0);
            for (var i = 0; i < src.Length; i++)
            {
                // resize left default (empty) Strings in the slots: writing an
                // owned String over an empty one transfers cleanly.
                slots[i] = NativeString.Create(src[i]);
            }
        }
        return native;
    }

    public static string[] ReadStrings(Opaque16* native)
    {
        const GDExtensionVariantType type = GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_PACKED_STRING_ARRAY;
        var n = Size(type, native);
        if (n == 0) return [];
        var result = new string[n];
        var slots = (ulong*)GdExtensionInterface.PackedStringArrayOperatorIndex((nint)native, 0);
        for (var i = 0; i < n; i++)
        {
            result[i] = NativeString.Read(in slots[i]);
        }
        return result;
    }

    public static string[] ToStringArray(ref Opaque16 native)
    {
        fixed (Opaque16* p = &native)
        {
            var result = ReadStrings(p);
            Destroy(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_PACKED_STRING_ARRAY, ref native);
            return result;
        }
    }

    public static void Destroy(GDExtensionVariantType type, ref Opaque16 native)
    {
        fixed (Opaque16* p = &native)
        {
            Builtins.Destructor(type)((nint)p);
        }
        native = default;
    }

    private static int Size(GDExtensionVariantType type, Opaque16* native)
    {
        long size = 0;
        Builtins.Method(type, "size", SizeHash)((nint)native, null, (nint)(&size), 0);
        return (int)size;
    }

    private static void Resize(GDExtensionVariantType type, ref Opaque16 native, long newSize)
    {
        fixed (Opaque16* p = &native)
        {
            var args = stackalloc nint[1];
            args[0] = (nint)(&newSize);
            long ret = 0;
            Builtins.Method(type, "resize", ResizeHash)((nint)p, args, (nint)(&ret), 1);
        }
    }
}
