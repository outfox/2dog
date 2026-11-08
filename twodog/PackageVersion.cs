namespace twodog.cli;

/// <summary>
/// A NuGet version as 2dog compares them, release or prerelease: 4.7.2.152, 4.8.0.154-beta, 4.8.0.160. Ordered as
/// NuGet orders them (<see cref="ToolVersions.CompareNuGet"/>); <see cref="Text"/> keeps it as written.
/// </summary>
internal sealed class PackageVersion : IComparable<PackageVersion>, IEquatable<PackageVersion>
{
    private readonly Version _numbers;

    private PackageVersion(string text, Version numbers, string? label)
    {
        Text = text;
        _numbers = numbers;
        Label = label;
    }

    public string Text { get; }

    /// <summary>The prerelease label ("beta"), or null for a release.</summary>
    public string? Label { get; }

    public int Major => _numbers.Major;
    public int Minor => _numbers.Minor;
    public int Build => Math.Max(_numbers.Build, 0);
    public bool IsPrerelease => Label != null;

    /// <summary>The form NuGet stores it under: no trailing ".0" revision, lower-case label.</summary>
    public string Normalized =>
        (_numbers.Revision > 0 ? _numbers.ToString() : $"{Major}.{Minor}.{Build}") + (Label is { } l ? "-" + l.ToLowerInvariant() : "");

    /// <summary>
    /// The Godot release numbers a 2dog engine or natives version is built for: "4.7.2" for 4.7.2.152, "4.8.0" for
    /// 4.8.0.154-beta. 2dog versions do not carry Godot's prerelease label (that engine may bind 4.8.0-beta.2).
    /// </summary>
    public string GodotVersion => $"{Major}.{Minor}.{Build}";

    /// <summary>
    /// An official prerelease, as opposed to a fork's custom suffix ("4.7.2.139-sora.2"): a 2dog line's label
    /// (4.8.0.154-beta, 4.8.0.160-rc) or Godot's own package form (Godot.NET.Sdk 4.8.0-beta.2, 4.8.0-dev.7).
    /// </summary>
    public bool IsOfficialPrerelease =>
        Label is { } label && System.Text.RegularExpressions.Regex.IsMatch(label,
            _numbers.Revision < 0 ? @"^(dev|beta|rc)(\.\d+)*$" : "^(dev|beta|rc)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>The version in a NuGet version string, exact-pin brackets and build metadata ignored; null when unreadable.</summary>
    public static PackageVersion? Parse(string? text)
    {
        if (text is null) return null;
        var trimmed = text.Trim().Trim('[', ']').Split('+')[0];
        var dash = trimmed.IndexOf('-');
        var numbers = dash < 0 ? trimmed : trimmed[..dash];
        var label = dash < 0 ? null : trimmed[(dash + 1)..];
        if (!Version.TryParse(numbers, out var v) || label is { Length: 0 }) return null;
        return new PackageVersion(trimmed, v, label);
    }

    public int CompareTo(PackageVersion? other) => other is null ? 1 : ToolVersions.CompareNuGet(Text, other.Text) ?? 0;
    public bool Equals(PackageVersion? other) => other is not null && CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is PackageVersion other && Equals(other);
    public override int GetHashCode() => Normalized.GetHashCode();
    public override string ToString() => Text;

    public static bool operator <(PackageVersion a, PackageVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(PackageVersion a, PackageVersion b) => a.CompareTo(b) > 0;
    public static bool operator <=(PackageVersion a, PackageVersion b) => a.CompareTo(b) <= 0;
    public static bool operator >=(PackageVersion a, PackageVersion b) => a.CompareTo(b) >= 0;
    public static bool operator ==(PackageVersion? a, PackageVersion? b) => a is null ? b is null : a.Equals(b);
    public static bool operator !=(PackageVersion? a, PackageVersion? b) => !(a == b);
}
