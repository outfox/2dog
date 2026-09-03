using System.Xml.Linq;

namespace twodog.cli;

/// <summary>
/// The 2dog version block in a project's root Directory.Build.props: the properties every host csproj references.
/// Creates the block in a user-owned props file (in place, whitespace preserved) or reads and rewrites its values.
/// </summary>
internal static class PropsPatcher
{
    public const string FileName = "Directory.Build.props";
    public const string Label = "2dog";

    /// <summary>Property name -> the tool's value, in file order.</summary>
    public static IReadOnlyList<(string Name, string Value)> ToolValues =>
    [
        ("TwoDogVersion", ToolVersions.TwoDogVersion),
        ("TwoDogNativesVersion", ToolVersions.NativesVersion),
        ("TwoDogGodotVersion", ToolVersions.GodotSdkVersion),
        ("TwoDogAvaloniaVersion", ToolVersions.AvaloniaVersion),
        ("TwoDogWindowsAppSdkVersion", ToolVersions.WindowsAppSdkVersion),
        ("TwoDogAspNetCoreVersion", ToolVersions.AspNetCoreVersion),
    ];

    /// <summary>The 2dog property group of a props document, or null.</summary>
    public static XElement? FindGroup(XDocument doc) =>
        doc.Root?.Elements().FirstOrDefault(e =>
            e.Name.LocalName == "PropertyGroup" && (string?)e.Attribute("Label") == Label);

    /// <summary>
    /// The values the block currently holds (name -> value), empty when there is no block. MSBuild property names
    /// are case-insensitive and the last definition wins, so duplicates read the way MSBuild evaluates them, and
    /// the tool's own properties are keyed by their canonical spelling whatever case the file uses.
    /// </summary>
    public static Dictionary<string, string> Read(string propsPath)
    {
        var doc = MsBuildXml.Load(propsPath);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in FindGroup(doc)?.Elements() ?? [])
            values[CanonicalName(element.Name.LocalName)] = element.Value.Trim();
        return values;
    }

    private static string CanonicalName(string name) =>
        ToolValues.FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Name ?? name;

    /// <summary>
    /// The file's text with the 2dog block appended, or null when it already has one. Everything else stays
    /// byte-identical; the block carries its own whitespace.
    /// </summary>
    public static string? AppendBlock(string propsPath)
    {
        var doc = MsBuildXml.Load(propsPath);
        if (FindGroup(doc) != null) return null;
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        MsBuildXml.AppendPropertyGroup(doc, "added by the 2dog tool: the package versions its hosts reference (2dog update rewrites them)",
            ToolValues.Select(v => new XElement(ns + v.Name, v.Value)), Label);
        return MsBuildXml.Serialize(doc);
    }

    /// <summary>
    /// The file's text with the block's values replaced (missing properties added), or null when nothing changes.
    /// </summary>
    public static string? SetValues(string propsPath, IReadOnlyList<(string Name, string Value)> values)
    {
        var doc = MsBuildXml.Load(propsPath);
        var group = FindGroup(doc);
        if (group == null) return null;

        var ns = group.Name.Namespace;
        var changed = false;
        foreach (var (name, value) in values)
        {
            var matches = group.Elements().Where(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0)
            {
                var last = group.Elements().LastOrDefault();
                var indent = last?.PreviousNode is XText text ? text.Value : "\n        ";
                if (last != null) last.AddAfterSelf(new XText(indent), new XElement(ns + name, value));
                else group.Add(new XText(indent), new XElement(ns + name, value), new XText("\n    "));
                changed = true;
            }
            else
            {
                // MSBuild takes the last definition whatever its case: every duplicate gets the value, so none stays stale.
                foreach (var element in matches.Where(e => e.Value.Trim() != value))
                {
                    element.Value = value;
                    changed = true;
                }
            }
        }

        return changed ? MsBuildXml.Serialize(doc) : null;
    }
}
