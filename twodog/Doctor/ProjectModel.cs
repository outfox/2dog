using System.Xml.Linq;

namespace twodog.cli;

/// <summary>One host project as found on disk; the csproj may not parse.</summary>
internal sealed class HostModel
{
    public required HostKind Kind { get; init; }
    public required string Folder { get; init; }
    public required string CsprojPath { get; init; }
    public XDocument? Doc { get; init; }
    public bool HasGdIgnore { get; init; }
    public List<PackageRef> Packages { get; init; } = [];

    /// <summary>The Blazor client project nested inside a Blazor host, when present.</summary>
    public string? ClientCsprojPath { get; init; }
    public string? ClientText { get; init; }

    public bool IsWebLike => twodog.cli.Hosts.IsWebLike(Kind);

    /// <summary>The trimmed value of the first element with that local name, anywhere in the csproj, or null.</summary>
    public string? Property(string name) => Doc is null ? null : MsBuildXml.Property(Doc, name);

    /// <summary>Every element with that local name (properties may be repeated per configuration).</summary>
    public IEnumerable<XElement> Properties(string name) => Doc?.Descendants()
        .Where(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? [];

    public bool HasProperty(string name) => Property(name) != null;
}

/// <summary>
/// A read-only view of a 2dog project: everything <see cref="ScaffoldCommand.Open"/> inspects, but anomalies
/// become data instead of exceptions so the checks can report each of them.
/// </summary>
internal sealed class ProjectModel
{
    public required string Dir { get; init; }
    public bool HasProjectGodot => Godot != null;
    public GodotProjectFile? Godot { get; init; }

    /// <summary>The .NET name of the game project (assembly_name, the sole root csproj, or a sanitized name).</summary>
    public string? BaseName { get; init; }

    public List<string> RootCsprojs { get; init; } = [];
    public string? GameCsprojPath { get; init; }
    public string? GameCsprojText { get; init; }
    public XDocument? GameCsproj { get; init; }
    public List<HostModel> Hosts { get; init; } = [];
    public List<string> Solutions { get; init; } = [];
    public string? SolutionText { get; init; }
    public string? ExportPresetsText { get; init; }
    public string? RootGlobalJsonText { get; init; }
    public bool HasRootBuildTargets { get; init; }
    public bool HasRootBuildProps { get; init; }
    public Dictionary<string, string> PropsValues { get; init; } = [];
    public bool LegacyRootWebBoot { get; init; }

    /// <summary>Problems hit while loading (unparseable files); reported once, checks skip what they cannot read.</summary>
    public List<string> LoadProblems { get; init; } = [];

    /// <summary>The single solution at the root, when there is exactly one (or one containing the game project).</summary>
    public string? Solution { get; init; }

    public bool HasWebLikeHost => Hosts.Any(h => h.IsWebLike);
    public string? GameCsprojName => GameCsprojPath is null ? null : Path.GetFileName(GameCsprojPath);

    /// <summary>Every host csproj, plus the Blazor client project nested inside its host.</summary>
    public IEnumerable<string> HostCsprojs =>
        Hosts.SelectMany(h => new[] { h.CsprojPath, h.ClientCsprojPath }).OfType<string>();

    public static ProjectModel Load(string dir)
    {
        dir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));
        var problems = new List<string>();
        var projectGodot = Path.Combine(dir, "project.godot");
        GodotProjectFile? godot = null;
        if (File.Exists(projectGodot))
        {
            try { godot = new GodotProjectFile(projectGodot); }
            catch (IOException ex) { problems.Add($"project.godot: {ex.Message}"); }
        }

        var rootCsprojs = Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.csproj").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order().ToList()
            : [];
        var assemblyName = godot?.Get("dotnet", "project/assembly_name");
        var baseName = assemblyName
                       ?? (rootCsprojs.Count == 1 ? rootCsprojs[0] : null)
                       ?? twodog.cli.Hosts.SanitizeName(godot?.Get("application", "config/name"))
                       ?? twodog.cli.Hosts.SanitizeName(Path.GetFileName(dir));

        string? gamePath = null, gameText = null;
        XDocument? gameDoc = null;
        if (baseName != null && File.Exists(Path.Combine(dir, baseName + ".csproj")))
        {
            gamePath = Path.Combine(dir, baseName + ".csproj");
            gameText = File.ReadAllText(gamePath);
            gameDoc = TryParse(gameText, gamePath, problems);
        }

        var hosts = new List<HostModel>();
        foreach (var existing in HostScan.Find(dir))
        {
            var csproj = Path.Combine(dir, existing.Folder, existing.Folder + ".csproj");
            var text = File.ReadAllText(csproj);
            var doc = TryParse(text, csproj, problems);
            string? clientPath = null, clientText = null;
            if (existing.Kind == HostKind.Blazor)
            {
                var candidate = Path.Combine(dir, twodog.cli.Hosts.BlazorClientProject(existing.Folder).Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(candidate))
                {
                    clientPath = candidate;
                    clientText = File.ReadAllText(candidate);
                }
            }

            hosts.Add(new HostModel
            {
                Kind = existing.Kind,
                Folder = existing.Folder,
                CsprojPath = csproj,
                Doc = doc,
                HasGdIgnore = File.Exists(Path.Combine(dir, existing.Folder, ".gdignore")),
                Packages = doc is null ? [] : VersionRewriter.References(doc),
                ClientCsprojPath = clientPath,
                ClientText = clientText,
            });
        }

        var solutions = Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.sln").Concat(Directory.EnumerateFiles(dir, "*.slnx")).Order().ToList()
            : [];
        var propsPath = Path.Combine(dir, PropsPatcher.FileName);
        var propsValues = new Dictionary<string, string>();
        if (File.Exists(propsPath))
        {
            try { propsValues = PropsPatcher.Read(propsPath); }
            catch (System.Xml.XmlException ex) { problems.Add($"{PropsPatcher.FileName}: {ex.Message}"); }
        }

        var solution = solutions.Count == 1 ? solutions[0]
            : solutions.Count > 1 && baseName != null
                ? solutions.FirstOrDefault(s => File.ReadAllText(s).Contains($"{baseName}.csproj", StringComparison.OrdinalIgnoreCase))
                : null;

        return new ProjectModel
        {
            Dir = dir,
            Godot = godot,
            BaseName = baseName,
            RootCsprojs = rootCsprojs,
            GameCsprojPath = gamePath,
            GameCsprojText = gameText,
            GameCsproj = gameDoc,
            Hosts = hosts,
            Solutions = solutions,
            Solution = solution,
            SolutionText = solution is null ? null : File.ReadAllText(solution),
            ExportPresetsText = ReadIfExists(Path.Combine(dir, ExportPresetOps.FileName)),
            RootGlobalJsonText = ReadIfExists(Path.Combine(dir, "global.json")),
            HasRootBuildTargets = File.Exists(Path.Combine(dir, "Directory.Build.targets")),
            HasRootBuildProps = File.Exists(propsPath),
            PropsValues = propsValues,
            LegacyRootWebBoot = File.Exists(Path.Combine(dir, "TwoDogWebBoot.cs")),
            LoadProblems = problems,
        };
    }

    private static string? ReadIfExists(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

    private static XDocument? TryParse(string text, string path, List<string> problems)
    {
        try
        {
            return XDocument.Parse(text);
        }
        catch (System.Xml.XmlException ex)
        {
            problems.Add($"{Path.GetFileName(path)} is not valid XML (line {ex.LineNumber}): {ex.Message}");
            return null;
        }
    }

}
