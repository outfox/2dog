using System.Reflection;

namespace twodog.cli;

/// <summary>Versions baked in at build time from Directory.Build.props.</summary>
internal static class ToolVersions
{
    private static string Metadata(string key) =>
        typeof(ToolVersions).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == key)?.Value
        ?? throw new InvalidOperationException($"Assembly metadata '{key}' missing");

    public static string TwoDogVersion => Metadata("TwoDogVersion");
    public static string NativesVersion => Metadata("NativesVersion");
    public static string GodotSdkVersion => Metadata("GodotSdkVersion");
    public static string AvaloniaVersion => Metadata("AvaloniaVersion");
}

/// <summary>Everything a scaffold run needs, once flags and prompts agree.</summary>
internal sealed class ScaffoldOptions
{
    /// <summary>Directory of the Godot project - existing, or the one to create.</summary>
    public string? ProjectPath;

    /// <summary>Base name: the project name for `new`, an override otherwise.</summary>
    public string? NameOverride;

    /// <summary>The hosts to create in this run, resolved from flags or prompts.</summary>
    public List<HostSpec> Hosts = [];

    public bool CreateProject;
    public bool DryRun;
    public bool Force;
    public bool Restore = true;
    public bool Verbose;
}

/// <summary>The project a run operates on, resolved before anything is planned.</summary>
internal sealed class ProjectContext
{
    public required string Dir { get; init; }
    public required string BaseName { get; init; }

    /// <summary>The parsed project.godot, or null for a project being created.</summary>
    public GodotProjectFile? Godot { get; init; }

    public List<ExistingHost> ExistingHosts { get; init; } = [];

    /// <summary>Every immediate subdirectory of the project, host or not.</summary>
    public List<string> ExistingFolders { get; init; } = [];

    public bool IsNew { get; init; }

    /// <summary>
    /// Folder names a new host must not use: the recognized hosts plus every
    /// other directory in the project, so scaffolding never lands inside
    /// content that has nothing to do with 2dog.
    /// </summary>
    public IEnumerable<string> TakenFolders =>
        ExistingHosts.Select(h => h.Folder).Concat(ExistingFolders).Distinct(StringComparer.OrdinalIgnoreCase);
}

/// <summary>An error with a user-facing message (exit code 2).</summary>
internal sealed class ToolException(string message) : Exception(message);
