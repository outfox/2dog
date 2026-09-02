using Spectre.Console;

namespace twodog.cli;

/// <summary>The doctor report: one line per category, expanded findings, a summary. Plain lines only, no live regions.</summary>
internal static class DoctorRenderer
{
    private static readonly Category[] Order =
    [
        Category.Environment, Category.Layout, Category.GameProject, Category.Hosts, Category.Solution,
        Category.Versions, Category.Presets, Category.GodotSettings,
    ];

    private const int LabelWidth = 14;

    /// <summary>The marker text (UTF-8 or its ASCII stand-in) and the colour it is shown in.</summary>
    private static (string Text, string Colour) Marker(Severity severity) => severity switch
    {
        Severity.Pass => (Out.Glyph("✓", "ok"), "green"),
        Severity.Info => (Out.Glyph("·", "-"), "grey"),
        Severity.Warn => ("!", "yellow"),
        _ => (Out.Glyph("✗", "x"), "red"),
    };

    public static string Glyph(Severity severity)
    {
        var (text, colour) = Marker(severity);
        return $"[{colour}]{text}[/]";
    }

    /// <summary>The marker padded to a fixed column: two cells with UTF-8 glyphs, three with the ASCII stand-ins.</summary>
    private static string Column(Severity severity)
    {
        var (text, colour) = Marker(severity);
        return $"[{colour}]{text}[/]" + new string(' ', (Out.Mode.Unicode ? 2 : 3) - text.Length);
    }

    public static void Render(IReadOnlyList<Finding> findings)
    {
        var verbose = Out.Mode.Verbose;
        foreach (var category in Order)
        {
            var group = findings.Where(f => f.Category == category).ToList();
            if (group.Count == 0) continue;
            var label = Categories.Label(category).PadRight(LabelWidth);
            var issues = group.Where(f => f.Severity >= Severity.Warn).ToList();

            if (issues.Count == 0)
            {
                var passes = group.Where(f => f.Severity == Severity.Pass).Select(f => f.Title).Distinct().ToList();
                var summary = passes.Count > 0 ? string.Join(", ", passes) : group[0].Title;
                Out.Info($"{Column(Severity.Pass)}[bold]{Markup.Escape(label)}[/] [grey]{Markup.Escape(Truncate(summary, SummaryWidth))}[/]");
                if (verbose) foreach (var finding in group) Detail(finding);
                continue;
            }

            Out.Line($"{Column(issues.Max(f => f.Severity))}[bold]{Markup.Escape(label.TrimEnd())}[/]");
            foreach (var finding in group.Where(f => f.Severity >= Severity.Warn || verbose))
                Detail(finding);
        }
    }

    private static void Detail(Finding finding)
    {
        if (finding.Severity == Severity.Pass)
        {
            Out.Info($"  {Column(Severity.Pass)}[grey]{Markup.Escape(finding.Id)}[/] {Markup.Escape(finding.Title)}");
            return;
        }

        Out.Line($"  {Column(finding.Severity)}{Markup.Escape(finding.Title)}");
        if (finding.Detail != null) Out.Line($"      [grey]{Markup.Escape(finding.Detail)}[/]");
        if (finding.Fix is { Class: not FixClass.Manual } fix)
            Out.Line($"      [grey]fix:[/] {Markup.Escape(fix.Description)} [grey]({fix.Tag})[/]");
        else if (finding.Advice is { } advice)
            Out.Line($"      [grey]run:[/] {Markup.Escape(advice)}");
    }

    /// <summary>"No issues found (34 checks)." or the counts of what remains and how much of it doctor can fix.</summary>
    public static void Summary(IReadOnlyList<Finding> findings, bool strict)
    {
        var fails = findings.Count(f => f.Severity == Severity.Fail);
        var warns = findings.Count(f => f.Severity == Severity.Warn);
        if (fails == 0 && warns == 0)
        {
            Out.Line($"[green]No issues found[/] [grey]({Plural(findings.Count, "check")}; -v lists every one)[/]");
            return;
        }

        var fixes = DoctorRunner.Fixes(findings);
        var safe = fixes.Count(f => f.Class == FixClass.Safe);
        var announced = fixes.Count(f => f.Class == FixClass.Announced);
        var manual = findings.Count(f => f.Severity >= Severity.Warn && f.Fix is null or { Class: FixClass.Manual });
        var verdict = strict || fails > 0 ? "[red]" : "[yellow]";
        Out.Line($"{verdict}{Plural(fails + warns, "issue")}[/] ({Plural(fails, "error")}, {Plural(warns, "warning")}): " +
                 $"{Plural(safe, "safe fix")}, {announced} announced, {manual} by hand.");
    }

    /// <summary>The commands a non-interactive run points at instead of asking.</summary>
    public static void HowToFix(IReadOnlyList<Finding> findings)
    {
        var fixes = DoctorRunner.Fixes(findings);
        if (fixes.Any(f => f.Class == FixClass.Safe))
            Out.Line($"[grey]apply the safe fixes:[/]      {FixClasses.Command(FixClass.Safe)}");
        if (fixes.Any(f => f.Class == FixClass.Announced))
            Out.Line($"[grey]the announced ones as well:[/] {FixClasses.Command(FixClass.Announced)}");
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 3)] + "...";

    /// <summary>What fits after the glyph and label on this terminal (redirected output never wraps).</summary>
    private static int SummaryWidth => Math.Clamp(Out.Console.Profile.Width - LabelWidth - 4, 40, 200);

    internal static string Plural(int n, string word) => n == 1 ? $"{n} {word}" : $"{n} {word}s";
}
