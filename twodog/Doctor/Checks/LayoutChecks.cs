using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace twodog.cli;

/// <summary>The project's shape: root files, names, the legacy layouts.</summary>
internal static class LayoutChecks
{
    public static readonly CheckInfo[] Checks =
    [
        new("layout.load-problems", Category.Layout, "every project file parses"),
        new("layout.game-csproj", Category.Layout, "the game csproj exists at the project root"),
        new("layout.assembly-name", Category.Layout, "project.godot names the assembly and the csproj matches it"),
        new("layout.multiple-root-csproj", Category.Layout, "a single csproj at the root, or assembly_name picks one"),
        new("layout.spaced-name", Category.Layout, "the .NET project name contains no whitespace"),
        new("layout.legacy-root-webboot", Category.Layout, "TwoDogWebBoot.cs lives in a web host folder, not at the root"),
        new("layout.root-build-targets", Category.Layout, "Directory.Build.targets provides the deep-clean target"),
        new("layout.root-build-props", Category.Layout, "Directory.Build.props holds the shared package versions"),
        new("layout.root-global-json", Category.Layout, "global.json pins a wasm-capable SDK when a browser host exists"),
    ];

    public static IEnumerable<Finding> Run(DoctorContext ctx)
    {
        const Category c = Category.Layout;
        var p = ctx.Project;

        foreach (var problem in p.LoadProblems)
            yield return new Finding("layout.load-problems", c, Severity.Fail, problem, null, "fix the file, then re-run");
        if (p.LoadProblems.Count == 0) yield return Finding.Pass("layout.load-problems", c, "files parse");

        if (p.GameCsprojPath == null)
        {
            yield return p.Hosts.Count == 0
                ? new Finding("layout.game-csproj", c, Severity.Info, "not a 2dog project yet (no hosts)", null, "2dog add")
                : new Finding("layout.game-csproj", c, Severity.Fail,
                    $"no {p.BaseName}.csproj at the project root, but {p.Hosts.Count} host(s) reference it", null, "2dog add");
        }
        else
        {
            yield return Finding.Pass("layout.game-csproj", c, p.GameCsprojName!);
        }

        var assemblyName = p.Godot?.Get("dotnet", "project/assembly_name");
        if (assemblyName == null && p.GameCsprojPath != null && p.Godot is { } godot && p.BaseName is { } name)
        {
            yield return new Finding("layout.assembly-name", c, Severity.Warn, "project.godot has no [dotnet] assembly_name",
                $"the editor resolves res://{name}.csproj from it", null, "project.godot",
                new Fix("godot:assembly-name", FixClass.Safe, $"append [dotnet] assembly_name=\"{name}\" to project.godot",
                    () => godot.AppendDotnetSection(name)));
        }
        else if (assemblyName != null && !p.RootCsprojs.Contains(assemblyName))
        {
            var found = p.RootCsprojs.Count > 0 ? string.Join(", ", p.RootCsprojs) : "no csproj at the root";
            yield return new Finding("layout.assembly-name", c, Severity.Fail,
                $"project.godot names the assembly '{assemblyName}' but no {assemblyName}.csproj exists (found: {found})",
                "the Godot editor requires res://<assembly_name>.csproj", "rename the csproj or fix assembly_name", "project.godot");
        }
        else if (assemblyName != null)
        {
            yield return Finding.Pass("layout.assembly-name", c, $"assembly_name {assemblyName}");
        }

        if (p.RootCsprojs.Count > 1 && assemblyName == null)
            yield return new Finding("layout.multiple-root-csproj", c, Severity.Fail,
                $"several csproj files at the root ({string.Join(", ", p.RootCsprojs)}) and no assembly_name to pick one",
                null, "set [dotnet] project/assembly_name in project.godot");

        if (p.BaseName is { } spaced && spaced.Any(char.IsWhiteSpace))
        {
            var suggested = Hosts.SanitizeName(spaced) ?? "MyGame";
            yield return new Finding("layout.spaced-name", c, Severity.Fail, $"the .NET project name '{spaced}' contains whitespace",
                ".NET publish silently drops such a project's NuGet packages from hosts that reference it (dotnet/sdk bug)",
                p.Hosts.Count == 0 ? $"2dog add --rename {suggested}" : "see https://2dog.dev/known-issues/spaced-project-names");
        }
        else if (p.BaseName != null)
        {
            yield return Finding.Pass("layout.spaced-name", c, "name");
        }

        if (p.LegacyRootWebBoot)
            yield return new Finding("layout.legacy-root-webboot", c, Severity.Warn, "TwoDogWebBoot.cs sits at the project root (older layout)",
                "it still works, but the Godot editor imports it as a script",
                "move it into the web host folder and let '2dog add --force' wire the guarded Compile Include (2dog never moves files)", "TwoDogWebBoot.cs");

        if (p.Hosts.Count > 0)
        {
            var targets = Path.Combine(p.Dir, "Directory.Build.targets");
            var inherited = FindAbove(p.Dir, "Directory.Build.targets");
            // MSBuild imports only the nearest file: a root file hides the parent's unless it imports it explicitly.
            var inheritedDeepClean = inherited != null && DefinesTarget(inherited, "TwoDogDeepClean");
            var deepClean = p.HasRootBuildTargets
                ? DefinesTarget(targets, "TwoDogDeepClean") || (inheritedDeepClean && ImportsParent(targets, inherited!))
                : inheritedDeepClean;
            if (p.HasRootBuildTargets)
                yield return deepClean
                    ? Finding.Pass("layout.root-build-targets", c, "Directory.Build.targets")
                    : new Finding("layout.root-build-targets", c, Severity.Warn, "Directory.Build.targets does not define the TwoDogDeepClean target",
                        "clean would leave per-configuration outputs behind",
                        "add the target from the template (2dog never edits your Directory.Build.targets)", "Directory.Build.targets");
            else if (inherited != null)
                yield return new Finding("layout.root-build-targets", c, Severity.Info,
                    deepClean ? "Directory.Build.targets comes from a parent directory" : "Directory.Build.targets comes from a parent directory (no TwoDogDeepClean target there)");
            else
                yield return new Finding("layout.root-build-targets", c, Severity.Warn, "Directory.Build.targets missing (TwoDogDeepClean target)",
                    "clean would leave per-configuration outputs behind", null, "Directory.Build.targets",
                    new Fix("create:Directory.Build.targets", FixClass.Safe, "create Directory.Build.targets (shared clean target)",
                        () => File.WriteAllText(targets, TemplateAssets.RootBuildTargets())));
        }

        var literals = p.Hosts.SelectMany(h => h.Packages).Count(r => r.IsManagedLiteral);
        if (literals > 0 && (!p.HasRootBuildProps || !p.PropsValues.ContainsKey("TwoDogVersion")))
            yield return new Finding("layout.root-build-props", c, Severity.Warn,
                $"{literals} package version(s) are literals in host csprojs; no shared Directory.Build.props block",
                "one place to update instead of one per host", "2dog update", PropsPatcher.FileName);
        else if (p.PropsValues.ContainsKey("TwoDogVersion"))
            yield return Finding.Pass("layout.root-build-props", c, "Directory.Build.props");

        if (p.HasWebLikeHost)
        {
            var globalJson = Path.Combine(p.Dir, "global.json");
            if (p.RootGlobalJsonText is { } json)
            {
                // The pin is user-owned: a pre-10 pin that cannot roll forward to a major is reported, never edited.
                var (pin, roll) = DotnetInfo.ParseGlobalJson(json);
                var tooOld = pin != null && pin.Major < 10 && roll.ToLowerInvariant() is not ("latestmajor" or "major");
                yield return tooOld
                    ? new Finding("layout.root-global-json", c, Severity.Warn, $"global.json pins SDK {pin} ({roll}); browser hosts need a .NET 10 SDK",
                        "publishing a browser host from the project root would use that SDK, which has no net10.0 wasm-tools",
                        "pin a 10.0 SDK (2dog never edits global.json)", "global.json")
                    : Finding.Pass("layout.root-global-json", c, pin != null ? $"global.json {pin}" : "global.json");
            }
            else if (FindAbove(p.Dir, "global.json") != null)
            {
                yield return new Finding("layout.root-global-json", c, Severity.Info, "global.json comes from a parent directory");
            }
            else
            {
                yield return new Finding("layout.root-global-json", c, Severity.Warn, "global.json missing at the project root",
                    "publishing a browser host from the project root needs the SDK 10 pin (its own folder has one)", null, "global.json",
                    new Fix("create:global.json", FixClass.Safe, "create global.json (pins a wasm-capable SDK)",
                        () => File.WriteAllText(globalJson, TemplateAssets.RootGlobalJson())));
            }
        }
    }

    /// <summary>A monorepo may keep the file above the project; MSBuild and the SDK resolver find it there.</summary>
    private static string? FindAbove(string dir, string fileName)
    {
        for (var parent = Directory.GetParent(dir); parent != null; parent = parent.Parent)
            if (File.Exists(Path.Combine(parent.FullName, fileName))) return Path.Combine(parent.FullName, fileName);
        return null;
    }

    /// <summary>Whether the file declares a Target of that name (a comment naming it does not count).</summary>
    private static bool DefinesTarget(string path, string name) => Inspect(path, doc => doc.Descendants()
        .Any(e => e.Name.LocalName == "Target" && string.Equals((string?)e.Attribute("Name"), name, StringComparison.OrdinalIgnoreCase)));

    private static readonly Regex PropertyReference = new(@"\$\((?<name>[A-Za-z_]\w*)\)", RegexOptions.Compiled);

    private static readonly Regex FileAboveLookup =
        new(@"GetPathOfFileAbove\(\s*['""]Directory\.Build\.targets['""]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Whether the file imports that parent: an Import whose path (after expanding $(MSBuildThisFileDirectory) and
    /// the file's own properties) resolves to it, or a GetPathOfFileAbove lookup for the same file name, which
    /// resolves to the nearest one above - the parent. Other expressions cannot be evaluated here and do not count.
    /// </summary>
    private static bool ImportsParent(string path, string parent) => Inspect(path, doc =>
    {
        var dir = Path.GetDirectoryName(path)!;
        var properties = doc.Descendants().Where(e => e.Parent?.Name.LocalName == "PropertyGroup")
            .GroupBy(e => e.Name.LocalName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().Value.Trim(), StringComparer.OrdinalIgnoreCase);
        properties["MSBuildThisFileDirectory"] = dir + Path.DirectorySeparatorChar;

        return doc.Descendants().Where(e => e.Name.LocalName == "Import")
            .Select(e => PropertyReference.Replace((string?)e.Attribute("Project") ?? "",
                m => properties.GetValueOrDefault(m.Groups["name"].Value, m.Value)))
            .Any(project => FileAboveLookup.IsMatch(project) || (!project.Contains("$(") && ResolvesTo(project, dir, parent)));
    });

    private static bool ResolvesTo(string project, string dir, string target)
    {
        try
        {
            return HostChecks.SamePath(Path.Combine(dir, project.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)), target);
        }
        catch (ArgumentException) { return false; }
    }

    /// <summary>An unreadable file raises no finding of its own here; a malformed one counts as defining nothing.</summary>
    private static bool Inspect(string path, Func<XDocument, bool> test)
    {
        try { return test(XDocument.Load(path)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }
        catch (XmlException) { return false; }
    }
}
