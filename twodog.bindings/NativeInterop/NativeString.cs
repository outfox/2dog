namespace Godot.NativeInterop;

/// <summary>
/// Helpers for Godot's String builtin (8-byte opaque, refcounted COW).
/// Every native String created or received must be destroyed exactly once.
/// </summary>
public static unsafe class NativeString
{
    /// <summary>Creates a native Godot String from a managed string. Caller owns it.</summary>
    public static ulong Create(string s)
    {
        ulong opaque = 0;
        fixed (char* p = s)
        {
            // default_little_endian=1: BOM-less input is host (LE) order.
            GdExtensionInterface.StringNewWithUtf16CharsAndLen2((nint)(&opaque), (nint)p, s.Length, 1);
        }
        return opaque;
    }

    /// <summary>Reads a native Godot String into a managed string without destroying it.</summary>
    public static string Read(in ulong opaque)
    {
        fixed (ulong* p = &opaque)
        {
            var len = GdExtensionInterface.StringToUtf8Chars((nint)p, 0, 0);
            if (len == 0) return string.Empty;
            var buffer = len <= 512 ? stackalloc byte[(int)len] : new byte[len];
            fixed (byte* pb = buffer)
            {
                GdExtensionInterface.StringToUtf8Chars((nint)p, (nint)pb, len);
            }
            return System.Text.Encoding.UTF8.GetString(buffer);
        }
    }

    /// <summary>Destroys a native Godot String.</summary>
    public static void Destroy(ref ulong opaque)
    {
        var dtor = (delegate* unmanaged<nint, void>)GdExtensionInterface.VariantGetPtrDestructor(
            (int)GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_STRING);
        fixed (ulong* p = &opaque)
        {
            dtor((nint)p);
        }
        opaque = 0;
    }

    /// <summary>Reads and destroys a native Godot String (the common "consume a return value" case).</summary>
    public static string ReadAndDestroy(ref ulong opaque)
    {
        var result = Read(in opaque);
        Destroy(ref opaque);
        return result;
    }
}
