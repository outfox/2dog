namespace twodog.cli;

/// <summary>Every check group in report order; --list-checks and the docs drift test read the same list.</summary>
internal static class CheckCatalog
{
    public static readonly (Category Category, CheckInfo[] Checks, Func<DoctorContext, IEnumerable<Finding>> Run)[] Groups =
    [
        (Category.Environment, EnvironmentChecks.Checks, EnvironmentChecks.Run),
        (Category.Layout, LayoutChecks.Checks, LayoutChecks.Run),
        (Category.GameProject, GameCsprojChecks.Checks, GameCsprojChecks.Run),
        (Category.Hosts, HostChecks.Checks, HostChecks.Run),
        (Category.Solution, SolutionChecks.Checks, SolutionChecks.Run),
        (Category.Versions, VersionChecks.Checks, VersionChecks.Run),
        (Category.Presets, PresetChecks.Checks, PresetChecks.Run),
        (Category.GodotSettings, GodotSettingsChecks.Checks, GodotSettingsChecks.Run),
    ];

    public static IEnumerable<CheckInfo> All => Groups.SelectMany(g => g.Checks);
}

/// <summary>Runs the catalogue, selects fixes by policy, applies them.</summary>
internal static class DoctorRunner
{
    /// <summary>Every finding, in catalogue order; a crashing check group becomes one Fail rather than a crash.</summary>
    public static List<Finding> RunChecks(DoctorContext ctx)
    {
        var findings = new List<Finding>();
        foreach (var (category, checks, run) in CheckCatalog.Groups)
        {
            try
            {
                findings.AddRange(run(ctx));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var prefix = checks[0].Id.Split('.')[0];
                findings.Add(new Finding($"{prefix}.crashed", category, Severity.Fail,
                    $"the {Categories.Label(category)} checks crashed: {ex.Message}", ex.GetType().Name,
                    "re-run with --verbose and report it at https://github.com/outfox/2dog/issues"));
                Out.Verbose(ex.ToString());
            }
        }

        return findings.Where(f => !ctx.Options.Ignore.Contains(f.Id)).ToList();
    }

    /// <summary>The distinct fixes among the findings (one per key, any severity), in finding order.</summary>
    public static List<Fix> Fixes(IEnumerable<Finding> findings) =>
        findings.Select(f => f.Fix).OfType<Fix>().Where(f => f.Class != FixClass.Manual)
            .GroupBy(f => f.Key).Select(g => g.First()).ToList();

    /// <summary>The fixes a policy applies: safe ones, plus the announced ones under --fix-all.</summary>
    public static List<Fix> Select(IEnumerable<Finding> findings, bool includeAnnounced) =>
        Fixes(findings).Where(f => f.Class == FixClass.Safe || (includeAnnounced && f.Class == FixClass.Announced)).ToList();

    /// <summary>Applies fixes in order; one that fails is reported and the rest still run.</summary>
    public static List<Fix> Apply(IReadOnlyList<Fix> fixes)
    {
        var applied = new List<Fix>();
        foreach (var fix in fixes)
        {
            Out.Action((fix.Class == FixClass.Announced ? "announced: " : "") + fix.Description);
            try
            {
                fix.Apply();
                applied.Add(fix);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var (message, hint) = FriendlyError.Describe(ex);
                Out.ErrorLine($"fix failed: {fix.Description}: {message}");
                if (hint != null) Out.Hint(hint);
                Out.Verbose(ex.ToString());
            }
        }

        return applied;
    }
}
