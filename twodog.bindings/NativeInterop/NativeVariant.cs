using System.Runtime.InteropServices;

namespace Godot.NativeInterop;

/// <summary>
/// A Godot Variant over opaque inline storage (24 bytes in the float_64 build
/// config: 8-byte type tag + 16-byte payload). Must be destroyed exactly once
/// unless it was never initialized. Runtime size is asserted at load time via
/// the engine's own utility `type_string` roundtrip in the test host.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = Size)]
public unsafe struct NativeVariant
{
    public const int Size = 24;
    private fixed byte _data[Size];
}

/// <summary>Construction, conversion, dynamic calls, and destruction for <see cref="NativeVariant"/>.</summary>
public static unsafe class Variants
{
    // from-type: fn(uninitialized_variant*, value*); to-type: fn(value*, variant*)
    private static readonly nint[] FromType = new nint[(int)GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_VARIANT_MAX];
    private static readonly nint[] ToType = new nint[(int)GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_VARIANT_MAX];

    private static delegate* unmanaged<nint, nint, void> From(GDExtensionVariantType type)
    {
        ref var slot = ref FromType[(int)type];
        if (slot == 0) slot = GdExtensionInterface.GetVariantFromTypeConstructor((int)type);
        return (delegate* unmanaged<nint, nint, void>)slot;
    }

    private static delegate* unmanaged<nint, nint, void> To(GDExtensionVariantType type)
    {
        ref var slot = ref ToType[(int)type];
        if (slot == 0) slot = GdExtensionInterface.GetVariantToTypeConstructor((int)type);
        return (delegate* unmanaged<nint, nint, void>)slot;
    }

    // ---- construction ----

    public static NativeVariant FromObject(nint objectPtr)
    {
        NativeVariant v;
        From(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_OBJECT)((nint)(&v), (nint)(&objectPtr));
        return v;
    }

    public static NativeVariant FromBool(bool value)
    {
        NativeVariant v;
        byte b = value ? (byte)1 : (byte)0;
        From(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_BOOL)((nint)(&v), (nint)(&b));
        return v;
    }

    public static NativeVariant FromInt(long value)
    {
        NativeVariant v;
        From(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_INT)((nint)(&v), (nint)(&value));
        return v;
    }

    public static NativeVariant FromFloat(double value)
    {
        NativeVariant v;
        From(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_FLOAT)((nint)(&v), (nint)(&value));
        return v;
    }

    /// <summary>Creates a STRING variant from a managed string (copies; no native String leaks).</summary>
    public static NativeVariant FromString(string value)
    {
        var str = NativeString.Create(value);
        NativeVariant v;
        From(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_STRING)((nint)(&v), (nint)(&str));
        NativeString.Destroy(ref str);
        return v;
    }

    /// <summary>Creates a variant from any blittable value type (math structs, opaque handles).</summary>
    public static NativeVariant FromStruct<T>(GDExtensionVariantType type, in T value) where T : unmanaged
    {
        NativeVariant v;
        fixed (T* p = &value)
        {
            From(type)((nint)(&v), (nint)p);
        }
        return v;
    }

    /// <summary>Creates a variant from a raw type-pointer (ptrcall arg slot).</summary>
    public static NativeVariant FromTypePointer(GDExtensionVariantType type, nint valuePtr)
    {
        NativeVariant v;
        From(type)((nint)(&v), valuePtr);
        return v;
    }

    /// <summary>Writes a variant's coerced value into a raw type-pointer (ptrcall ret slot).</summary>
    public static void ToTypePointer(GDExtensionVariantType type, in NativeVariant v, nint valuePtr)
    {
        fixed (NativeVariant* p = &v)
        {
            To(type)(valuePtr, (nint)p);
        }
    }

    /// <summary>Owned deep copy of a (possibly borrowed) variant.</summary>
    public static NativeVariant NewCopy(in NativeVariant source)
    {
        NativeVariant copy;
        fixed (NativeVariant* p = &source)
        {
            GdExtensionInterface.VariantNewCopy((nint)(&copy), (nint)p);
        }
        return copy;
    }

    // ---- extraction ----

    /// <summary>Converts/coerces a variant to any blittable value type.</summary>
    public static T ToStruct<T>(GDExtensionVariantType type, in NativeVariant v) where T : unmanaged
    {
        T value = default;
        fixed (NativeVariant* p = &v)
        {
            To(type)((nint)(&value), (nint)p);
        }
        return value;
    }

    public static GDExtensionVariantType TypeOf(in NativeVariant v)
    {
        fixed (NativeVariant* p = &v)
        {
            return (GDExtensionVariantType)GdExtensionInterface.VariantGetType((nint)p);
        }
    }

    public static bool ToBool(in NativeVariant v)
    {
        byte b = 0;
        fixed (NativeVariant* p = &v)
        {
            To(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_BOOL)((nint)(&b), (nint)p);
        }
        return b != 0;
    }

    public static long ToInt(in NativeVariant v)
    {
        long value = 0;
        fixed (NativeVariant* p = &v)
        {
            To(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_INT)((nint)(&value), (nint)p);
        }
        return value;
    }

    public static double ToFloat(in NativeVariant v)
    {
        double value = 0;
        fixed (NativeVariant* p = &v)
        {
            To(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_FLOAT)((nint)(&value), (nint)p);
        }
        return value;
    }

    public static nint ToObject(in NativeVariant v)
    {
        nint obj = 0;
        fixed (NativeVariant* p = &v)
        {
            To(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_OBJECT)((nint)(&obj), (nint)p);
        }
        return obj;
    }

    public static string ToManagedString(in NativeVariant v)
    {
        ulong str = 0;
        fixed (NativeVariant* p = &v)
        {
            To(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_STRING)((nint)(&str), (nint)p);
        }
        return NativeString.ReadAndDestroy(ref str);
    }

    // ---- dynamic dispatch ----

    /// <summary>
    /// Dynamic (hash-free) method call on a variant, Godot's `Variant::call`.
    /// Works for any exposed method including fork-only classes; slower than
    /// ptrcall but needs no extension_api hashes.
    /// </summary>
    public static NativeVariant Call(ref NativeVariant self, StringName method, ReadOnlySpan<NativeVariant> args = default)
    {
        NativeVariant ret;
        GDExtensionCallError err;
        var method0 = method.Opaque;
        fixed (NativeVariant* pSelf = &self)
        fixed (NativeVariant* pArgs = args)
        {
            // Build the arg-pointer array (variant_call wants Variant**).
            var argPtrs = stackalloc nint[Math.Max(args.Length, 1)];
            for (var i = 0; i < args.Length; i++) argPtrs[i] = (nint)(pArgs + i);
            GdExtensionInterface.VariantCall((nint)pSelf, (nint)(&method0), (nint)argPtrs, args.Length, (nint)(&ret), (nint)(&err));
        }
        if ((int)err.error != 0)
            throw new InvalidOperationException($"variant_call failed: error={err.error} argument={err.argument} expected={err.expected}");
        return ret;
    }

    // ---- destruction ----

    public static void Destroy(ref NativeVariant v)
    {
        fixed (NativeVariant* p = &v)
        {
            GdExtensionInterface.VariantDestroy((nint)p);
        }
    }
}
