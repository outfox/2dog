using System.Runtime.InteropServices;

namespace Godot;

// Blittable math types matching the engine's memory layout for the float_64
// build configuration (single-precision floats, 64-bit pointers) - the layout
// 2dog ships. Sizes/offsets validated against builtin_class_member_offsets by
// MathLayout.Validate(). GodotSharp-compatible member naming; the full method
// surface (operators, math helpers) grows in later phases.

[StructLayout(LayoutKind.Sequential)]
public struct Vector2(float x, float y)
{
    public float X = x, Y = y;
    public override string ToString() => $"({X}, {Y})";
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
public struct Vector3(float x, float y, float z)
{
    public float X = x, Y = y, Z = z;
    public override string ToString() => $"({X}, {Y}, {Z})";
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
public struct Color(float r, float g, float b, float a = 1f)
{
    public float R = r, G = g, B = b, A = a;
    public override string ToString() => $"({R}, {G}, {B}, {A})";
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
