using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace twodog.cli;

/// <summary>One package reference in a host csproj and how its version is expressed.</summary>
internal sealed record PackageRef(string Id, string RawVersion)
{
    /// <summary>The version without exact-pin brackets, when it is a literal; null for properties and wildcards.</summary>
    public Version? Parsed => Version.TryParse(RawVersion.Trim('[', ']'), out var v) ? v : null;

    /// <summary>An exact pin, "[4.7.1.1]" or "[$(TwoDogNativesVersion)]": a range such as "[4.7.1,5.0.0]" still floats.</summary>
    public bool IsPinned => RawVersion.StartsWith('[') && RawVersion.EndsWith(']') && !RawVersion.Contains(',');
    public bool IsProperty => RawVersion.Contains("$(", StringComparison.Ordinal);
    public bool IsLiteral => Parsed != null;

    /// <summary>A package the shared props block manages, still carrying a literal version.</summary>
    public bool IsManagedLiteral => !IsProperty && VersionRewriter.PropertyFor(Id) != null;
}

/// <summary>
/// Maps the packages the template references onto the version properties of the root Directory.Build.props, and
/// rewrites literal versions in a host csproj to those properties (the `2dog update` migration).
/// </summary>
internal static class VersionRewriter
{
    /// <summary>Package id -> the property it follows and whether the reference is an exact pin.</summary>
    private static readonly IReadOnlyDictionary<string, (string Property, bool Pinned)> Managed =
        new Dictionary<string, (string, bool)>(StringComparer.OrdinalIgnoreCase)
        {
            ["2dog.engine"] = ("TwoDogVersion", false),
            ["2dog.xunit"] = ("TwoDogVersion", false),
            ["2dog.avalonia"] = ("TwoDogVersion", false),
            ["2dog.blazor"] = ("TwoDogVersion", false),
            ["2dog.browser-wasm"] = ("TwoDogNativesVersion", true),
            ["GodotSharpEditor"] = ("TwoDogGodotVersion", false),
            ["Avalonia.Desktop"] = ("TwoDogAvaloniaVersion", false),
            ["Avalonia.Wayland"] = ("TwoDogAvaloniaVersion", false),
            ["Avalonia.Themes.Fluent"] = ("TwoDogAvaloniaVersion", false),
            ["Microsoft.WindowsAppSDK"] = ("TwoDogWindowsAppSdkVersion", false),
            ["Microsoft.AspNetCore.Components.WebAssembly"] = ("TwoDogAspNetCoreVersion", false),
            ["Microsoft.AspNetCore.Components.WebAssembly.Server"] = ("TwoDogAspNetCoreVersion", false),
        };

    /// <summary>The 2dog package ids proper (one version, TwoDogVersion).</summary>
    public static bool IsTwoDogPackage(string id) =>
        Managed.TryGetValue(id, out var m) && m.Property == "TwoDogVersion";

    public static string? PropertyFor(string id) => Managed.TryGetValue(id, out var m) ? m.Property : null;

    /// <summary>The property reference a managed package should carry, e.g. "[$(TwoDogNativesVersion)]".</summary>
    public static string Reference(string id)
    {
        var (property, pinned) = Managed[id];
        return pinned ? $"[$({property})]" : $"$({property})";
    }

    /// <summary>Every PackageReference with a Version attribute in the csproj text.</summary>
    public static List<PackageRef> References(string csprojText) => References(XDocument.Parse(csprojText));

    public static List<PackageRef> References(XDocument doc) =>
        doc.Descendants()
            .Where(e => e.Name.LocalName == "PackageReference")
            .Select(e => ((string?)e.Attribute("Include"), (string?)e.Attribute("Version")))
            .Where(p => p.Item1 != null && p.Item2 != null)
            .Select(p => new PackageRef(p.Item1!, p.Item2!))
            .ToList();

    /// <summary>The managed references that still carry a literal version.</summary>
    public static List<PackageRef> Literals(string csprojText) =>
        References(csprojText).Where(r => r.IsManagedLiteral).ToList();

    // Either XML quote style, any whitespace around '=': the file is edited as text, so both must be recognized.
    private static readonly Regex PackageReferenceTag = new(@"<PackageReference\b[^>]*>", RegexOptions.Compiled);
    private static readonly Regex IncludeAttribute = new(@"\bInclude\s*=\s*(?<q>[""'])(?<id>[^""']*)\k<q>", RegexOptions.Compiled);
    private static readonly Regex VersionAttribute = new(@"\bVersion\s*=\s*(?<q>[""'])(?<version>[^""']*)\k<q>", RegexOptions.Compiled);

    /// <summary>
    /// The csproj with every managed literal version replaced by its property reference, plus what changed; null
    /// text when nothing had to change. A textual edit of the Version attributes only, so the rest of the file
    /// stays byte-identical (an XML writer would re-space attributes).
    /// </summary>
    public static (string? NewText, List<string> Changes) Migrate(string csprojPath)
    {
        var changes = new List<string>();
        var text = PackageReferenceTag.Replace(File.ReadAllText(csprojPath), tag =>
        {
            var include = IncludeAttribute.Match(tag.Value);
            var version = VersionAttribute.Match(tag.Value);
            if (!include.Success || !version.Success) return tag.Value;
            var id = include.Groups["id"].Value;
            var current = version.Groups["version"].Value;
            if (!Managed.ContainsKey(id) || current.Contains("$(", StringComparison.Ordinal)) return tag.Value;
            var reference = Reference(id);
            changes.Add($"{id} {current} -> {reference}");
            var group = version.Groups["version"];
            return tag.Value[..group.Index] + reference + tag.Value[(group.Index + group.Length)..];
        });

        return (changes.Count == 0 ? null : text, changes);
    }

    private static readonly Regex SdkAttribute =
        new(@"\bSdk\s*=\s*(?<q>[""'])Godot\.NET\.Sdk/(?<version>[^""']+)\k<q>", RegexOptions.Compiled);

    /// <summary>The Godot.NET.Sdk version a game csproj declares, or null.</summary>
    public static string? GodotSdkVersion(string csprojText) =>
        SdkAttribute.Match(csprojText) is { Success: true } m ? m.Groups["version"].Value : null;

    /// <summary>
    /// The game csproj text with its Godot.NET.Sdk version replaced, or null when it already matches. Only the
    /// version characters change, so quoting and spacing around the attribute survive.
    /// </summary>
    public static string? SetGodotSdkVersion(string csprojText, string version)
    {
        var match = SdkAttribute.Match(csprojText);
        if (!match.Success || match.Groups["version"].Value == version) return null;
        var group = match.Groups["version"];
        return csprojText[..group.Index] + version + csprojText[(group.Index + group.Length)..];
    }
}
