using Godot;

namespace twodog;

/// <summary>Converts host coordinates to the selected engine's math types.</summary>
public static class GodotMath
{
    /// <summary>Creates a vector using the engine's scalar precision.</summary>
    /// <remarks>
    /// The signature is identical in both engine packages, so host integrations can use one
    /// assembly with either precision. Single-precision engines round the coordinates to floats.
    /// </remarks>
    public static Vector2 CreateVector2(double x, double y)
    {
#if REAL_T_IS_DOUBLE
        return new Vector2(x, y);
#else
        return new Vector2((float)x, (float)y);
#endif
    }
}
