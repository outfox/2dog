using System.Text.Json;
using System.Text.Json.Serialization;

namespace twodog.cli;

/// <summary>
/// The --json envelope: exactly one document on stdout per run, also on failure. The common part is the same for
/// every verb; each verb fills its own section and leaves the others null.
/// </summary>
internal sealed class Report
{
    public int Schema { get; init; } = 1;
    public string Tool { get; init; } = "2dog";
    public string Version { get; init; } = ToolVersions.TwoDogVersion;
    public string? Command { get; set; }
    public bool Ok => ExitCode == ExitCodes.Ok;
    public int ExitCode { get; set; }
    public List<string> Warnings { get; set; } = [];
    public List<string> Notes { get; set; } = [];
    public List<string> Errors { get; set; } = [];
    public List<string> Hints { get; set; } = [];

    // new / add
    public ReportProject? Project { get; set; }
    public List<ReportHost>? Hosts { get; set; }
    public bool? DryRun { get; set; }
    public bool? Cancelled { get; set; }
    public List<ReportAction>? Actions { get; set; }
    public List<string>? Skipped { get; set; }
    public List<ReportStep>? NextSteps { get; set; }

    // version
    public List<ReportVersion>? Versions { get; set; }

    // pack list
    public ReportPack? Pack { get; set; }

    // doctor
    public ReportDoctor? Doctor { get; set; }
    public List<ReportCheck>? Checks { get; set; }
}

internal sealed record ReportCheck(string Id, string Category, string Description);

internal sealed record ReportDoctor(List<ReportFinding> Findings, ReportDoctorSummary? Summary, List<string> Applied, ReportBuild? Build);

internal sealed record ReportFinding(
    string Id, string Category, string Severity, string Title, string? Detail, string? Remedy, string? Path, ReportFix? Fix);

internal sealed record ReportFix(string Class, string Description, bool Applied);

internal sealed record ReportDoctorSummary(int Checks, int Pass, int Info, int Warn, int Fail, int SafeFixes, int AnnouncedFixes, int Applied)
{
    public static ReportDoctorSummary Of(IReadOnlyList<Finding> findings, int checks, int applied)
    {
        var fixes = DoctorRunner.Fixes(findings);
        return new ReportDoctorSummary(checks,
            findings.Count(f => f.Severity == Severity.Pass), findings.Count(f => f.Severity == Severity.Info),
            findings.Count(f => f.Severity == Severity.Warn), findings.Count(f => f.Severity == Severity.Fail),
            fixes.Count(f => f.Class == FixClass.Safe), fixes.Count(f => f.Class == FixClass.Announced), applied);
    }
}

internal sealed record ReportBuild(
    string Target, string Configuration, int ExitCode, long DurationMs, string Log, List<ReportMatch> Matches, List<string> UnmatchedErrors);

internal sealed record ReportMatch(string Id, string Title, string Remedy, string Line);

internal sealed record ReportProject(string Dir, string Name, bool IsNew, List<ReportHost> ExistingHosts);

internal sealed record ReportHost(string Kind, string Folder);

internal sealed record ReportAction(string Kind, string Description, string Status);

internal sealed record ReportStep(string Command, string Comment);

internal sealed record ReportVersion(string Label, string Version, string Packages, string? LatestStable, bool? UpToDate);

internal sealed record ReportPack(string Path, uint FormatVersion, string Godot, ulong TotalBytes, List<ReportPackEntry> Entries);

internal sealed record ReportPackEntry(string Path, ulong Size);

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Report))]
internal partial class ReportJsonContext : JsonSerializerContext;

internal static class JsonReport
{
    /// <summary>Completes the envelope from what the run reported and writes it to stdout.</summary>
    public static void Print(Report report, int exitCode)
    {
        report.ExitCode = exitCode;
        report.Warnings = Out.Warnings.ToList();
        report.Notes = Out.Notes.ToList();
        report.Errors = Out.Errors.ToList();
        report.Hints = Out.Hints.ToList();
        Out.Writer.WriteLine(JsonSerializer.Serialize(report, ReportJsonContext.Default.Report));
    }

    /// <summary>Fills the scaffold section (new, add, update) from a run's project and result.</summary>
    public static void Describe(Report report, ProjectContext project, IReadOnlyList<HostSpec> hosts, ScaffoldResult result)
    {
        report.Project = new ReportProject(project.Dir, project.BaseName, project.IsNew,
            project.ExistingHosts.Select(h => new ReportHost(Hosts.Label(h.Kind), h.Folder)).ToList());
        report.Hosts = hosts.Select(h => new ReportHost(Hosts.Label(h.Kind), h.Folder)).ToList();
        report.DryRun = result.DryRun;
        report.Cancelled = result.Cancelled;
        report.Actions = result.Actions.Select(a => new ReportAction(Name(a.Kind), a.Description, Name(a.Status))).ToList();
        report.Skipped = result.Skipped.ToList();
        report.NextSteps = result.NextSteps.Select(s => new ReportStep(s.Command, s.Comment)).ToList();
    }

    /// <summary>Enum names as JSON values: lowerCamelCase.</summary>
    public static string Name<T>(T value) where T : struct, Enum
    {
        var text = value.ToString();
        return char.ToLowerInvariant(text[0]) + text[1..];
    }
}
