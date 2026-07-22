using Godot.NativeInterop;

namespace Godot;

/// <summary>
/// GodotSharp-compatible StringName: interned engine name with implicit string
/// conversions both ways, so string literals work at every call site. Backed
/// by the process-lifetime interned cache (no per-instance native ownership,
/// no dispose needed).
/// </summary>
public sealed class StringName : IEquatable<StringName>
{
    private readonly string _name;

    /// <summary>Interned opaque payload (cache-owned, stable for the process).</summary>
    internal readonly ulong NativeValue;

    public StringName(string name)
    {
        _name = name;
        NativeValue = StringNames.Get(name).Opaque;
    }

    internal static StringName Intern(string name) => new(name);

    public static implicit operator StringName(string name) => new(name);
    public static implicit operator string(StringName name) => name._name;

    public bool IsEmpty => _name.Length == 0;

    public override string ToString() => _name;

    public bool Equals(StringName? other) => other is not null && NativeValue == other.NativeValue;
    public override bool Equals(object? obj) => obj is StringName other && Equals(other);
    public override int GetHashCode() => NativeValue.GetHashCode();

    public static bool operator ==(StringName? a, StringName? b) =>
        a is null ? b is null : a.Equals(b);

    public static bool operator !=(StringName? a, StringName? b) => !(a == b);
}
