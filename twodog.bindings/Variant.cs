using Godot.NativeInterop;
using Array = Godot.Collections.Array;
using Dictionary = Godot.Collections.Dictionary;

namespace Godot;

/// <summary>
/// Public Variant over 24 bytes of inline opaque storage. Owns its engine-side
/// payload: <see cref="Dispose"/> releases it (a no-op for POD contents; for
/// strings/objects/containers it drops the held reference). Copying the struct
/// aliases the payload - treat instances as move-only and dispose exactly once.
/// GodotSharp-style implicit conversions in, As* accessors out.
/// </summary>
public struct Variant : IDisposable
{
    internal NativeVariant Native;

    internal Variant(NativeVariant native) => Native = native;

    public VariantType VariantType => (VariantType)(long)Variants.TypeOf(in Native);

    // ---- in ----

    public static implicit operator Variant(bool value) => new(Variants.FromBool(value));
    public static implicit operator Variant(long value) => new(Variants.FromInt(value));
    public static implicit operator Variant(int value) => new(Variants.FromInt(value));
    public static implicit operator Variant(double value) => new(Variants.FromFloat(value));
    public static implicit operator Variant(float value) => new(Variants.FromFloat(value));
    public static implicit operator Variant(string value) => new(Variants.FromString(value));

    public static implicit operator Variant(Vector2 value) =>
        new(Variants.FromStruct(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_VECTOR2, in value));

    public static implicit operator Variant(Vector3 value) =>
        new(Variants.FromStruct(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_VECTOR3, in value));

    public static implicit operator Variant(Color value) =>
        new(Variants.FromStruct(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_COLOR, in value));

    public static Variant From(GodotObject? value) => new(Variants.FromObject(value?.NativePtr ?? 0));

    public static Variant From(Array value) =>
        new(Variants.FromStruct(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_ARRAY, in value.Native));

    public static Variant From(Dictionary value) =>
        new(Variants.FromStruct(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_DICTIONARY, in value.Native));

    // ---- out ----

    public bool AsBool() => Variants.ToBool(in Native);
    public long AsInt64() => Variants.ToInt(in Native);
    public int AsInt32() => (int)Variants.ToInt(in Native);
    public double AsDouble() => Variants.ToFloat(in Native);
    public string AsString() => Variants.ToManagedString(in Native);

    public Vector2 AsVector2() => Variants.ToStruct<Vector2>(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_VECTOR2, in Native);
    public Vector3 AsVector3() => Variants.ToStruct<Vector3>(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_VECTOR3, in Native);
    public Color AsColor() => Variants.ToStruct<Color>(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_COLOR, in Native);

    public GodotObject? AsGodotObject() =>
        InstanceBindings.GetOrCreate(Variants.ToObject(in Native), adoptRef: false);

    public T? As<T>() where T : GodotObject => (T?)AsGodotObject();

    /// <summary>Extracts an owned Array reference (COW handle; dispose it separately).</summary>
    public Array AsGodotArray() =>
        new(Variants.ToStruct<ulong>(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_ARRAY, in Native));

    /// <summary>Extracts an owned Dictionary reference (COW handle; dispose it separately).</summary>
    public Dictionary AsGodotDictionary() =>
        new(Variants.ToStruct<ulong>(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_DICTIONARY, in Native));

    public override string ToString() => AsString();

    public void Dispose() => Variants.Destroy(ref Native);
}
