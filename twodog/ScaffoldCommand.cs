using System.Text.RegularExpressions;

namespace twodog.cli;

/// <summary>
/// The one scaffolding engine behind every verb: it makes a Godot project
/// directory hold the 2dog host projects it was asked for. `2dog new` creates
/// the Godot project first and then runs the same path; `2dog add` and
/// `2dog convert` run it against an existing one.
///
/// The whole run is planned first as a list of actions, then either printed
/// (--dry-run) or applied - both paths walk the same plan. Hard invariant:
/// the tool only ever creates new files or edits *.csproj / project.godot /
/// *.sln in place; it never moves, renames or deletes anything, and has no
/// VCS awareness.
/// </summary>
internal static class ScaffoldCommand
{
    private sealed record PlannedAction(string Description, Action Apply);

    /// <summary>
    /// Resolves what the run operates on: the project directory, its base
    /// name and the hosts it already has. Validating this up front is what
    /// lets the interactive layer offer a sensible host selection before any
    /// action is planned.
    /// </summary>
    public static ProjectContext Open(ScaffoldOptions options)
    {
        var projectDir = Path.GetFullPath(options.ProjectPath ?? ".");
        var projectGodot = Path.Combine(projectDir, "project.godot");

        if (options.CreateProject)
        {
            if (File.Exists(projectGodot))
                throw new ToolException($"{projectDir} already holds a Godot project - use '2dog add' to add hosts to it");
            var name = Hosts.SanitizeName(options.NameOverride)
                       ?? throw new ToolException(options.NameOverride is null
                           ? "a project name is required to create a new project"
                           : $"'{options.NameOverride}' is not a usable project name - it needs a letter or a digit");
            return new ProjectContext
            {
                Dir = projectDir,
                BaseName = name,
                ExistingFolders = Subdirectories(projectDir),
                IsNew = true,
            };
        }

        if (!File.Exists(projectGodot))
            throw new ToolException($"no project.godot in {projectDir} - point 2dog at a Godot project directory, " +
                                    "or run '2dog new <Name>' to create one");

        var godot = new GodotProjectFile(projectGodot);
        return new ProjectContext
        {
            Dir = projectDir,
            BaseName = DeriveBaseName(options, projectDir, godot),
            Godot = godot,
            ExistingHosts = HostScan.Find(projectDir),
            ExistingFolders = Subdirectories(projectDir),
        };
    }

    private static List<string> Subdirectories(string dir) =>
        Directory.Exists(dir)
            ? Directory.EnumerateDirectories(dir).Select(d => Path.GetFileName(d)).ToList()
            : [];

    /// <summary>
    /// Runs a scaffold. <paramref name="confirm"/>, when given, is shown the
    /// planned action descriptions and decides whether they are applied.
    /// </summary>
    public static int Run(ProjectContext project, ScaffoldOptions options, Func<IReadOnlyList<string>, bool>? confirm = null)
    {
        var projectDir = project.Dir;
        var baseName = project.BaseName;

        var plan = new List<PlannedAction>();
        var warnings = new List<string>();
        var skipped = new List<string>();

        if (project.IsNew)
            PlanNewProject(plan, skipped, options, projectDir, baseName);

        var godotCsproj = Path.Combine(projectDir, baseName + ".csproj");
        var newHosts = options.Hosts;
        var existingHosts = project.ExistingHosts;
        var allHostFolders = existingHosts.Select(h => h.Folder)
            .Concat(newHosts.Select(h => h.Folder))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var wantsWeb = newHosts.Any(h => h.Kind == HostKind.Web) || existingHosts.Any(h => h.Kind == HostKind.Web);

        // Root files that already exist are worth a word during a conversion
        // ("your file was left alone"), but not when hosts are added to a
        // project that 2dog already set up - there they are simply ours.
        var converting = !project.IsNew && existingHosts.Count == 0;

        PlanGodotCsproj(plan, warnings, project, godotCsproj, allHostFolders);
        PlanRootBuildTargets(plan, warnings, projectDir, converting);
        PlanRootGlobalJson(plan, warnings, projectDir, wantsWeb, converting);
        PlanWebBoot(plan, skipped, options, projectDir, converting);
        PlanExportPresets(plan, projectDir, wantsWeb);
        PlanHosts(plan, skipped, options, projectDir, baseName, newHosts);
        PlanSolution(plan, options, projectDir, baseName, godotCsproj, allHostFolders, newHosts);

        foreach (var warning in warnings)
            Console.WriteLine($"warning: {warning}");
        foreach (var skip in skipped)
            Console.WriteLine($"skip: {skip} (exists; use --force to overwrite)");

        if (plan.Count == 0)
        {
            Console.WriteLine("Nothing to do - the project already has everything that was asked for.");
            return 0;
        }

        if (options.DryRun)
        {
            foreach (var action in plan)
                Console.WriteLine("would: " + action.Description);
            Console.WriteLine($"\nDry run: {plan.Count} action(s) planned, nothing changed.");
            return 0;
        }

        if (confirm != null && !confirm(plan.Select(a => a.Description).ToList()))
        {
            Console.WriteLine("Cancelled - nothing changed.");
            return 0;
        }

        foreach (var action in plan)
        {
            Console.WriteLine(action.Description);
            action.Apply();
        }

        PrintNextSteps(project, newHosts);
        return 0;
    }

    // internal for unit tests
    internal static string DeriveBaseName(ScaffoldOptions options, string projectDir, GodotProjectFile godotProject)
    {
        var rootCsprojs = Directory.EnumerateFiles(projectDir, "*.csproj")
            .Select(Path.GetFileNameWithoutExtension)
            .Cast<string>()
            .ToList();

        if (options.NameOverride is { } forced)
        {
            var name = Hosts.SanitizeName(forced)
                       ?? throw new ToolException($"--name '{forced}' is not a usable name - it needs a letter or a digit");
            if (godotProject.Get("dotnet", "project/assembly_name") is { } existing && existing != name)
                throw new ToolException(
                    $"--name '{name}' conflicts with project.godot's assembly_name '{existing}'; " +
                    "the Godot editor requires the csproj to be named after the assembly name.");
            return name;
        }

        // 1. [dotnet] project/assembly_name is authoritative: Godot resolves
        //    res://<assembly_name>.csproj from it.
        if (godotProject.Get("dotnet", "project/assembly_name") is { } assemblyName)
        {
            if (rootCsprojs.Count > 0 && !rootCsprojs.Contains(assemblyName))
                throw new ToolException(
                    $"project.godot names the assembly '{assemblyName}' but no {assemblyName}.csproj exists " +
                    $"(found: {string.Join(", ", rootCsprojs)}). Fix the mismatch (the Godot editor requires " +
                    "res://<assembly_name>.csproj), then re-run.");
            return assemblyName;
        }

        // 2. A single existing csproj names the project.
        switch (rootCsprojs.Count)
        {
            case 1:
                return rootCsprojs[0];
            case > 1:
                throw new ToolException(
                    $"multiple csproj files at the project root ({string.Join(", ", rootCsprojs)}) and no " +
                    "[dotnet] assembly_name in project.godot to pick one; pass --name.");
        }

        // 3. Godot's display name, then the directory name.
        return Hosts.SanitizeName(godotProject.Get("application", "config/name"))
               ?? Hosts.SanitizeName(Path.GetFileName(projectDir))
               ?? throw new ToolException("could not derive a project name; pass --name");
    }

    private static void PlanNewProject(
        List<PlannedAction> plan, List<string> skipped, ScaffoldOptions options, string projectDir, string baseName)
    {
        if (!Directory.Exists(projectDir))
            plan.Add(new PlannedAction($"create directory {projectDir}", () => Directory.CreateDirectory(projectDir)));

        foreach (var (relativePath, content) in TemplateAssets.NewProjectFiles(baseName))
        {
            var target = Path.Combine(projectDir, relativePath);
            if (File.Exists(target) && !options.Force)
            {
                skipped.Add(relativePath);
                continue;
            }

            plan.Add(new PlannedAction($"create {relativePath}", () =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, content);
            }));
        }
    }

    private static void PlanGodotCsproj(
        List<PlannedAction> plan, List<string> warnings, ProjectContext project,
        string godotCsproj, List<string> hostFolders)
    {
        if (!File.Exists(godotCsproj))
        {
            // GDScript-only project (or a brand-new one): scaffold the csproj
            // and declare the assembly so the Godot editor finds it
            // (res://<name>.csproj).
            var content = SetHostExcludes(TemplateAssets.GodotCsproj(project.BaseName), hostFolders);
            plan.Add(new PlannedAction(
                $"create {Path.GetFileName(godotCsproj)} (Godot.NET.Sdk/{ToolVersions.GodotSdkVersion})",
                () => File.WriteAllText(godotCsproj, content)));

            // A new project's project.godot already declares [dotnet].
            if (project.Godot is { } godot && !godot.HasSection("dotnet"))
                plan.Add(new PlannedAction(
                    $"append [dotnet] assembly_name=\"{project.BaseName}\" to project.godot",
                    () => godot.AppendDotnetSection(project.BaseName)));
            return;
        }

        var result = CsprojPatcher.Patch(godotCsproj, hostFolders);
        warnings.AddRange(result.Warnings);
        if (result.NewContent is { } newContent)
            plan.Add(new PlannedAction(
                $"patch {Path.GetFileName(godotCsproj)} ({string.Join("; ", result.Added)})",
                () => File.WriteAllText(godotCsproj, newContent)));
    }

    /// <summary>
    /// Rewrites the template csproj's DefaultItemExcludes to the host folders
    /// this project actually gets (the template lists the three default ones).
    /// </summary>
    internal static string SetHostExcludes(string csproj, IReadOnlyList<string> hostFolders)
    {
        var value = "$(DefaultItemExcludes)" + string.Concat(hostFolders.Select(f => $";{f}/**"));
        return Regex.Replace(csproj, "<DefaultItemExcludes>.*?</DefaultItemExcludes>",
            $"<DefaultItemExcludes>{value}</DefaultItemExcludes>", RegexOptions.Singleline);
    }

    private static void PlanRootGlobalJson(
        List<PlannedAction> plan, List<string> warnings, string projectDir, bool wantsWeb, bool converting)
    {
        if (!wantsWeb) return;

        // global.json applies at or below its own directory, so a pin at the
        // project root is what lets the web host publish from there (the pin
        // inside the .web folder only covers dotnet runs started inside it).
        // An existing global.json is the user's own SDK policy - never touch
        // it, not even with --force.
        var path = Path.Combine(projectDir, "global.json");
        if (File.Exists(path))
        {
            if (converting)
                warnings.Add("global.json already exists - left untouched. Publishing the web host from the " +
                             "project root needs it to pin a .NET 10 SDK with the wasm-tools workload " +
                             "(publishing from inside the web host folder works regardless: its own global.json wins there).");
            return;
        }

        plan.Add(new PlannedAction("create global.json (pins a wasm-capable SDK for the whole project)",
            () => File.WriteAllText(path, TemplateAssets.RootGlobalJson())));
    }

    private static void PlanRootBuildTargets(
        List<PlannedAction> plan, List<string> warnings, string projectDir, bool converting)
    {
        // Directory.Build.targets is user-owned configuration when it already
        // exists, so scaffolding only creates the template's cleanup target for
        // projects that do not have one yet.
        var path = Path.Combine(projectDir, "Directory.Build.targets");
        if (File.Exists(path))
        {
            if (converting)
                warnings.Add("Directory.Build.targets already exists - left untouched. Add the TwoDogDeepClean target manually if you want clean to remove all configuration outputs.");
            return;
        }

        plan.Add(new PlannedAction("create Directory.Build.targets (shared clean target)",
            () => File.WriteAllText(path, TemplateAssets.RootBuildTargets())));
    }

    private static void PlanWebBoot(
        List<PlannedAction> plan, List<string> skipped, ScaffoldOptions options, string projectDir, bool converting)
    {
        // Written even without a web host: it is #if LIBGODOT_ENABLED-guarded
        // and matches the template content, so adding a web host later just
        // works.
        var path = Path.Combine(projectDir, "TwoDogWebBoot.cs");
        if (File.Exists(path) && !options.Force)
        {
            if (converting) skipped.Add("TwoDogWebBoot.cs");
            return;
        }

        plan.Add(new PlannedAction("create TwoDogWebBoot.cs (web bootstrap)",
            () => File.WriteAllText(path, TemplateAssets.WebBootSource())));
    }

    private static void PlanExportPresets(List<PlannedAction> plan, string projectDir, bool wantsWeb)
    {
        // The engine refuses `--export-pack` (which the web host's publish
        // runs) without an export_presets.cfg at the project root.
        var path = Path.Combine(projectDir, ExportPresetOps.FileName);
        if (!File.Exists(path))
        {
            // Even without a web host, matching dotnet-new output: the template
            // always ships the Web preset, so adding a web host later just
            // works.
            plan.Add(new PlannedAction($"create {ExportPresetOps.FileName} ('{ExportPresetOps.WebPresetName}' export preset)",
                () => File.WriteAllText(path, TemplateAssets.ExportPresets())));
            return;
        }

        if (!wantsWeb) return;
        var text = File.ReadAllText(path);
        if (ExportPresetOps.HasPreset(text, ExportPresetOps.WebPresetName)) return;
        plan.Add(new PlannedAction($"append '{ExportPresetOps.WebPresetName}' export preset to {ExportPresetOps.FileName}",
            () => File.AppendAllText(path, ExportPresetOps.AppendText(text))));
    }

    private static void PlanHosts(
        List<PlannedAction> plan, List<string> skipped, ScaffoldOptions options,
        string projectDir, string baseName, IReadOnlyList<HostSpec> hosts)
    {
        foreach (var host in hosts)
        foreach (var (relativePath, content) in TemplateAssets.HostFiles(host.Kind, baseName, host.Folder))
        {
            var target = Path.Combine(projectDir, relativePath);
            if (File.Exists(target) && !options.Force)
            {
                skipped.Add(relativePath);
                continue;
            }

            plan.Add(new PlannedAction($"create {relativePath}", () =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, content);
            }));
        }
    }

    private static void PlanSolution(
        List<PlannedAction> plan, ScaffoldOptions options, string projectDir, string baseName,
        string godotCsproj, IReadOnlyList<string> allHostFolders, IReadOnlyList<HostSpec> newHosts)
    {
        var (solutionPath, exists) = SolutionOps.Locate(projectDir, baseName);
        if (!exists)
        {
            // `Locate` uses the historic .sln extension for its hypothetical
            // path. New 2dog solutions are .slnx.
            solutionPath = Path.ChangeExtension(solutionPath, ".slnx");
        }
        else if (solutionPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            var classicSolutionPath = solutionPath;
            solutionPath = Path.ChangeExtension(classicSolutionPath, ".slnx");
            plan.Add(new PlannedAction($"migrate {Path.GetFileName(classicSolutionPath)} to {Path.GetFileName(solutionPath)}",
                () => SolutionOps.MigrateToSlnx(classicSolutionPath)));
        }
        var solutionName = Path.GetFileName(solutionPath);

        if (!exists)
            plan.Add(new PlannedAction($"create {solutionName}",
                () => SolutionOps.CreateSolution(solutionPath)));

        var allProjects = new List<string> { godotCsproj };
        allProjects.AddRange(allHostFolders.Select(f => Path.Combine(projectDir, f, f + ".csproj")));
        var missing = allProjects
            .Where(p => !exists || !SolutionOps.ContainsProject(solutionPath, Path.GetFileName(p)))
            .ToList();
        if (missing.Count > 0)
            plan.Add(new PlannedAction(
                $"add {missing.Count} project(s) to {solutionName}",
                () => SolutionOps.AddProjects(solutionPath, missing)));

        foreach (var web in newHosts.Where(h => h.Kind == HostKind.Web))
        {
            // Separator-agnostic: SolutionOps matches either / or \\ in the solution.
            var webRelative = $"{web.Folder}/{web.Folder}.csproj";
            plan.Add(new PlannedAction(
                $"exclude {web.Folder} from plain solution builds (needs wasm-tools; built via dotnet publish)",
                () =>
                {
                    if (!SolutionOps.ExcludeFromSolutionBuild(solutionPath, webRelative))
                        Console.WriteLine($"note: could not adjust {solutionName} build configs for {web.Folder}; " +
                                          "solution-wide builds will include it (requires the wasm-tools workload).");
                }));
        }

        // Only restore when the run actually changes something.
        if (options.Restore && plan.Count > 0)
            plan.Add(new PlannedAction($"dotnet restore {solutionName}", () =>
            {
                SolutionOps.Restore(solutionPath, out var succeeded);
                if (!succeeded)
                    Console.WriteLine("warning: dotnet restore failed - if the web host is the culprit, install " +
                                      "the wasm-tools workload (dotnet workload install wasm-tools) and restore again.");
            }));
    }

    private static void PrintNextSteps(ProjectContext project, IReadOnlyList<HostSpec> hosts)
    {
        Console.WriteLine("\nDone. Next steps:");

        // Relative-path comparison, not string equality on the full paths: a
        // trailing separator or a case-insensitive filesystem would otherwise
        // suggest `cd` into the directory the user is already in.
        var relative = Path.GetRelativePath(".", project.Dir);
        if (relative is not ("." or ""))
            Console.WriteLine($"  cd {relative}");

        foreach (var host in hosts)
            Console.WriteLine(host.Kind switch
            {
                HostKind.Desktop => $"  dotnet run --project {host.Folder}".PadRight(42) + "# desktop host",
                HostKind.Tests => $"  dotnet test {host.Folder}".PadRight(42) + "# xUnit tests (headless Godot)",
                HostKind.Web => $"  dotnet publish {host.Folder}".PadRight(42) + "# browser bundle (needs wasm-tools workload)",
                _ => $"  {host.Folder}",
            });

        Console.WriteLine("\nDocs: https://2dog.dev");
    }
}
