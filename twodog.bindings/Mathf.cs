namespace Godot;

/// <summary>
/// GodotSharp-compatible math helpers. Hand-written (GodotSharp homes these
/// here rather than on GD); float-first with double overloads, matching the
/// official surface for the commonly used members.
/// </summary>
public static class Mathf
{
    public const float E = 2.7182818f;
    public const float Sqrt2 = 1.4142136f;
    public const float Pi = 3.1415927f;
    public const float Tau = 6.2831855f;
    public const float Inf = float.PositiveInfinity;
    public const float NaN = float.NaN;
    public const float Epsilon = 1e-06f;

    public static int Abs(int s) => Math.Abs(s);
    public static float Abs(float s) => Math.Abs(s);
    public static double Abs(double s) => Math.Abs(s);

    public static float Ceil(float s) => MathF.Ceiling(s);
    public static double Ceil(double s) => Math.Ceiling(s);
    public static float Floor(float s) => MathF.Floor(s);
    public static double Floor(double s) => Math.Floor(s);
    public static float Round(float s) => MathF.Round(s);
    public static double Round(double s) => Math.Round(s);

    public static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);
    public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
    public static double Clamp(double value, double min, double max) => Math.Clamp(value, min, max);

    public static int Min(int a, int b) => Math.Min(a, b);
    public static float Min(float a, float b) => Math.Min(a, b);
    public static double Min(double a, double b) => Math.Min(a, b);
    public static int Max(int a, int b) => Math.Max(a, b);
    public static float Max(float a, float b) => Math.Max(a, b);
    public static double Max(double a, double b) => Math.Max(a, b);

    public static int Sign(int s) => Math.Sign(s);
    public static float Sign(float s) => s == 0f ? 0f : MathF.Sign(s);
    public static double Sign(double s) => s == 0.0 ? 0.0 : Math.Sign(s);

    public static float Sqrt(float s) => MathF.Sqrt(s);
    public static double Sqrt(double s) => Math.Sqrt(s);
    public static float Pow(float x, float y) => MathF.Pow(x, y);
    public static double Pow(double x, double y) => Math.Pow(x, y);
    public static float Exp(float s) => MathF.Exp(s);
    public static double Exp(double s) => Math.Exp(s);
    public static float Log(float s) => MathF.Log(s);
    public static double Log(double s) => Math.Log(s);

    public static float Sin(float s) => MathF.Sin(s);
    public static double Sin(double s) => Math.Sin(s);
    public static float Cos(float s) => MathF.Cos(s);
    public static double Cos(double s) => Math.Cos(s);
    public static float Tan(float s) => MathF.Tan(s);
    public static double Tan(double s) => Math.Tan(s);
    public static float Asin(float s) => MathF.Asin(s);
    public static double Asin(double s) => Math.Asin(s);
    public static float Acos(float s) => MathF.Acos(s);
    public static double Acos(double s) => Math.Acos(s);
    public static float Atan(float s) => MathF.Atan(s);
    public static double Atan(double s) => Math.Atan(s);
    public static float Atan2(float y, float x) => MathF.Atan2(y, x);
    public static double Atan2(double y, double x) => Math.Atan2(y, x);

    public static float DegToRad(float deg) => deg * (Pi / 180f);
    public static double DegToRad(double deg) => deg * (Math.PI / 180.0);
    public static float RadToDeg(float rad) => rad * (180f / Pi);
    public static double RadToDeg(double rad) => rad * (180.0 / Math.PI);

    public static float Lerp(float from, float to, float weight) => from + (to - from) * weight;
    public static double Lerp(double from, double to, double weight) => from + (to - from) * weight;

    public static float InverseLerp(float from, float to, float weight) => (weight - from) / (to - from);
    public static double InverseLerp(double from, double to, double weight) => (weight - from) / (to - from);

    public static float Remap(float value, float inFrom, float inTo, float outFrom, float outTo) =>
        Lerp(outFrom, outTo, InverseLerp(inFrom, inTo, value));

    public static float MoveToward(float from, float to, float delta) =>
        Abs(to - from) <= delta ? to : from + Sign(to - from) * delta;

    public static double MoveToward(double from, double to, double delta) =>
        Abs(to - from) <= delta ? to : from + Sign(to - from) * delta;

    public static float Wrap(float value, float min, float max)
    {
        var range = max - min;
        return range == 0f ? min : value - range * Floor((value - min) / range);
    }

    public static int Wrap(int value, int min, int max)
    {
        var range = max - min;
        return range == 0 ? min : min + ((value - min) % range + range) % range;
    }

    public static float Snapped(float value, float step) =>
        step != 0f ? Floor(value / step + 0.5f) * step : value;

    public static bool IsEqualApprox(float a, float b)
    {
        if (a == b) return true;
        var tolerance = Epsilon * Abs(a);
        if (tolerance < Epsilon) tolerance = Epsilon;
        return Abs(a - b) < tolerance;
    }

    public static bool IsEqualApprox(double a, double b)
    {
        if (a == b) return true;
        var tolerance = 1e-14 * Abs(a);
        if (tolerance < 1e-14) tolerance = 1e-14;
        return Abs(a - b) < tolerance;
    }

    public static bool IsZeroApprox(float s) => Abs(s) < Epsilon;
    public static bool IsZeroApprox(double s) => Abs(s) < 1e-14;
    public static bool IsFinite(float s) => float.IsFinite(s);
    public static bool IsFinite(double s) => double.IsFinite(s);
    public static bool IsInf(float s) => float.IsInfinity(s);
    public static bool IsInf(double s) => double.IsInfinity(s);
    public static bool IsNaN(float s) => float.IsNaN(s);
    public static bool IsNaN(double s) => double.IsNaN(s);

    public static int PosMod(int a, int b)
    {
        var c = a % b;
        if (c < 0 && b > 0 || c > 0 && b < 0) c += b;
        return c;
    }

    public static float PosMod(float a, float b)
    {
        var c = a % b;
        if (c < 0 && b > 0 || c > 0 && b < 0) c += b;
        return c;
    }

    public static int RoundToInt(float s) => (int)MathF.Round(s);
    public static int FloorToInt(float s) => (int)MathF.Floor(s);
    public static int CeilToInt(float s) => (int)MathF.Ceiling(s);
}
