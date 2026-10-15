using System.Xml.Linq;

namespace twodog.cli;

/// <summary>Project-wide creation settings shared by the game and its hosts.</summary>
internal static class BuildSettings
{
    internal static readonly HashSet<string> PrecisionPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        "2dog.engine", "2dog.godotsharp", "2dog.godotsharp.editor", "2dog.gdunit", "2dog.tools",
        "2dog.browser-wasm", "2dog.win-x64", "2dog.linux-x64", "2dog.osx-arm64",
        "2dog.android-arm64", "2dog.android-x64",
    };

    public static bool IsDouble(string projectDir)
    {
        var props = Path.Combine(projectDir, PropsPatcher.FileName);
        if (File.Exists(props) && ReadProperty(props, "TwoDogPrecision") is { } precision)
            return precision.Equals("double", StringComparison.OrdinalIgnoreCase);
        return Directory.Exists(projectDir) && Directory.EnumerateFiles(projectDir, "*.csproj")
            .Any(path => VersionRewriter.References(MsBuildXml.Load(path))
                .Any(p => p.Id.Equals("2dog.godotsharp.double", StringComparison.OrdinalIgnoreCase)));
    }

    public static bool HasWebThreads(string projectDir)
    {
        var props = Path.Combine(projectDir, PropsPatcher.FileName);
        return File.Exists(props) && ReadProperty(props, "TwoDogWebThreads")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string? ReadProperty(string path, string name) => MsBuildXml.Load(path).Descendants()
        .LastOrDefault(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value.Trim();

    public static void Plan(List<PlannedAction> plan, ProjectContext project, ScaffoldOptions options,
        IEnumerable<string> projects)
    {
        var values = new List<(string Name, string Value)>();
        if (options.DoublePrecision is { } useDouble)
        {
            values.Add(("TwoDogPrecision", useDouble ? "double" : "single"));
            values.Add(("GodotFloat64", useDouble ? "true" : "false"));
        }
        if (options.WebThreads is { } threads)
            values.Add(("TwoDogWebThreads", threads ? "true" : "false"));
        if (values.Count > 0)
        {
            var path = Path.Combine(project.Dir, PropsPatcher.FileName);
            plan.Add(new PlannedAction($"set {string.Join(", ", values.Select(v => $"{v.Name}={v.Value}"))} in {PropsPatcher.FileName}",
                ActionKind.Patch, () =>
                {
                    // Read at apply time: version updates or creation may already have rewritten this file.
                    var doc = MsBuildXml.Load(path);
                    var ns = doc.Root!.Name.Namespace;
                    var group = doc.Root.Elements(ns + "PropertyGroup")
                        .LastOrDefault(e => (string?)e.Attribute("Label") == "2dog build");
                    if (group is null)
                        MsBuildXml.AppendPropertyGroup(doc, "build settings selected with the 2dog tool",
                            values.Select(v => new XElement(ns + v.Name, v.Value)), "2dog build");
                    else
                        foreach (var (name, value) in values)
                        {
                            if (group.Element(ns + name) is { } element) element.Value = value;
                            else group.Add(new XText("\n        "), new XElement(ns + name, value));
                        }
                    MsBuildXml.Write(path, MsBuildXml.Serialize(doc));
                }));
        }

        if (options.DoublePrecision is not { } doublePrecision) return;
        foreach (var path in projects.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            plan.Add(new PlannedAction($"select {(doublePrecision ? "double" : "single")} precision packages in {Path.GetRelativePath(project.Dir, path)}",
                ActionKind.Patch, () =>
                {
                    var doc = MsBuildXml.Load(path);
                    var changed = false;
                    foreach (var reference in doc.Descendants().Where(e => e.Name.LocalName == "PackageReference"))
                    {
                        if (reference.Attribute("Include") is not { } include) continue;
                        var id = include.Value;
                        var baseId = id.EndsWith(".double", StringComparison.OrdinalIgnoreCase) ? id[..^7] : id;
                        if (!PrecisionPackages.Contains(baseId)) continue;
                        var selected = baseId + (doublePrecision ? ".double" : "");
                        if (id == selected) continue;
                        include.Value = selected;
                        changed = true;
                    }
                    if (changed) MsBuildXml.Write(path, MsBuildXml.Serialize(doc));
                }));
        }
    }
}
