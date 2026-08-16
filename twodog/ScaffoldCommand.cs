using System.Text.RegularExpressions;

namespace twodog.cli;

/// <summary>
/// The one scaffolding engine behind every verb: it makes a Godot project
/// directory hold the 2dog host projects it was asked for. `2dog new` creates
/// the Godot project first and then runs the same path; `2dog add` (alias
/// `convert`) runs it against an existing one.
///
/// The whole run is planned first as a list of actions, then either printed
/// (--dry-run) or applied - both paths walk the same plan. Hard invariant:
/// the tool only ever creates new files or edits *.csproj / project.godot /
/// *.sln in place, and has no VCS awareness. It never moves, renames or
/// deletes anything, with two announced exceptions the user opts into:
/// the .sln-to-.slnx migration, and the --rename fix for spaced names.
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
            if (name != options.NameOverride)
                Out.Note($"project name adjusted: '{options.NameOverride}' -> '{name}' " +
                         "(spaces would break .NET publish; only letters, digits, '.', '_' and '-' survive)");
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
        var existingHosts = HostScan.Find(projectDir);
        var rename = ResolveSpacedName(options, projectDir, godot, existingHosts);
        return new ProjectContext
        {
            Dir = projectDir,
            BaseName = rename?.NewName ?? DeriveBaseName(options, projectDir, godot),
            Godot = godot,
            ExistingHosts = existingHosts,
            ExistingFolders = Subdirectories(projectDir),
            Rename = rename,
        };
    }

    /// <summary>
    /// The project's .NET restore identity when it contains whitespace, else
    /// null: [dotnet] assembly_name is authoritative, otherwise a sole root
    /// csproj names the project. Whitespace there makes `dotnet publish` of a
    /// referencing host silently drop the game's transitive NuGet packages
    /// (dotnet/sdk parses the assets file's dependency strings up to the
    /// first whitespace), so scaffolding hosts against such a name is refused.
    /// </summary>
    internal static string? SpacedIdentity(string projectDir, GodotProjectFile godotProject)
    {
        var name = godotProject.Get("dotnet", "project/assembly_name");
        if (name == null)
        {
            var rootCsprojs = Directory.EnumerateFiles(projectDir, "*.csproj")
                .Select(Path.GetFileNameWithoutExtension)
                .Cast<string>()
                .ToList();
            if (rootCsprojs.Count == 1) name = rootCsprojs[0];
        }

        return name != null && name.Any(char.IsWhiteSpace) ? name : null;
    }

    /// <summary>
    /// Refuses to scaffold against a whitespace-containing .NET name, and
    /// resolves --rename into the operation that fixes it. The automated fix
    /// is only offered while no hosts exist yet: afterwards every host csproj
    /// carries the old name too, and the tool won't rewrite user code.
    /// </summary>
    internal static RenameOperation? ResolveSpacedName(
        ScaffoldOptions options, string projectDir, GodotProjectFile godotProject, List<ExistingHost> existingHosts)
    {
        var spaced = SpacedIdentity(projectDir, godotProject);
        if (spaced == null)
        {
            if (options.RenameTo != null)
                throw new ToolException("--rename is only for projects whose .NET name contains whitespace; " +
                                        "this project's name is fine. Use --name to override the base name.");
            return null;
        }

        var suggested = Hosts.SanitizeName(spaced);
        if (existingHosts.Count > 0)
        {
            var message = SpacedNameMessage(spaced, suggested, existingHosts);
            if (options.RenameTo != null)
                message = "--rename only works before any 2dog hosts exist - this project already has " +
                          $"{existingHosts.Count} host(s) whose csprojs carry the old name.\n" + message;
            throw new ToolException(message);
        }

        if (options.RenameTo is null)
            throw new SpacedNameException(SpacedNameMessage(spaced, suggested, existingHosts),
                spaced, suggested, canOfferRename: true);

        var newName = Hosts.SanitizeName(options.RenameTo);
        if (newName is null || newName != options.RenameTo.Trim())
            throw new ToolException($"--rename '{options.RenameTo}' is not a usable name - " +
                                    "only letters, digits, '.', '_' and '-'");
        if (options.NameOverride != null && options.NameOverride != newName)
            throw new ToolException($"--name '{options.NameOverride}' conflicts with --rename '{newName}' - " +
                                    "--rename already sets the project's name");
        if (File.Exists(Path.Combine(projectDir, newName + ".csproj")))
            throw new ToolException($"cannot rename to '{newName}': {newName}.csproj already exists");

        return new RenameOperation(spaced, newName,
            CsprojExists: File.Exists(Path.Combine(projectDir, spaced + ".csproj")));
    }

    /// <summary>The refusal message: why, the manual checklist, and the way out.</summary>
    private static string SpacedNameMessage(string spaced, string? suggested, List<ExistingHost> existingHosts)
    {
        suggested ??= "MyGame";
        var steps = new List<string>
        {
            "close the Godot editor",
            $"rename '{spaced}.csproj' to '{suggested}.csproj'",
            $"set [dotnet] project/assembly_name=\"{suggested}\" in project.godot",
            $"point any solution entry at {suggested}.csproj",
        };
        if (existingHosts.Count > 0)
            steps.Add($"in each host csproj, update the ProjectReference to ../{suggested}.csproj " +
                      "and any TrimmerRootAssembly/RootNamespace using the old name");
        steps.Add("re-run 2dog add");

        var message =
            $"project name '{spaced}' contains whitespace - .NET publish silently drops such a project's NuGet " +
            "packages from hosts that reference it (dotnet/sdk bug), so 2dog refuses to scaffold against it.\n" +
            "Fix the .NET identity by hand (Godot's display name may keep its spaces):\n" +
            string.Join("\n", steps.Select((s, i) => $"  {i + 1}. {s}"));
        if (existingHosts.Count == 0)
            message += $"\nOr let 2dog do it: 2dog add --rename {suggested}";
        return message;
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
        var wantsWeb = newHosts.Any(h => h.Kind is HostKind.Web or HostKind.WebXr)
                       || existingHosts.Any(h => h.Kind is HostKind.Web or HostKind.WebXr);

        // The web bootstrap lives in the (first) web host folder and compiles
        // into the game assembly via a Compile Include in the root csproj. A
        // root-level TwoDogWebBoot.cs is the older layout - it still works via
        // the root csproj's default globs, so it is left alone (the tool never
        // moves or deletes) and no folder copy is planned next to it.
        var legacyRootBoot = File.Exists(Path.Combine(projectDir, "TwoDogWebBoot.cs"));
        // Every existing web-like host wins over new ones: a project that
        // already boots from its webxr folder keeps that single active copy
        // (and --force updates it) instead of gaining a second, dead one.
        var webBootFolder = legacyRootBoot
            ? null
            : existingHosts.FirstOrDefault(h => h.Kind == HostKind.Web)?.Folder
              ?? existingHosts.FirstOrDefault(h => h.Kind == HostKind.WebXr)?.Folder
              ?? newHosts.FirstOrDefault(h => h.Kind == HostKind.Web)?.Folder
              ?? newHosts.FirstOrDefault(h => h.Kind == HostKind.WebXr)?.Folder;
        if (legacyRootBoot && wantsWeb)
            warnings.Add("TwoDogWebBoot.cs sits at the project root (older layout) - left untouched, it still " +
                         "works there. Newer layouts keep it in the web host folder with a guarded Compile " +
                         "Include in the root csproj, so the Godot editor stops importing it as a script.");

        // Root files that already exist are worth a word when 2dog is first
        // added to a project ("your file was left alone"), but not when hosts
        // are added to one 2dog already set up - there they are simply ours.
        var retrofitting = !project.IsNew && existingHosts.Count == 0;

        // The rename runs first: the riskiest step (a file move the Godot
        // editor may block) fails before anything else is touched, and every
        // later action already sees the new name.
        string? renamedFrom = null;
        if (project.Rename is { } rename)
        {
            PlanRename(plan, project, rename, projectDir);
            if (rename.CsprojExists) renamedFrom = Path.Combine(projectDir, rename.OldName + ".csproj");
        }

        PlanGodotCsproj(plan, warnings, project, godotCsproj, allHostFolders, webBootFolder, renamedFrom);
        PlanRootBuildTargets(plan, warnings, projectDir, retrofitting);
        PlanRootGlobalJson(plan, warnings, projectDir, wantsWeb, retrofitting);
        PlanWebBoot(plan, skipped, options, projectDir, webBootFolder, retrofitting);
        PlanExportPresets(plan, projectDir, wantsWeb);
        PlanHosts(plan, skipped, options, projectDir, baseName, newHosts);
        PlanSolution(plan, options, projectDir, baseName, godotCsproj, allHostFolders, newHosts);

        foreach (var warning in warnings)
            Out.Warning(warning);
        foreach (var skip in skipped)
            Out.Skip($"{skip} (exists; use --force to overwrite)");

        if (plan.Count == 0)
        {
            Out.Line("[green]Nothing to do[/] - the project already has everything that was asked for.");
            return 0;
        }

        if (options.DryRun)
        {
            foreach (var action in plan)
                Out.Would(action.Description);
            Out.Blank();
            Out.Line($"Dry run: [bold]{plan.Count}[/] action(s) planned, nothing changed.");
            return 0;
        }

        if (confirm != null && !confirm(plan.Select(a => a.Description).ToList()))
        {
            Out.Line("[yellow]Cancelled[/] - nothing changed.");
            return 0;
        }

        foreach (var action in plan)
        {
            Out.Action(action.Description);
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
        string godotCsproj, List<string> hostFolders, string? webBootFolder, string? renamedFrom = null)
    {
        // With a rename planned, the user's csproj still sits at the old path
        // at plan time; the patch below must read it from there (and keeps
        // writing to the new path, where the earlier rename action put it).
        var readPath = renamedFrom ?? godotCsproj;
        if (!File.Exists(readPath))
        {
            // GDScript-only project (or a brand-new one): scaffold the csproj
            // and declare the assembly so the Godot editor finds it
            // (res://<name>.csproj).
            var content = SetWebBootInclude(
                SetHostExcludes(TemplateAssets.GodotCsproj(project.BaseName), hostFolders),
                project.BaseName, webBootFolder);
            plan.Add(new PlannedAction(
                $"create {Path.GetFileName(godotCsproj)} (Godot.NET.Sdk/{ToolVersions.GodotSdkVersion})",
                () => File.WriteAllText(godotCsproj, content)));

            // A new project's project.godot already declares [dotnet]; a
            // planned rename owns the assembly_name write itself.
            if (project.Godot is { } godot && !godot.HasSection("dotnet") && project.Rename is null)
                plan.Add(new PlannedAction(
                    $"append [dotnet] assembly_name=\"{project.BaseName}\" to project.godot",
                    () => godot.AppendDotnetSection(project.BaseName)));
            return;
        }

        var result = CsprojPatcher.Patch(readPath, hostFolders,
            webBootFolder is null ? null : $"{webBootFolder}/TwoDogWebBoot.cs");
        warnings.AddRange(result.Warnings);
        if (result.NewContent is { } newContent)
            plan.Add(new PlannedAction(
                $"patch {Path.GetFileName(godotCsproj)} ({string.Join("; ", result.Added)})",
                () => File.WriteAllText(godotCsproj, newContent)));
    }

    /// <summary>
    /// The spaced-name fix (--rename): move the csproj, set assembly_name,
    /// repoint the solution. Sequential announced actions with no rollback -
    /// same stance as the sln-to-slnx migration; a failure names its step and
    /// earlier steps stand.
    /// </summary>
    private static void PlanRename(
        List<PlannedAction> plan, ProjectContext project, RenameOperation rename, string projectDir)
    {
        var oldCsproj = Path.Combine(projectDir, rename.OldName + ".csproj");
        var newCsproj = Path.Combine(projectDir, rename.NewName + ".csproj");

        if (rename.CsprojExists)
            plan.Add(new PlannedAction($"rename {rename.OldName}.csproj to {rename.NewName}.csproj", () =>
            {
                try
                {
                    File.Move(oldCsproj, newCsproj);
                }
                catch (IOException ex)
                {
                    throw new ToolException($"could not rename {rename.OldName}.csproj: {ex.Message} - " +
                                            "close the Godot editor and any IDE holding the project, then re-run.");
                }
            }));

        plan.Add(new PlannedAction($"set [dotnet] assembly_name=\"{rename.NewName}\" in project.godot",
            () => project.Godot!.SetAssemblyName(rename.NewName)));

        foreach (var solution in Directory.EnumerateFiles(projectDir, "*.sln")
                     .Concat(Directory.EnumerateFiles(projectDir, "*.slnx"))
                     .Where(s => SolutionOps.ContainsProject(s, rename.OldName + ".csproj")))
            plan.Add(new PlannedAction(
                $"point {Path.GetFileName(solution)} at {rename.NewName}.csproj",
                () =>
                {
                    if (!SolutionOps.RenameProject(solution, rename.OldName, rename.NewName))
                        Out.Note($"{Path.GetFileName(solution)} no longer references " +
                                 $"{rename.OldName}.csproj - nothing to update.");
                }));
    }

    /// <summary>
    /// Rewrites the template csproj's DefaultItemExcludes to the host folders
    /// this project actually gets (the template lists every host folder).
    /// </summary>
    internal static string SetHostExcludes(string csproj, IReadOnlyList<string> hostFolders)
    {
        var value = "$(DefaultItemExcludes)" + string.Concat(hostFolders.Select(f => $";{f}/**"));
        return Regex.Replace(csproj, "<DefaultItemExcludes>.*?</DefaultItemExcludes>",
            $"<DefaultItemExcludes>{value}</DefaultItemExcludes>", RegexOptions.Singleline);
    }

    /// <summary>
    /// Rewrites the template csproj's guarded TwoDogWebBoot.cs Compile Include
    /// (the template names &lt;Base&gt;.web) to the web host folder this project
    /// actually gets. With no web host the template path is kept: the Exists
    /// condition makes it inert, and dropping the file in later just works.
    /// </summary>
    internal static string SetWebBootInclude(string csproj, string baseName, string? webFolder) =>
        webFolder is null
            ? csproj
            : csproj.Replace($"{baseName}.web/TwoDogWebBoot.cs", $"{webFolder}/TwoDogWebBoot.cs");

    private static void PlanRootGlobalJson(
        List<PlannedAction> plan, List<string> warnings, string projectDir, bool wantsWeb, bool retrofitting)
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
            if (retrofitting)
                warnings.Add("global.json already exists - left untouched. Publishing the web host from the " +
                             "project root needs it to pin a .NET 10 SDK with the wasm-tools workload " +
                             "(publishing from inside the web host folder works regardless: its own global.json wins there).");
            return;
        }

        plan.Add(new PlannedAction("create global.json (pins a wasm-capable SDK for the whole project)",
            () => File.WriteAllText(path, TemplateAssets.RootGlobalJson())));
    }

    private static void PlanRootBuildTargets(
        List<PlannedAction> plan, List<string> warnings, string projectDir, bool retrofitting)
    {
        // Directory.Build.targets is user-owned configuration when it already
        // exists, so scaffolding only creates the template's cleanup target for
        // projects that do not have one yet.
        var path = Path.Combine(projectDir, "Directory.Build.targets");
        if (File.Exists(path))
        {
            if (retrofitting)
                warnings.Add("Directory.Build.targets already exists - left untouched. Add the TwoDogDeepClean target manually if you want clean to remove all configuration outputs.");
            return;
        }

        plan.Add(new PlannedAction("create Directory.Build.targets (shared clean target)",
            () => File.WriteAllText(path, TemplateAssets.RootBuildTargets())));
    }

    private static void PlanWebBoot(
        List<PlannedAction> plan, List<string> skipped, ScaffoldOptions options, string projectDir,
        string? webBootFolder, bool retrofitting)
    {
        // null: no web host to boot, or a legacy root-level file the root
        // csproj's default globs already compile (left alone).
        if (webBootFolder is null) return;

        var relative = $"{webBootFolder}/TwoDogWebBoot.cs";
        var path = Path.Combine(projectDir, webBootFolder, "TwoDogWebBoot.cs");
        if (File.Exists(path) && !options.Force)
        {
            if (retrofitting) skipped.Add(relative);
            return;
        }

        // The web host folder may be created later in this same plan.
        plan.Add(new PlannedAction($"create {relative} (web bootstrap)", () =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, TemplateAssets.WebBootSource());
        }));
    }

    private static void PlanExportPresets(List<PlannedAction> plan, string projectDir, bool wantsWeb)
    {
        // The engine refuses `--export-pack` without an export_presets.cfg at
        // the project root; the web host's publish exports through the 'Web'
        // preset and desktop publishes through the per-OS desktop presets.
        var path = Path.Combine(projectDir, ExportPresetOps.FileName);
        if (!File.Exists(path))
        {
            // Even without a web host, matching dotnet-new output: the template
            // always ships all presets, so adding a host later just works.
            plan.Add(new PlannedAction($"create {ExportPresetOps.FileName} (web + desktop export presets)",
                () => File.WriteAllText(path, TemplateAssets.ExportPresets())));
            return;
        }

        // Desktop presets are needed regardless of the host mix: every layout
        // has a desktop host, and its publish exports the pck through them.
        var text = File.ReadAllText(path);
        var missing = ExportPresetOps.DesktopPresetNames
            .Where(name => !ExportPresetOps.HasPreset(text, name));
        if (wantsWeb && !ExportPresetOps.HasPreset(text, ExportPresetOps.WebPresetName))
            missing = missing.Prepend(ExportPresetOps.WebPresetName);

        foreach (var preset in missing)
        {
            // Re-read inside the action: earlier appends in the same plan must
            // be visible so each preset gets the next free index.
            plan.Add(new PlannedAction($"append '{preset}' export preset to {ExportPresetOps.FileName}",
                () => File.AppendAllText(path, ExportPresetOps.AppendText(File.ReadAllText(path), preset))));
        }
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
                File.WriteAllBytes(target, content);
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

        foreach (var host in newHosts.Where(h => h.Kind is HostKind.Web or HostKind.WebXr or HostKind.WinUi))
        {
            // Separator-agnostic: SolutionOps matches either / or \\ in the solution.
            var relative = $"{host.Folder}/{host.Folder}.csproj";
            var (why, note) = host.Kind is HostKind.WinUi
                ? ("only builds on Windows; built via dotnet run", "fails to build on non-Windows systems")
                : ("needs wasm-tools; built via dotnet publish", "requires the wasm-tools workload");
            plan.Add(new PlannedAction(
                $"exclude {host.Folder} from plain solution builds ({why})",
                () =>
                {
                    // The wasm hosts have no Editor configuration; the WinUI host does.
                    if (!SolutionOps.ExcludeFromSolutionBuild(solutionPath, relative,
                            mapEditorToDebug: host.Kind is not HostKind.WinUi))
                        Out.Note($"could not adjust {solutionName} build configs for {host.Folder}; " +
                                 $"solution-wide builds will include it ({note}).");
                }));
        }

        // Only restore when the run actually changes something.
        if (options.Restore && plan.Count > 0)
            plan.Add(new PlannedAction($"dotnet restore {solutionName}", () =>
            {
                SolutionOps.Restore(solutionPath, out var succeeded);
                if (!succeeded)
                    Out.Warning("dotnet restore failed - if the web host is the culprit, install " +
                                "the wasm-tools workload (dotnet workload install wasm-tools) and restore again.");
            }));
    }

    private static void PrintNextSteps(ProjectContext project, IReadOnlyList<HostSpec> hosts)
    {
        // Relative-path comparison, not string equality on the full paths: a
        // trailing separator or a case-insensitive filesystem would otherwise
        // suggest `cd` into the directory the user is already in.
        var relative = Path.GetRelativePath(".", project.Dir);
        var cd = relative is "." or "" ? null : $"cd {QuoteIfNeeded(relative)}";

        var rows = hosts.Select(host => host.Kind switch
        {
            HostKind.Desktop => ($"dotnet run --project {host.Folder}", "desktop host"),
            HostKind.Tests => ($"dotnet test {host.Folder}", "xUnit tests (headless Godot)"),
            HostKind.Web => ($"dotnet publish {host.Folder}", "browser bundle (needs wasm-tools workload)"),
            HostKind.WebXr => ($"dotnet publish {host.Folder}", "WebXR browser bundle (needs wasm-tools workload)"),
            HostKind.WinForms => ($"dotnet run --project {host.Folder}", "WinForms host (Windows only)"),
            HostKind.WinUi => ($"dotnet run --project {host.Folder}", "WinUI 3 host (Windows only)"),
            HostKind.Avalonia => ($"dotnet run --project {host.Folder}", "Avalonia host (cross-platform GUI)"),
            _ => (host.Folder, ""),
        }).ToList();

        Out.NextSteps(cd, rows);
    }

    private static string QuoteIfNeeded(string path) => path.Contains(' ') ? $"\"{path}\"" : path;
}
