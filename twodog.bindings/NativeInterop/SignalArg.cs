namespace Godot.NativeInterop;

/// <summary>
/// Safe accessors for borrowed signal/method variant-argument blocks
/// (NativeVariant** as nint). Generated user-assembly code calls these so it
/// never needs unsafe blocks itself.
/// </summary>
public static unsafe class SignalArg
{
    private static ref NativeVariant At(nint args, int index) => ref *((NativeVariant**)args)[index];

    public static bool Bool(nint args, int index) => Variants.ToBool(in At(args, index));
    public static int Int32(nint args, int index) => (int)Variants.ToInt(in At(args, index));
    public static long Int64(nint args, int index) => Variants.ToInt(in At(args, index));
    public static float Single(nint args, int index) => (float)Variants.ToFloat(in At(args, index));
    public static double Double(nint args, int index) => Variants.ToFloat(in At(args, index));
    public static string StringOf(nint args, int index) => Variants.ToManagedString(in At(args, index));

    public static Vector2 Vector2At(nint args, int index) =>
        Variants.ToStruct<Vector2>(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_VECTOR2, in At(args, index));

    public static Vector3 Vector3At(nint args, int index) =>
        Variants.ToStruct<Vector3>(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_VECTOR3, in At(args, index));

    public static Color ColorAt(nint args, int index) =>
        Variants.ToStruct<Color>(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_COLOR, in At(args, index));

    /// <summary>Owned copy; dispose it (or accept the small leak on POD contents).</summary>
    public static Variant VariantAt(nint args, int index) => new(Variants.NewCopy(in At(args, index)));

    public static Godot.StringName StringNameAt(nint args, int index) =>
        Godot.StringName.Intern(Variants.ToManagedString(in At(args, index)));

    /// <summary>Owned Array handle; dispose it.</summary>
    public static Collections.Array ArrayAt(nint args, int index) =>
        new(Variants.ToStruct<ulong>(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_ARRAY, in At(args, index)));

    /// <summary>Owned Dictionary handle; dispose it.</summary>
    public static Collections.Dictionary DictionaryAt(nint args, int index) =>
        new(Variants.ToStruct<ulong>(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_DICTIONARY, in At(args, index)));

    public static NodePath NodePathAt(nint args, int index) =>
        new(Variants.ToStruct<ulong>(GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_NODE_PATH, in At(args, index)));

    public static T? Object<T>(nint args, int index) where T : GodotObject =>
        (T?)InstanceBindings.GetOrCreate(Variants.ToObject(in At(args, index)), adoptRef: false);
}
