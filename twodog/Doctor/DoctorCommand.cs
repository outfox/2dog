using Spectre.Console;

namespace twodog.cli;

/// <summary>
/// `2dog doctor`: checks the project and the machine, offers the fixes it can apply, optionally builds and explains
/// the failures. Static checks only by default: fast, and tolerant of being offline.
/// </summary>
internal static class DoctorCommand
{
    /// <summary>Tests inject fakes for the subprocess probes and the machine.</summary>
    internal static IProcessRunner Runner { get; set; } = ProcessRunner.Default;

    internal static IEnvironment Environment { get; set; } = SystemEnvironment.Instance;

    public static int Run(ParsedCommand cmd, Report report)
    {
        var options = cmd.Doctor!;
        if (options.ListChecks) return ListChecks(report);
        if (options.LogFile != null) return AnalyseLog(options.LogFile, cmd.Options.ProjectPath, report);

        var model = ProjectModel.Load(cmd.Options.ProjectPath ?? ".");
        if (!model.HasProjectGodot)
            throw new ToolException($"no project.godot in {model.Dir} - point 2dog doctor at a Godot project directory");

        var interactive = !cmd.NoInteractive && Tui.CanPrompt;
        if (interactive) Tui.Header();
        var ctx = new DoctorContext { Project = model, Env = Environment, Runner = Runner, Options = options }.Prefetch();
        Out.ProjectSummary("project", model.BaseName ?? "?", model.Dir, model.Hosts.Select(h => (h.Folder, h.Kind)));

        var findings = Out.Status("checking", () => DoctorRunner.RunChecks(ctx));
        DoctorRenderer.Render(findings);

        var selected = options.Fix || options.FixAll
            ? DoctorRunner.Select(findings, options.FixAll)
            : interactive ? Tui.SelectFixes(DoctorRunner.Fixes(findings)) : [];
        List<Fix> applied = [];
        var final = findings;
        if (selected.Count > 0)
        {
            Out.Blank();
            applied = DoctorRunner.Apply(selected);
            ctx = ctx.WithProject(ProjectModel.Load(model.Dir));
            final = DoctorRunner.RunChecks(ctx);
            Out.Blank();
            Out.Line("[bold]re-check[/]");
            DoctorRenderer.Render(final);
        }

        Out.Blank();
        DoctorRenderer.Summary(final, options.Strict);
        if (!interactive && selected.Count == 0) DoctorRenderer.HowToFix(final);

        ProcessResult? buildResult = null;
        BuildDiagnosis? diagnosis = null;
        string? logPath = null, target = null;
        if (options.BuildTarget != null)
        {
            Out.Blank();
            Out.Rule("build");
            (buildResult, logPath, target) = BuildRunner.Run(ctx, options.BuildTarget, options.Configuration);
            var verdict = buildResult.Ok ? "[green]succeeded[/]" : $"[red]failed[/] ({buildResult.Outcome})";
            Out.Line($"{Markup.Escape(Path.GetFileName(target))} ({Markup.Escape(options.Configuration)}): {verdict} in {buildResult.Elapsed.TotalSeconds:0.0} s");
            if (!buildResult.Ok)
            {
                diagnosis = BuildLogAnalyzer.Analyze(buildResult.Output, ctx.Project);
                BuildLogAnalyzer.Render(diagnosis);
            }

            Out.Line($"[grey]full log:[/] {Markup.Escape(logPath)}");
        }

        report.Doctor = Describe(final, findings.Count, applied,
            buildResult is null ? null : DescribeBuild(target!, options.Configuration, buildResult, logPath!, diagnosis));
        var failing = final.Any(f => f.Severity == Severity.Fail || (options.Strict && f.Severity == Severity.Warn))
                      || buildResult is { Ok: false };
        return failing ? ExitCodes.Findings : ExitCodes.Ok;
    }

    private static int ListChecks(Report report)
    {
        report.Checks = CheckCatalog.All.Select(c => new ReportCheck(c.Id, Categories.Label(c.Category), c.Description))
            .Concat(BuildSignatures.All.Select(s => new ReportCheck(s.Id, "build log", s.Title)))
            .ToList();
        foreach (var (category, checks, _) in CheckCatalog.Groups)
        {
            Out.Line($"[bold]{Categories.Label(category)}[/]");
            foreach (var check in checks) Out.Plain($"  {check.Id,-28} {check.Description}");
        }

        Out.Line("[bold]build log signatures[/] [grey](--build, --log)[/]");
        foreach (var signature in BuildSignatures.All) Out.Plain($"  {signature.Id,-28} {signature.Title}");
        return ExitCodes.Ok;
    }

    private static int AnalyseLog(string logFile, string? projectPath, Report report)
    {
        var text = logFile == "-" ? Console.In.ReadToEnd() : File.ReadAllText(logFile);
        var model = ProjectModel.Load(projectPath ?? ".");
        var diagnosis = BuildLogAnalyzer.Analyze(text, model.HasProjectGodot ? model : null);
        BuildLogAnalyzer.Render(diagnosis);
        report.Doctor = new ReportDoctor([], null, [],
            new ReportBuild(logFile, "", diagnosis.HasProblems ? 1 : 0, 0, logFile, Matches(diagnosis), diagnosis.UnmatchedErrors));
        return diagnosis.HasProblems ? ExitCodes.Findings : ExitCodes.Ok;
    }

    private static ReportDoctor Describe(IReadOnlyList<Finding> final, int checks, List<Fix> applied, ReportBuild? build)
    {
        var appliedKeys = applied.Select(f => f.Key).ToHashSet();
        var findings = final.Select(f => new ReportFinding(f.Id, Categories.Label(f.Category), JsonReport.Name(f.Severity), f.Title,
            f.Detail, f.Advice, f.Path,
            f.Fix is { } fix ? new ReportFix(JsonReport.Name(fix.Class), fix.Description, appliedKeys.Contains(fix.Key)) : null)).ToList();
        return new ReportDoctor(findings, ReportDoctorSummary.Of(final, checks, applied.Count), applied.Select(f => f.Description).ToList(), build);
    }

    private static ReportBuild DescribeBuild(string target, string configuration, ProcessResult result, string log, BuildDiagnosis? diagnosis) =>
        new(target, configuration, result.ExitCode, (long)result.Elapsed.TotalMilliseconds, log,
            diagnosis is null ? [] : Matches(diagnosis), diagnosis?.UnmatchedErrors ?? []);

    private static List<ReportMatch> Matches(BuildDiagnosis diagnosis) =>
        diagnosis.Matches.Select(m => new ReportMatch(m.Signature.Id, m.Signature.Title, m.Signature.Remedy, m.Line)).ToList();
}
