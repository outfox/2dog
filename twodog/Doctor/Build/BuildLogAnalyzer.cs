using System.Text.RegularExpressions;
using Spectre.Console;

namespace twodog.cli;

internal sealed record SignatureMatch(BuildSignature Signature, string Line, IReadOnlyList<string> Context);

/// <summary>What a log said: recognized failures, the other errors, and a raw tail when nothing else is there.</summary>
internal sealed record BuildDiagnosis(List<SignatureMatch> Matches, List<string> UnmatchedErrors, List<string> Tail)
{
    public bool HasProblems => Matches.Any(m => m.Signature.Severity == Severity.Fail) || UnmatchedErrors.Count > 0;
}

/// <summary>Matches a build, restore or runtime log against the signature table.</summary>
internal static class BuildLogAnalyzer
{
    /// <summary>The canonical MSBuild diagnostic line: file(line,col): error CODE: message.</summary>
    private static readonly Regex Diagnostic = new(
        @"^\s*(?:(?<file>.+?)(?:\((?<line>\d+),(?<col>\d+)\))?\s*:\s*)?(?<kind>error|warning)\s+(?<code>[A-Z]+\d+)\s*:\s*(?<msg>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static BuildDiagnosis Analyze(string text, ProjectModel? project = null) =>
        Analyze(text.Split('\n').Select(l => l.TrimEnd('\r')).ToList(), project);

    public static BuildDiagnosis Analyze(IReadOnlyList<string> lines, ProjectModel? project = null)
    {
        var matches = new List<SignatureMatch>();
        var covered = new HashSet<int>();
        foreach (var signature in BuildSignatures.All)
        {
            var hits = Enumerable.Range(0, lines.Count).Where(i => signature.Pattern.IsMatch(lines[i])).ToList();
            if (hits.Count == 0) continue;
            var index = hits[0];
            covered.UnionWith(hits);
            var context = lines.Skip(Math.Max(0, index - signature.ContextBefore)).Take(Math.Min(signature.ContextBefore, index)).ToList();
            matches.Add(new SignatureMatch(signature, lines[index].Trim(), context));
        }

        var errors = new List<string>();
        var seen = new HashSet<string>();
        var hostLeak = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var m = Diagnostic.Match(lines[i]);
            if (!m.Success || !m.Groups["kind"].Value.Equals("error", StringComparison.OrdinalIgnoreCase)) continue;
            var key = $"{m.Groups["code"].Value}:{m.Groups["msg"].Value.Trim()}";
            if (project != null && !hostLeak && m.Groups["code"].Value.StartsWith("CS", StringComparison.Ordinal)
                && IsHostLeak(m.Groups["file"].Value, lines[i], project))
                hostLeak = true;
            if (covered.Contains(i) || !seen.Add(key)) continue;
            errors.Add(lines[i].Trim());
        }

        if (hostLeak)
            matches.Add(new SignatureMatch(new BuildSignature("build.host-leak", Diagnostic,
                "host sources compile into the game assembly",
                "2dog doctor --fix (adds the host folders to the game's DefaultItemExcludes)"), "", []));

        var tail = matches.Count == 0 && errors.Count == 0
            ? lines.Where(l => l.Trim().Length > 0).TakeLast(25).ToList()
            : [];
        return new BuildDiagnosis(matches, errors.TakeLast(20).ToList(), tail);
    }

    /// <summary>A compiler error in a host folder, reported while building the game csproj, means a missing exclude.</summary>
    private static bool IsHostLeak(string file, string line, ProjectModel project)
    {
        if (project.GameCsprojName is not { } game || !line.Contains($"[{project.Dir}", StringComparison.OrdinalIgnoreCase)) return false;
        if (!line.Contains(game, StringComparison.OrdinalIgnoreCase)) return false;
        var normalized = file.Replace('\\', '/');
        return project.Hosts.Any(h => normalized.Contains($"/{h.Folder}/", StringComparison.OrdinalIgnoreCase));
    }

    public static void Render(BuildDiagnosis diagnosis)
    {
        foreach (var match in diagnosis.Matches)
        {
            Out.Line($"{DoctorRenderer.Glyph(match.Signature.Severity)} [bold]{Markup.Escape(match.Signature.Title)}[/]");
            foreach (var context in match.Context) Out.Line($"  [grey]> {Markup.Escape(context)}[/]");
            if (match.Line.Length > 0) Out.Line($"  [grey]> {Markup.Escape(match.Line)}[/]");
            Out.Line($"  [grey]run:[/] {Markup.Escape(match.Signature.Remedy)}");
        }

        if (diagnosis.UnmatchedErrors.Count > 0)
        {
            Out.Line(diagnosis.Matches.Count > 0 ? "[bold]other errors[/]" : "[bold]errors[/]");
            foreach (var error in diagnosis.UnmatchedErrors) Out.Line($"  [red]{Markup.Escape(error)}[/]");
        }

        if (diagnosis.Tail.Count > 0)
        {
            Out.Line("[bold]last lines[/] [grey](no known signature matched)[/]");
            foreach (var line in diagnosis.Tail) Out.Line($"  [grey]{Markup.Escape(line)}[/]");
        }

        var known = diagnosis.Matches.Count;
        Out.Line($"[grey]{known} known issue(s) matched, {diagnosis.UnmatchedErrors.Count} other error(s).[/]");
    }
}

/// <summary>Runs `dotnet build` for doctor: plain text output, captured, and written to a log outside the project.</summary>
internal static class BuildRunner
{
    public static (ProcessResult Result, string LogPath, string Target) Run(DoctorContext ctx, string target, string configuration)
    {
        var project = ctx.Project;
        var resolved = target.Length == 0
            ? project.Solution ?? project.GameCsprojPath ?? throw new ToolException("nothing to build: no solution and no game csproj")
            : Resolve(project, target);

        var logDir = Path.Combine(Path.GetTempPath(), "2dog", "doctor");
        Directory.CreateDirectory(logDir);
        var logPath = Path.Combine(logDir, $"{Path.GetFileNameWithoutExtension(resolved)}-{DateTime.Now:yyyyMMdd-HHmmss}.log");

        var result = ctx.Runner.Run(ProcessRunner.Dotnet(project.Dir, $"building {Path.GetFileName(resolved)} ({configuration})",
            TimeSpan.FromMinutes(30), "build", resolved, "-c", configuration, "-nologo", "-v:m", "-clp:NoSummary;ForceNoAlign"),
            Cancellation.Token);
        File.WriteAllLines(logPath, result.Output.Prepend($"$ {result.CommandLine}"));
        return (result, logPath, resolved);
    }

    /// <summary>A host folder name, or a project or solution path relative to the project.</summary>
    private static string Resolve(ProjectModel project, string target)
    {
        if (project.Hosts.FirstOrDefault(h => h.Folder.Equals(target, StringComparison.OrdinalIgnoreCase)) is { } host)
            return host.CsprojPath;
        var path = Path.GetFullPath(Path.Combine(project.Dir, target));
        if (File.Exists(path) || Directory.Exists(path)) return path;
        throw new ToolException($"--build target '{target}' is neither a host folder nor a file in {project.Dir}");
    }
}
