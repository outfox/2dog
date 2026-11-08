namespace twodog.cli;

/// <summary>
/// A NuGet version as 2dog compares them, release or prerelease: 4.7.2.152, 4.8.0-beta.1.154, 4.8.0.160. Ordered as
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

    /// <summary>The prerelease label ("beta.1.154"), or null for a release.</summary>
    public string? Label { get; }

    public int Major => _numbers.Major;
    public int Minor => _numbers.Minor;
    public int Build => Math.Max(_numbers.Build, 0);
    public bool IsPrerelease => Label != null;

    /// <summary>The form NuGet stores it under: no trailing ".0" revision, lower-case label.</summary>
    public string Normalized =>
        (_numbers.Revision > 0 ? _numbers.ToString() : $"{Major}.{Minor}.{Build}") + (Label is { } l ? "-" + l.ToLowerInvariant() : "");

    /// <summary>
    /// The Godot release a 2dog engine or natives version is built for: the version without its 2dog revision,
    /// "4.7.2" for 4.7.2.152 and "4.8.0-beta.1" for 4.8.0-beta.1.154. A prerelease carries its revision at the end
    /// of the label; a version with four numbers already has it, so a fork's suffix ("4.7.2.139-sora.2") is not Godot's.
    /// </summary>
    public string GodotVersion
    {
        get
        {
            var godot = $"{Major}.{Minor}.{Build}";
            if (Label is not { } label || _numbers.Revision >= 0) return godot;
            var dot = label.LastIndexOf('.');
            return dot > 0 && int.TryParse(label[(dot + 1)..], out _) ? $"{godot}-{label[..dot]}" : godot;
        }
    }

    /// <summary>A Godot prerelease label in Godot's own package form ("beta", "beta.1", "rc.2", with or without a 2dog
    /// revision): official 2dog prereleases, as opposed to a fork's custom suffix.</summary>
    public bool IsGodotPrerelease =>
        Label is { } label && _numbers.Revision < 0 && System.Text.RegularExpressions.Regex.IsMatch(label, @"^(beta|rc)(\.\d+)*$",
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
