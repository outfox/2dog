using System.Runtime.InteropServices;

namespace Godot;

// Blittable math types matching the engine's memory layout for the float_64
// build configuration (single-precision floats, 64-bit pointers) - the layout
// 2dog ships. Sizes/offsets validated against builtin_class_member_offsets by
// MathLayout.Validate(). GodotSharp-compatible member naming; the full method
// surface (operators, math helpers) grows in later phases.

[StructLayout(LayoutKind.Sequential)]
public struct Vector2(float x, float y) : IEquatable<Vector2>
{
    public float X = x, Y = y;

    public static readonly Vector2 Zero = new(0, 0);
    public static readonly Vector2 One = new(1, 1);

    public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vector2 operator -(Vector2 v) => new(-v.X, -v.Y);
    public static Vector2 operator *(Vector2 a, Vector2 b) => new(a.X * b.X, a.Y * b.Y);
    public static Vector2 operator *(Vector2 v, float s) => new(v.X * s, v.Y * s);
    public static Vector2 operator *(float s, Vector2 v) => new(v.X * s, v.Y * s);
    public static Vector2 operator /(Vector2 a, Vector2 b) => new(a.X / b.X, a.Y / b.Y);
    public static Vector2 operator /(Vector2 v, float s) => new(v.X / s, v.Y / s);
    public static bool operator ==(Vector2 a, Vector2 b) => a.X == b.X && a.Y == b.Y;
    public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);

    public readonly float Length() => MathF.Sqrt(X * X + Y * Y);
    public readonly float LengthSquared() => X * X + Y * Y;

    public readonly Vector2 Normalized()
    {
        var len = Length();
        return len == 0f ? Zero : new Vector2(X / len, Y / len);
    }

    public readonly float Dot(Vector2 with) => X * with.X + Y * with.Y;
    public readonly float DistanceTo(Vector2 to) => (to - this).Length();
    public readonly float DistanceSquaredTo(Vector2 to) => (to - this).LengthSquared();
    public readonly Vector2 DirectionTo(Vector2 to) => (to - this).Normalized();
    public readonly Vector2 Lerp(Vector2 to, float weight) => this + (to - this) * weight;
    public readonly Vector2 Abs() => new(MathF.Abs(X), MathF.Abs(Y));
    public readonly float Angle() => MathF.Atan2(Y, X);
    public readonly Vector2 Rotated(float angle)
    {
        var (sin, cos) = MathF.SinCos(angle);
        return new Vector2(X * cos - Y * sin, X * sin + Y * cos);
    }

    public readonly bool Equals(Vector2 other) => this == other;
    public override readonly bool Equals(object? obj) => obj is Vector2 v && this == v;
    public override readonly int GetHashCode() => HashCode.Combine(X, Y);
    public override readonly string ToString() => $"({X}, {Y})";
}

[StructLayout(LayoutKind.Sequential)]
public struct Vector2I(int x, int y)
{
    public int X = x, Y = y;
    public override string ToString() => $"({X}, {Y})";
}

[StructLayout(LayoutKind.Sequential)]
public struct Rect2(Vector2 position, Vector2 size)
{
    public Vector2 Position = position, Size = size;
}

[StructLayout(LayoutKind.Sequential)]
public struct Rect2I(Vector2I position, Vector2I size)
{
    public Vector2I Position = position, Size = size;
}

[StructLayout(LayoutKind.Sequential)]
public struct Vector3(float x, float y, float z) : IEquatable<Vector3>
{
    public float X = x, Y = y, Z = z;

    public static readonly Vector3 Zero = new(0, 0, 0);
    public static readonly Vector3 One = new(1, 1, 1);

    public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vector3 operator -(Vector3 v) => new(-v.X, -v.Y, -v.Z);
    public static Vector3 operator *(Vector3 a, Vector3 b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
    public static Vector3 operator *(Vector3 v, float s) => new(v.X * s, v.Y * s, v.Z * s);
    public static Vector3 operator *(float s, Vector3 v) => new(v.X * s, v.Y * s, v.Z * s);
    public static Vector3 operator /(Vector3 a, Vector3 b) => new(a.X / b.X, a.Y / b.Y, a.Z / b.Z);
    public static Vector3 operator /(Vector3 v, float s) => new(v.X / s, v.Y / s, v.Z / s);
    public static bool operator ==(Vector3 a, Vector3 b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
    public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);

    public readonly float Length() => MathF.Sqrt(X * X + Y * Y + Z * Z);
    public readonly float LengthSquared() => X * X + Y * Y + Z * Z;

    public readonly Vector3 Normalized()
    {
        var len = Length();
        return len == 0f ? Zero : new Vector3(X / len, Y / len, Z / len);
    }

    public readonly float Dot(Vector3 with) => X * with.X + Y * with.Y + Z * with.Z;
    public readonly Vector3 Cross(Vector3 with) =>
        new(Y * with.Z - Z * with.Y, Z * with.X - X * with.Z, X * with.Y - Y * with.X);
    public readonly float DistanceTo(Vector3 to) => (to - this).Length();
    public readonly float DistanceSquaredTo(Vector3 to) => (to - this).LengthSquared();
    public readonly Vector3 DirectionTo(Vector3 to) => (to - this).Normalized();
    public readonly Vector3 Lerp(Vector3 to, float weight) => this + (to - this) * weight;
    public readonly Vector3 Abs() => new(MathF.Abs(X), MathF.Abs(Y), MathF.Abs(Z));

    public readonly bool Equals(Vector3 other) => this == other;
    public override readonly bool Equals(object? obj) => obj is Vector3 v && this == v;
    public override readonly int GetHashCode() => HashCode.Combine(X, Y, Z);
    public override readonly string ToString() => $"({X}, {Y}, {Z})";
}

[StructLayout(LayoutKind.Sequential)]
public struct Vector3I(int x, int y, int z)
{
    public int X = x, Y = y, Z = z;
}

[StructLayout(LayoutKind.Sequential)]
public struct Transform2D
{
    public Vector2 X, Y, Origin;
}

[StructLayout(LayoutKind.Sequential)]
public struct Vector4(float x, float y, float z, float w)
{
    public float X = x, Y = y, Z = z, W = w;
}

[StructLayout(LayoutKind.Sequential)]
public struct Vector4I(int x, int y, int z, int w)
{
    public int X = x, Y = y, Z = z, W = w;
}

[StructLayout(LayoutKind.Sequential)]
public struct Plane(Vector3 normal, float d)
{
    public Vector3 Normal = normal;
    public float D = d;
}

[StructLayout(LayoutKind.Sequential)]
public struct Quaternion(float x, float y, float z, float w)
{
    public float X = x, Y = y, Z = z, W = w;
}

[StructLayout(LayoutKind.Sequential)]
public struct Aabb(Vector3 position, Vector3 size)
{
    public Vector3 Position = position, Size = size;
}

[StructLayout(LayoutKind.Sequential)]
public struct Basis
{
    public Vector3 Row0, Row1, Row2;
}

[StructLayout(LayoutKind.Sequential)]
public struct Transform3D
{
    public Basis Basis;
    public Vector3 Origin;
}

[StructLayout(LayoutKind.Sequential)]
public struct Projection
{
    public Vector4 X, Y, Z, W;
}

[StructLayout(LayoutKind.Sequential)]
public struct Color(float r, float g, float b, float a = 1f) : IEquatable<Color>
{
    public float R = r, G = g, B = b, A = a;

    public static Color operator *(Color a, Color b) => new(a.R * b.R, a.G * b.G, a.B * b.B, a.A * b.A);
    public static Color operator *(Color c, float s) => new(c.R * s, c.G * s, c.B * s, c.A * s);
    public static bool operator ==(Color a, Color b) => a.R == b.R && a.G == b.G && a.B == b.B && a.A == b.A;
    public static bool operator !=(Color a, Color b) => !(a == b);

    public readonly Color Lerp(Color to, float weight) => new(
        R + (to.R - R) * weight, G + (to.G - G) * weight, B + (to.B - B) * weight, A + (to.A - A) * weight);

    /// <summary>Parses #RGB/#RGBA/#RRGGBB/#RRGGBBAA (leading '#' optional), GodotSharp-style.</summary>
    public static Color FromHtml(string rgba)
    {
        var s = rgba.StartsWith('#') ? rgba.Substring(1) : rgba;
        if (s.Length is 3 or 4) // short form: each nibble doubles
        {
            var chars = new char[s.Length * 2];
            for (var i = 0; i < s.Length; i++) chars[i * 2] = chars[i * 2 + 1] = s[i];
            s = new string(chars);
        }
        if (s.Length is not (6 or 8))
            throw new ArgumentException($"Invalid HTML color: '{rgba}'.", nameof(rgba));
        var r = Convert.ToInt32(s.Substring(0, 2), 16) / 255f;
        var g = Convert.ToInt32(s.Substring(2, 2), 16) / 255f;
        var b = Convert.ToInt32(s.Substring(4, 2), 16) / 255f;
        var a = s.Length == 8 ? Convert.ToInt32(s.Substring(6, 2), 16) / 255f : 1f;
        return new Color(r, g, b, a);
    }

    /// <summary>OK HSL color construction via the engine's own builtin (exact parity).</summary>
    public static unsafe Color FromOkHsl(float hue, float saturation, float lightness, float alpha = 1f)
    {
        // Ptrcall float args travel as doubles; static builtin => null base.
        var method = NativeInterop.Builtins.Method(
            NativeInterop.GDExtensionVariantType.GDEXTENSION_VARIANT_TYPE_COLOR, "from_ok_hsl", 1573799446);
        double h = hue, s = saturation, l = lightness, a = alpha;
        var args = stackalloc nint[4] { (nint)(&h), (nint)(&s), (nint)(&l), (nint)(&a) };
        Color result = default;
        method(0, args, (nint)(&result), 4);
        return result;
    }

    public readonly bool Equals(Color other) => this == other;
    public override readonly bool Equals(object? obj) => obj is Color c && this == c;
    public override readonly int GetHashCode() => HashCode.Combine(R, G, B, A);
    public override readonly string ToString() => $"({R}, {G}, {B}, {A})";
}

/// <summary>Common named colors (GodotSharp-compatible values; set grows on demand).</summary>
public static class Colors
{
    public static readonly Color White = new(1, 1, 1);
    public static readonly Color Black = new(0, 0, 0);
    public static readonly Color Red = new(1, 0, 0);
    public static readonly Color Green = new(0, 1, 0);
    public static readonly Color Blue = new(0, 0, 1);
    public static readonly Color Yellow = new(1, 1, 0);
    public static readonly Color Cyan = new(0, 1, 1);
    public static readonly Color Magenta = new(1, 0, 1);
    public static readonly Color Gray = new(0.502f, 0.502f, 0.502f);
    public static readonly Color DarkGray = new(0.663f, 0.663f, 0.663f);
    public static readonly Color LightGray = new(0.827f, 0.827f, 0.827f);
    public static readonly Color Orange = new(1, 0.647f, 0);
    public static readonly Color Purple = new(0.627f, 0.125f, 0.941f);
    public static readonly Color Pink = new(1, 0.753f, 0.796f);
    public static readonly Color Brown = new(0.647f, 0.165f, 0.165f);
    public static readonly Color Transparent = new(1, 1, 1, 0);
}

[StructLayout(LayoutKind.Sequential)]
public struct Rid
{
    public ulong Id;
}

/// <summary>Compile-us-honest checks against builtin_class_sizes (float_64).</summary>
public static class MathLayout
{
    public static unsafe void Validate()
    {
        Assert(sizeof(Vector2) == 8, nameof(Vector2));
        Assert(sizeof(Vector2I) == 8, nameof(Vector2I));
        Assert(sizeof(Rect2) == 16, nameof(Rect2));
        Assert(sizeof(Rect2I) == 16, nameof(Rect2I));
        Assert(sizeof(Vector3) == 12, nameof(Vector3));
        Assert(sizeof(Vector3I) == 12, nameof(Vector3I));
        Assert(sizeof(Transform2D) == 24, nameof(Transform2D));
        Assert(sizeof(Vector4) == 16, nameof(Vector4));
        Assert(sizeof(Vector4I) == 16, nameof(Vector4I));
        Assert(sizeof(Plane) == 16, nameof(Plane));
        Assert(sizeof(Quaternion) == 16, nameof(Quaternion));
        Assert(sizeof(Aabb) == 24, nameof(Aabb));
        Assert(sizeof(Basis) == 36, nameof(Basis));
        Assert(sizeof(Transform3D) == 48, nameof(Transform3D));
        Assert(sizeof(Projection) == 64, nameof(Projection));
        Assert(sizeof(Color) == 16, nameof(Color));
        Assert(sizeof(Rid) == 8, nameof(Rid));
    }

    private static void Assert(bool ok, string type)
    {
        if (!ok) throw new InvalidOperationException($"Math type layout mismatch: {type} does not match the engine's float_64 layout.");
    }
}
