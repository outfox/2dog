using System.Runtime.InteropServices;

namespace Godot.NativeInterop;

/// <summary>
/// An interned Godot StringName. The opaque payload is a single pointer to the
/// engine's interned data, so equal names compare equal by value.
///
/// Ownership: values handed out by <see cref="StringNames.Get"/> are owned by
/// the process-lifetime cache (created is_static where possible, never
/// destructed), so copying the 8-byte payload around without engine
/// copy-constructor calls is safe.
/// </summary>
public readonly struct StringName(ulong opaque) : IEquatable<StringName>
{
    public readonly ulong Opaque = opaque;

    public bool Equals(StringName other) => Opaque == other.Opaque;
    public override bool Equals(object? obj) => obj is StringName other && Equals(other);
    public override int GetHashCode() => Opaque.GetHashCode();
    public static bool operator ==(StringName a, StringName b) => a.Opaque == b.Opaque;
    public static bool operator !=(StringName a, StringName b) => a.Opaque != b.Opaque;
}

public static unsafe class StringNames
{
    private static readonly Dictionary<string, StringName> Cache = [];
    private static readonly Lock CacheLock = new();

    /// <summary>Gets (or creates and caches for process lifetime) the StringName for <paramref name="name"/>.</summary>
    public static StringName Get(string name)
    {
        lock (CacheLock)
        {
            if (Cache.TryGetValue(name, out var cached)) return cached;

            ulong opaque = 0;
            if (System.Text.Ascii.IsValid(name))
            {
                var bytes = System.Text.Encoding.ASCII.GetBytes(name + '\0');
                fixed (byte* p = bytes)
                {
                    // is_static=1: never unref'd, safe across engine teardown.
                    GdExtensionInterface.StringNameNewWithLatin1Chars((nint)(&opaque), (nint)p, 1);
                }
            }
            else
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(name);
                fixed (byte* p = bytes)
                {
                    GdExtensionInterface.StringNameNewWithUtf8CharsAndLen((nint)(&opaque), (nint)p, bytes.Length);
                }
            }

            var sn = new StringName(opaque);
            Cache[name] = sn;
            return sn;
        }
    }

    /// <summary>
    /// Creates an OWNED (non-static, non-cached) StringName whose ownership
    /// transfers to the caller - e.g. written into an engine-destructed
    /// virtual-call return slot.
    /// </summary>
    public static ulong CreateOwned(string name)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(name);
        ulong opaque = 0;
        fixed (byte* p = bytes)
        {
            GdExtensionInterface.StringNameNewWithUtf8CharsAndLen((nint)(&opaque), (nint)p, bytes.Length);
        }
        return opaque;
    }

    /// <summary>
    /// Reads a BORROWED native StringName's text without touching its
    /// ownership. Conversion goes through Variant (STRING_NAME -> STRING),
    /// the supported coercion path.
    /// </summary>
    public static string Read(ulong opaque)
    {
        var fromSn = (delegate* unmanaged<nint, nint, void>)GdExtensionInterface.GetVariantFromTypeConstructor(
            (int)GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_STRING_NAME);
        NativeVariant v;
        fromSn((nint)(&v), (nint)(&opaque));
        var result = Variants.ToManagedString(in v);
        Variants.Destroy(ref v);
        return result;
    }

    /// <summary>
    /// Reads an OWNED native StringName (e.g. a ptrcall return) into a managed
    /// string and destroys it.
    /// </summary>
    public static string ReadAndDestroy(ref ulong opaque)
    {
        var result = Read(opaque);
        var dtor = (delegate* unmanaged<nint, void>)GdExtensionInterface.VariantGetPtrDestructor(
            (int)GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_STRING_NAME);
        fixed (ulong* p = &opaque)
        {
            dtor((nint)p);
        }
        opaque = 0;
        return result;
    }
}
