namespace Godot.NativeInterop;

/// <summary>
/// Runtime CLR type &lt;-&gt; Variant conversion for script-instance member access.
/// Mirrors the type set the source generator supports for [Export]/[Signal].
/// </summary>
internal static class VariantMarshal
{
    internal static bool IsSupported(Type t) =>
        t.IsEnum || typeof(GodotObject).IsAssignableFrom(t)
        || (t.IsArray && t.GetElementType() is { } e && IsSupported(e))
        || Strip(t) switch
        {
            var s when s == typeof(bool) || s == typeof(int) || s == typeof(long)
                || s == typeof(float) || s == typeof(double) || s == typeof(string)
                || s == typeof(Vector2) || s == typeof(Vector3) || s == typeof(Color)
                || s == typeof(Variant) || s == typeof(StringName) || s == typeof(NodePath)
                || s == typeof(Collections.Array) || s == typeof(Collections.Dictionary) => true,
            _ => false,
        };

    private static Type Strip(Type t) => Nullable.GetUnderlyingType(t) ?? t;

    /// <summary>Converts a managed value to an owned Variant (caller disposes/moves).</summary>
    internal static Variant ToVariant(object? value, Type declaredType)
    {
        if (value is null) return default;
        return value switch
        {
            bool b => b,
            int i => i,
            long l => l,
            float f => f,
            double d => d,
            string s => s,
            Vector2 v2 => v2,
            Vector3 v3 => v3,
            Color c => c,
            Variant v => v.Copy(),
            StringName sn => Variant.From(sn),
            NodePath np => Variant.From(np),
            Collections.Array a => Variant.From(a),
            Collections.Dictionary d => Variant.From(d),
            GodotObject o => Variant.From(o),
            Enum e => Convert.ToInt64(e),
            System.Array arr when declaredType.IsArray =>
                ArrayToVariant(arr, declaredType.GetElementType()!),
            _ => throw new NotSupportedException(
                $"twodog: script member type '{declaredType}' is not marshallable."),
        };
    }

    private static Variant ArrayToVariant(System.Array arr, Type elementType)
    {
        using var garr = new Collections.Array();
        foreach (var item in arr)
        {
            using var v = ToVariant(item, elementType);
            garr.Add(v);
        }
        return Variant.From(garr);
    }

    /// <summary>Converts a borrowed Variant to the declared managed type.</summary>
    internal static object? FromVariant(in Variant v, Type declaredType)
    {
        var t = Strip(declaredType);
        if (t.IsEnum) return Enum.ToObject(t, v.AsInt64());
        if (t.IsArray)
        {
            if (v.VariantType == VariantType.Nil) return null;
            var elem = t.GetElementType()!;
            using var garr = v.AsGodotArray();
            var arr = System.Array.CreateInstance(elem, garr.Count);
            for (var i = 0; i < arr.Length; i++)
            {
                using var item = garr[i];
                arr.SetValue(FromVariant(in item, elem), i);
            }
            return arr;
        }
        if (t == typeof(bool)) return v.AsBool();
        if (t == typeof(int)) return v.AsInt32();
        if (t == typeof(long)) return v.AsInt64();
        if (t == typeof(float)) return (float)v.AsDouble();
        if (t == typeof(double)) return v.AsDouble();
        if (t == typeof(string)) return v.AsString();
        if (t == typeof(Vector2)) return v.AsVector2();
        if (t == typeof(Vector3)) return v.AsVector3();
        if (t == typeof(Color)) return v.AsColor();
        if (t == typeof(Variant)) return v.Copy();
        if (t == typeof(StringName)) return v.VariantType == VariantType.Nil ? null : v.AsStringName();
        if (t == typeof(NodePath)) return v.VariantType == VariantType.Nil ? null : v.AsNodePath();
        if (t == typeof(Collections.Array)) return v.VariantType == VariantType.Nil ? null : v.AsGodotArray();
        if (t == typeof(Collections.Dictionary)) return v.VariantType == VariantType.Nil ? null : v.AsGodotDictionary();
        if (typeof(GodotObject).IsAssignableFrom(t)) return v.AsGodotObject();
        throw new NotSupportedException($"twodog: script member type '{declaredType}' is not marshallable.");
    }
}
