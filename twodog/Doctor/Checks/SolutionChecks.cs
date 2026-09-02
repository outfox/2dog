namespace twodog.cli;

/// <summary>The one solution at the root: format, membership, build exclusions.</summary>
internal static class SolutionChecks
{
    public static readonly CheckInfo[] Checks =
    [
        new("sln.exists", Category.Solution, "a solution exists at the project root"),
        new("sln.multiple", Category.Solution, "exactly one solution contains the game project"),
        new("sln.legacy-format", Category.Solution, "the solution uses the .slnx format"),
        new("sln.contains-game", Category.Solution, "the solution lists the game project"),
        new("sln.contains-hosts", Category.Solution, "the solution lists every host project"),
        new("sln.build-exclusions", Category.Solution, "browser and WinUI hosts are excluded from plain solution builds"),
    ];

    public static IEnumerable<Finding> Run(DoctorContext ctx)
    {
        const Category c = Category.Solution;
        var p = ctx.Project;
        if (p.Hosts.Count == 0 && p.GameCsprojPath == null) yield break;

        var projects = new List<string>();
        if (p.GameCsprojPath != null) projects.Add(p.GameCsprojPath);
        foreach (var host in p.Hosts)
        {
            projects.Add(host.CsprojPath);
            if (host.ClientCsprojPath != null) projects.Add(host.ClientCsprojPath);
        }

        if (p.Solutions.Count == 0)
        {
            var path = Path.Combine(p.Dir, (p.BaseName ?? "Game") + ".slnx");
            yield return new Finding("sln.exists", c, Severity.Warn, "no solution at the project root",
                "the Godot editor and IDEs expect one", null, Path.GetFileName(path),
                new Fix("sln:create", FixClass.Safe, $"create {Path.GetFileName(path)} and add {projects.Count} project(s)", () =>
                {
                    SolutionOps.CreateSolution(path);
                    SolutionOps.AddProjects(path, projects);
                }));
            yield break;
        }

        if (p.Solution is not { } solution)
        {
            yield return new Finding("sln.multiple", c, Severity.Fail,
                $"several solutions at the root ({string.Join(", ", p.Solutions.Select(Path.GetFileName))}) and "
                + (p.GameSolutions.Count > 1 ? "more than one names" : "none names") + " the game project",
                "the Godot editor requires exactly one solution containing the game project", "remove the extras");
            yield break;
        }

        var name = Path.GetFileName(solution);
        // --fix-all migrates a .sln before the later solution fixes run; they follow it to the .slnx.
        string Current() => File.Exists(solution) ? solution : Path.ChangeExtension(solution, ".slnx");

        if (p.Solutions.Count > 1)
            yield return new Finding("sln.multiple", c, Severity.Warn,
                $"{p.Solutions.Count} solutions at the root; {name} is the one naming the game project", null, "remove the extras");

        if (solution.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            yield return new Finding("sln.legacy-format", c, Severity.Info, $"{name} uses the classic format",
                "new 2dog projects use .slnx", null, name,
                new Fix("sln:migrate", FixClass.Announced, $"migrate {name} to {Path.ChangeExtension(name, ".slnx")} (deletes the .sln)",
                    () => SolutionOps.MigrateToSlnx(solution)));

        var text = p.SolutionText ?? "";
        var missing = projects.Where(pr => !text.Contains(Path.GetFileName(pr), StringComparison.OrdinalIgnoreCase)).ToList();
        if (p.GameCsprojPath != null && missing.Contains(p.GameCsprojPath))
            yield return new Finding("sln.contains-game", c, Severity.Fail, $"{name} does not list {p.GameCsprojName}",
                "the Godot editor requires it", null, name,
                new Fix("sln:add", FixClass.Safe, $"add {missing.Count} project(s) to {name}", () => SolutionOps.AddProjects(Current(), missing)));
        else if (p.GameCsprojPath != null)
            yield return Finding.Pass("sln.contains-game", c, $"{name}");

        var missingHosts = missing.Where(m => m != p.GameCsprojPath).Select(m => Path.GetFileName(m)).ToList();
        if (missingHosts.Count > 0)
            yield return new Finding("sln.contains-hosts", c, Severity.Warn, $"{name} does not list {string.Join(", ", missingHosts)}", null, null, name,
                new Fix("sln:add", FixClass.Safe, $"add {missing.Count} project(s) to {name}", () => SolutionOps.AddProjects(Current(), missing)));
        else
            yield return Finding.Pass("sln.contains-hosts", c, $"{projects.Count} projects");

        var excludable = p.Hosts.Where(h => Hosts.ExcludedFromSolutionBuild(h.Kind)).ToList();
        var included = new List<(string Relative, HostKind Kind)>();
        foreach (var host in excludable)
        {
            string[] relatives = host.Kind == HostKind.Blazor
                ? [$"{host.Folder}/{host.Folder}.csproj", Hosts.BlazorClientProject(host.Folder)]
                : [$"{host.Folder}/{host.Folder}.csproj"];
            foreach (var relative in relatives.Where(r => text.Contains(Path.GetFileName(r), StringComparison.OrdinalIgnoreCase)))
                if (!SolutionOps.IsExcludedFromSolutionBuild(solution, relative)) included.Add((relative, host.Kind));
        }

        if (included.Count > 0)
            yield return new Finding("sln.build-exclusions", c, Severity.Warn,
                $"{name} builds {string.Join(", ", included.Select(i => Path.GetFileName(i.Relative)))} in plain solution builds",
                "browser hosts need wasm-tools and WinUI needs Windows; 'dotnet build' of the solution would fail without them", null, name,
                new Fix("sln:exclude", FixClass.Safe, $"exclude {included.Count} host(s) from plain solution builds", () =>
                {
                    // The wasm hosts have no Editor configuration; the WinUI host does.
                    foreach (var (relative, kind) in included)
                        SolutionOps.ExcludeFromSolutionBuild(Current(), relative, mapEditorToDebug: kind is not HostKind.WinUi);
                }));
        else if (excludable.Count > 0)
            yield return Finding.Pass("sln.build-exclusions", c, "build exclusions");
    }
}
