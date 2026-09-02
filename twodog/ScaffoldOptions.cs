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
    public static string WindowsAppSdkVersion => Metadata("WindowsAppSdkVersion");
    public static string AspNetCoreVersion => Metadata("AspNetCoreVersion");
}

/// <summary>Everything a scaffold run needs, once flags and prompts agree.</summary>
internal sealed class ScaffoldOptions
{
    /// <summary>Directory of the Godot project - existing, or the one to create.</summary>
    public string? ProjectPath;

    /// <summary>Base name: the project name for `new`, an override otherwise.</summary>
    public string? NameOverride;

    /// <summary>
    /// New .NET name for a project whose current name contains whitespace (--rename): whitespace in the restore
    /// identity makes `dotnet publish` silently drop the game's NuGet dependencies from hosts.
    /// </summary>
    public string? RenameTo;

    /// <summary>The hosts to create in this run, resolved from flags or prompts.</summary>
    public List<HostSpec> Hosts = [];

    public bool CreateProject;
    public bool DryRun;
    public bool Force;
    public bool Restore = true;
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

    /// <summary>The pending spaced-name fix, when --rename asked for one.</summary>
    public RenameOperation? Rename { get; init; }

    /// <summary>
    /// Folder names a new host must not use: the recognized hosts plus every other directory in the project, so
    /// scaffolding never lands inside unrelated content.
    /// </summary>
    public IEnumerable<string> TakenFolders =>
        ExistingHosts.Select(h => h.Folder).Concat(ExistingFolders).Distinct(StringComparer.OrdinalIgnoreCase);
}

/// <summary>The rename a spaced-name fix performs, resolved before planning.</summary>
internal sealed record RenameOperation(string OldName, string NewName, bool CsprojExists);

/// <summary>An error with a user-facing message (exit code 2).</summary>
internal class ToolException(string message) : Exception(message);

/// <summary>
/// A project whose .NET name contains whitespace (breaks publish, see --rename); what the interactive layer needs
/// to offer the fix.
/// </summary>
internal sealed class SpacedNameException(string message, string oldName, string? suggested, bool canOfferRename)
    : ToolException(message)
{
    public string OldName { get; } = oldName;
    public string? Suggested { get; } = suggested;

    /// <summary>Whether the automated --rename fix applies (no hosts exist yet).</summary>
    public bool CanOfferRename { get; } = canOfferRename;
}

/// <summary>One step of a plan: what it does, of which kind, and the code that does it.</summary>
internal sealed record PlannedAction(string Description, ActionKind Kind, Action Apply);

/// <summary>What kind of change a planned action makes; the JSON report and the plan summary group by it.</summary>
internal enum ActionKind
{
    CreateDir,
    CreateFile,
    Patch,
    GodotConfig,
    Rename,
    Solution,
    Restore,
}

internal enum ActionStatus
{
    Planned,
    Applied,
    Failed,
    NotRun,
}

internal sealed record ActionReport(string Description, ActionKind Kind, ActionStatus Status);

/// <summary>The outcome of a scaffold run, for the exit code and the --json report.</summary>
internal sealed class ScaffoldResult
{
    public required IReadOnlyList<ActionReport> Actions { get; init; }
    public IReadOnlyList<string> Skipped { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<(string Command, string Comment)> NextSteps { get; init; } = [];
    public bool DryRun { get; init; }
    public bool Cancelled { get; init; }

    /// <summary>The step that failed and why; earlier steps stand, later ones never ran.</summary>
    public (string Description, Exception Error)? Failure { get; init; }

    public int ExitCode => Failure is null ? ExitCodes.Ok : ExitCodes.Error;
}
