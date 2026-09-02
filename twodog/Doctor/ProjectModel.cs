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

    /// <summary>The single solution at the root, when there is exactly one (or exactly one naming the game project).</summary>
    public string? Solution { get; init; }

    /// <summary>The root solutions naming the game project; more than one is the ambiguity sln.multiple reports.</summary>
    public List<string> GameSolutions { get; init; } = [];

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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { problems.Add($"project.godot: {ex.Message}"); }
        }

        var rootCsprojs = Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.csproj").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order().ToList()
            : [];
        var assemblyName = BareAssemblyName(godot?.Get("dotnet", "project/assembly_name"), problems);
        var baseName = assemblyName
                       ?? (rootCsprojs.Count == 1 ? rootCsprojs[0] : null)
                       ?? twodog.cli.Hosts.SanitizeName(godot?.Get("application", "config/name"))
                       ?? twodog.cli.Hosts.SanitizeName(Path.GetFileName(dir));

        string? gamePath = null, gameText = null;
        XDocument? gameDoc = null;
        if (baseName != null && File.Exists(Path.Combine(dir, baseName + ".csproj")))
        {
            gamePath = Path.Combine(dir, baseName + ".csproj");
            gameText = TryRead(dir, gamePath, problems);
            gameDoc = gameText is null ? null : TryParse(gameText, gamePath, problems);
        }

        var hosts = new List<HostModel>();
        foreach (var existing in HostScan.Find(dir))
        {
            var csproj = Path.Combine(dir, existing.Folder, existing.Folder + ".csproj");
            var text = TryRead(dir, csproj, problems);
            var doc = text is null ? null : TryParse(text, csproj, problems);
            string? clientPath = null, clientText = null;
            if (existing.Kind == HostKind.Blazor)
            {
                var candidate = Path.Combine(dir, twodog.cli.Hosts.BlazorClientProject(existing.Folder).Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(candidate))
                {
                    clientPath = candidate;
                    clientText = TryRead(dir, candidate, problems);
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
            catch (Exception ex) when (ex is System.Xml.XmlException or IOException or UnauthorizedAccessException)
            {
                problems.Add($"{PropsPatcher.FileName}: {ex.Message}");
            }
        }

        var solutionTexts = solutions.ToDictionary(s => s, s => TryRead(dir, s, problems));
        var gameSolutions = baseName is null
            ? []
            : solutions.Where(s => solutionTexts[s]?.Contains($"{baseName}.csproj", StringComparison.OrdinalIgnoreCase) == true).ToList();
        var solution = solutions.Count == 1 ? solutions[0] : gameSolutions.Count == 1 ? gameSolutions[0] : null;

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
            GameSolutions = gameSolutions,
            SolutionText = solution is null ? null : solutionTexts[solution],
            ExportPresetsText = TryRead(dir, Path.Combine(dir, ExportPresetOps.FileName), problems),
            RootGlobalJsonText = TryRead(dir, Path.Combine(dir, "global.json"), problems),
            HasRootBuildTargets = File.Exists(Path.Combine(dir, "Directory.Build.targets")),
            HasRootBuildProps = File.Exists(propsPath),
            PropsValues = propsValues,
            LegacyRootWebBoot = File.Exists(Path.Combine(dir, "TwoDogWebBoot.cs")),
            LoadProblems = problems,
        };
    }

    /// <summary>The file's text, or null when it is absent or unreadable (recorded as a load problem).</summary>
    private static string? TryRead(string dir, string path, List<string> problems)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add($"{Path.GetRelativePath(dir, path).Replace('\\', '/')}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Godot resolves res://&lt;assembly_name&gt;.csproj, so anything but a bare file name is rejected.</summary>
    private static string? BareAssemblyName(string? name, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var bare = name is not ("." or "..") && name.IndexOfAny(new[] { '/', '\\', ':' }) < 0
                   && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !Path.IsPathRooted(name);
        if (bare) return name;
        problems.Add($"project.godot: assembly_name '{name}' is not a plain file name");
        return null;
    }

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
