using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace twodog.cli;

/// <summary>The machine as the checks see it; tests substitute a fake.</summary>
internal interface IEnvironment
{
    string? Var(string name);
    bool IsWindows { get; }
    bool IsMacOS { get; }
    Architecture Architecture { get; }
    bool FileExists(string path);
    bool DirectoryExists(string path);
}

internal sealed class SystemEnvironment : IEnvironment
{
    public static readonly SystemEnvironment Instance = new();

    public string? Var(string name) => Environment.GetEnvironmentVariable(name);
    public bool IsWindows => OperatingSystem.IsWindows();
    public bool IsMacOS => OperatingSystem.IsMacOS();
    public Architecture Architecture => RuntimeInformation.OSArchitecture;
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
}

/// <summary>What the doctor command line asked for.</summary>
internal sealed class DoctorOptions
{
    public bool Fix;
    public bool FixAll;
    public bool Strict;
    public bool Offline;
    public bool ListChecks;
    public HashSet<string> Ignore = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Run a build: null = no build, "" = the default target, else a host folder or project/solution.</summary>
    public string? BuildTarget;

    public string Configuration = "Debug";

    /// <summary>Analyse an existing log instead of checking a project ("-" = stdin).</summary>
    public string? LogFile;
}

/// <summary>
/// Everything a check may consult: the project, the machine, the tool's own versions, the options, and lazily the
/// subprocess-backed facts (each probe runs at most once, and only when a check needs it). The nuget.org query
/// starts as soon as the context exists, so it overlaps with the file checks and the dotnet probes.
/// </summary>
internal sealed class DoctorContext
{
    public required ProjectModel Project { get; init; }
    public required IEnvironment Env { get; init; }
    public required IProcessRunner Runner { get; init; }
    public required DoctorOptions Options { get; init; }

    private readonly Lazy<List<DotnetInfo.Sdk>> _sdks;
    private readonly Lazy<List<string>?> _workloads;
    private readonly Lazy<string?> _globalPackages;
    private Task<IReadOnlyDictionary<string, string?>>? _latest;
    private Dictionary<string, Version>? _versions;

    public DoctorContext()
    {
        _sdks = new(() => DotnetInfo.ParseSdks(Dotnet("--list-sdks")?.Output ?? []));
        _workloads = new(() => Dotnet("workload", "list") is { Ok: true } r ? DotnetInfo.ParseWorkloads(r.Output) : null);
        _globalPackages = new(() => DotnetInfo.ParseGlobalPackages(Dotnet("nuget", "locals", "global-packages", "-l")?.Output ?? []));
    }

    /// <summary>The same machine facts (probes already run or running) with a freshly loaded project.</summary>
    [SetsRequiredMembers]
    private DoctorContext(DoctorContext other, ProjectModel project)
    {
        Project = project;
        Env = other.Env;
        Runner = other.Runner;
        Options = other.Options;
        _sdks = other._sdks;
        _workloads = other._workloads;
        _globalPackages = other._globalPackages;
        _latest = other._latest;
    }

    public DoctorContext WithProject(ProjectModel project) => new(this, project);

    /// <summary>Kicks off the nuget.org lookup in the background (unless --offline).</summary>
    public DoctorContext Prefetch()
    {
        if (!Options.Offline) _latest ??= Task.Run(() => NuGetLatest.Query(["2dog"]));
        return this;
    }

    /// <summary>Installed SDKs, newest first; empty when dotnet could not be asked.</summary>
    public List<DotnetInfo.Sdk> Sdks => _sdks.Value;

    /// <summary>Installed workload ids, or null when the listing failed.</summary>
    public List<string>? Workloads => _workloads.Value;

    /// <summary>The NuGet global packages folder, or null when unknown.</summary>
    public string? GlobalPackages => _globalPackages.Value;

    /// <summary>The latest stable 2dog tool on nuget.org; null offline or unreachable.</summary>
    public string? LatestTool => _latest?.GetAwaiter().GetResult().GetValueOrDefault("2dog");

    /// <summary>The versions the project uses, by property name (props block, literals, the game Sdk).</summary>
    public Dictionary<string, Version> Versions => _versions ??= ProjectVersions.Current(Project);

    private ProcessResult? Dotnet(params string[] args)
    {
        try
        {
            return Runner.Run(ProcessRunner.Dotnet(Project.Dir, null, TimeSpan.FromMinutes(2), args), Cancellation.Token);
        }
        catch (ToolException)
        {
            return null;
        }
    }
}

/// <summary>The versions a project uses, read from wherever it keeps them.</summary>
internal static class ProjectVersions
{
    public static Dictionary<string, Version> Current(ProjectModel project)
    {
        // MSBuild property names are case-insensitive; a lookup by the canonical name must find a lower-cased block.
        var current = new Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase);
        void Note(string property, PackageRef reference)
        {
            if (reference.Parsed is { } v && (!current.TryGetValue(property, out var have) || v > have))
                current[property] = v;
        }

        foreach (var (name, value) in project.PropsValues) Note(name, new PackageRef(name, value));
        foreach (var host in project.Hosts)
        {
            foreach (var reference in host.Packages.Where(r => r.IsManagedLiteral))
                Note(VersionRewriter.PropertyFor(reference.Id)!, reference);
            if (host.ClientText is { } client)
                foreach (var reference in SafeLiterals(client))
                    Note(VersionRewriter.PropertyFor(reference.Id)!, reference);
        }

        if (project.GameCsprojText is { } game && VersionRewriter.GodotSdkVersion(game) is { } sdk)
            Note("TwoDogGodotVersion", new PackageRef("Godot.NET.Sdk", sdk));
        return current;
    }

    private static List<PackageRef> SafeLiterals(string text)
    {
        try { return VersionRewriter.Literals(text); }
        catch (System.Xml.XmlException) { return []; }
    }
}
