namespace twodog.cli;

/// <summary>
/// `2dog convert`: converts an existing Godot project to 2dog in place.
///
/// The whole run is planned first as a list of actions, then either printed
/// (--dry-run) or applied - both paths walk the same plan. Hard invariant:
/// the tool only ever creates new files or edits *.csproj / project.godot /
/// *.sln in place; it never moves, renames or deletes anything, and has no
/// VCS awareness.
/// </summary>
internal static class ConvertCommand
{
    private sealed record PlannedAction(string Description, Action Apply);

    public static int Run(ConvertOptions options)
    {
        var projectDir = Path.GetFullPath(options.ProjectPath ?? ".");
        var projectGodot = Path.Combine(projectDir, "project.godot");
        if (!File.Exists(projectGodot))
            throw new ConvertException($"no project.godot in {projectDir} - point 2dog convert at a Godot project directory");

        var godotProject = new GodotProjectFile(projectGodot);
        var baseName = DeriveBaseName(options, projectDir, godotProject);
        var godotCsproj = Path.Combine(projectDir, baseName + ".csproj");

        var hostSuffixes = new List<string> { "2dog" };
        if (options.IncludeWeb) hostSuffixes.Add("web");
        if (options.IncludeTests) hostSuffixes.Add("tests");
        var hostFolders = hostSuffixes.Select(s => $"{baseName}.{s}").ToList();

        var plan = new List<PlannedAction>();
        var warnings = new List<string>();
        var skipped = new List<string>();

        PlanGodotCsproj(plan, warnings, options, godotProject, godotCsproj, baseName, hostFolders);
        PlanRootBuildTargets(plan, warnings, projectDir);
        PlanRootGlobalJson(plan, warnings, options, projectDir);
        PlanWebBoot(plan, skipped, options, projectDir);
        PlanExportPresets(plan, options, projectDir);
        PlanHosts(plan, skipped, options, projectDir, baseName, hostSuffixes);
        PlanSolution(plan, warnings, options, projectDir, baseName, godotCsproj, hostFolders);

        foreach (var warning in warnings)
            Console.WriteLine($"warning: {warning}");
        foreach (var skip in skipped)
            Console.WriteLine($"skip: {skip} (exists; use --force to overwrite)");

        if (plan.Count == 0)
        {
            Console.WriteLine("Nothing to do - project already converted.");
            return 0;
        }

        foreach (var action in plan)
        {
            Console.WriteLine((options.DryRun ? "would: " : "") + action.Description);
            if (!options.DryRun) action.Apply();
        }

        if (options.DryRun)
        {
            Console.WriteLine($"\nDry run: {plan.Count} action(s) planned, nothing changed.");
            return 0;
        }

        PrintNextSteps(baseName, options);
        return 0;
    }

    // internal for unit tests
    internal static string DeriveBaseName(ConvertOptions options, string projectDir, GodotProjectFile godotProject)
    {
        var rootCsprojs = Directory.EnumerateFiles(projectDir, "*.csproj")
            .Select(Path.GetFileNameWithoutExtension)
            .Cast<string>()
            .ToList();

        if (options.NameOverride is { } forced)
        {
            var name = Sanitize(forced) ?? throw new ConvertException($"--name '{forced}' contains no usable characters");
            if (godotProject.Get("dotnet", "project/assembly_name") is { } existing && existing != name)
                throw new ConvertException(
                    $"--name '{name}' conflicts with project.godot's assembly_name '{existing}'; " +
                    "the Godot editor requires the csproj to be named after the assembly name.");
            return name;
        }

        // 1. [dotnet] project/assembly_name is authoritative: Godot resolves
        //    res://<assembly_name>.csproj from it.
        if (godotProject.Get("dotnet", "project/assembly_name") is { } assemblyName)
        {
            if (rootCsprojs.Count > 0 && !rootCsprojs.Contains(assemblyName))
                throw new ConvertException(
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
                throw new ConvertException(
                    $"multiple csproj files at the project root ({string.Join(", ", rootCsprojs)}) and no " +
                    "[dotnet] assembly_name in project.godot to pick one; pass --name.");
        }

        // 3. Godot's display name, then the directory name.
        var fallback = Sanitize(godotProject.Get("application", "config/name"))
                       ?? Sanitize(Path.GetFileName(projectDir))
                       ?? throw new ConvertException("could not derive a project name; pass --name");
        return fallback;
    }

    /// <summary>Reduce a display name to a file/assembly stem (dotnet-new-style).</summary>
    private static string? Sanitize(string? name)
    {
        if (name == null) return null;
        var chars = name.Where(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-').ToArray();
        return chars.Length == 0 ? null : new string(chars);
    }

    private static void PlanGodotCsproj(
        List<PlannedAction> plan, List<string> warnings, ConvertOptions options,
        GodotProjectFile godotProject, string godotCsproj, string baseName, List<string> hostFolders)
    {
        if (!File.Exists(godotCsproj))
        {
            // GDScript-only project: scaffold the csproj and declare the
            // assembly so the Godot editor finds it (res://<name>.csproj).
            var content = PruneSkippedHostExcludes(TemplateAssets.GodotCsproj(baseName), baseName, options);
            plan.Add(new PlannedAction(
                $"create {Path.GetFileName(godotCsproj)} (Godot.NET.Sdk/{ToolVersions.GodotSdkVersion})",
                () => File.WriteAllText(godotCsproj, content)));

            if (!godotProject.HasSection("dotnet"))
                plan.Add(new PlannedAction(
                    $"append [dotnet] assembly_name=\"{baseName}\" to project.godot",
                    () => godotProject.AppendDotnetSection(baseName)));
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
    /// The template Godot csproj excludes all three host folders; drop the
    /// entries for hosts this run does not scaffold.
    /// </summary>
    private static string PruneSkippedHostExcludes(string csproj, string baseName, ConvertOptions options)
    {
        if (!options.IncludeWeb) csproj = csproj.Replace($";{baseName}.web/**", "");
        if (!options.IncludeTests) csproj = csproj.Replace($";{baseName}.tests/**", "");
        return csproj;
    }

    private static void PlanRootGlobalJson(List<PlannedAction> plan, List<string> warnings, ConvertOptions options, string projectDir)
    {
        if (!options.IncludeWeb) return;

        // global.json applies at or below its own directory, so a pin at the
        // project root is what lets the web host publish from there (the pin
        // inside the .web folder only covers dotnet runs started inside it).
        // An existing global.json is the user's own SDK policy - never touch
        // it, not even with --force.
        var path = Path.Combine(projectDir, "global.json");
        if (File.Exists(path))
        {
            warnings.Add("global.json already exists - left untouched. Publishing the web host from the " +
                         "project root needs it to pin a .NET 10 SDK with the wasm-tools workload " +
                         "(publishing from inside the web host folder works regardless: its own global.json wins there).");
            return;
        }

        plan.Add(new PlannedAction("create global.json (pins a wasm-capable SDK for the whole project)",
            () => File.WriteAllText(path, TemplateAssets.RootGlobalJson())));
    }

    private static void PlanRootBuildTargets(List<PlannedAction> plan, List<string> warnings, string projectDir)
    {
        // Directory.Build.targets is user-owned configuration when it already
        // exists, so conversion only creates the template's cleanup target for
        // projects that do not have one yet.
        var path = Path.Combine(projectDir, "Directory.Build.targets");
        if (File.Exists(path))
        {
            warnings.Add("Directory.Build.targets already exists - left untouched. Add the TwoDogDeepClean target manually if you want clean to remove all configuration outputs.");
            return;
        }

        plan.Add(new PlannedAction("create Directory.Build.targets (shared clean target)",
            () => File.WriteAllText(path, TemplateAssets.RootBuildTargets())));
    }

    private static void PlanWebBoot(List<PlannedAction> plan, List<string> skipped, ConvertOptions options, string projectDir)
    {
        // Written even with --no-web: it is #if LIBGODOT_ENABLED-guarded and
        // matches the template content, so adding a web host later just works.
        var path = Path.Combine(projectDir, "TwoDogWebBoot.cs");
        if (File.Exists(path) && !options.Force)
        {
            skipped.Add("TwoDogWebBoot.cs");
            return;
        }

        plan.Add(new PlannedAction("create TwoDogWebBoot.cs (web bootstrap)",
            () => File.WriteAllText(path, TemplateAssets.WebBootSource())));
    }

    private static void PlanExportPresets(List<PlannedAction> plan, ConvertOptions options, string projectDir)
    {
        // The engine refuses `--export-pack` (which the web host's publish
        // runs) without an export_presets.cfg at the project root.
        var path = Path.Combine(projectDir, ExportPresetOps.FileName);
        if (!File.Exists(path))
        {
            // Even with --no-web, matching dotnet-new output: the template
            // always ships the Web preset, so adding a web host later just
            // works.
            plan.Add(new PlannedAction($"create {ExportPresetOps.FileName} ('{ExportPresetOps.WebPresetName}' export preset)",
                () => File.WriteAllText(path, TemplateAssets.ExportPresets())));
            return;
        }

        if (!options.IncludeWeb) return;
        var text = File.ReadAllText(path);
        if (ExportPresetOps.HasPreset(text, ExportPresetOps.WebPresetName)) return;
        plan.Add(new PlannedAction($"append '{ExportPresetOps.WebPresetName}' export preset to {ExportPresetOps.FileName}",
            () => File.AppendAllText(path, ExportPresetOps.AppendText(text))));
    }

    private static void PlanHosts(
        List<PlannedAction> plan, List<string> skipped, ConvertOptions options,
        string projectDir, string baseName, List<string> hostSuffixes)
    {
        foreach (var suffix in hostSuffixes)
        foreach (var (relativePath, content) in TemplateAssets.HostFiles(suffix, baseName))
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
        List<PlannedAction> plan, List<string> warnings, ConvertOptions options,
        string projectDir, string baseName, string godotCsproj, List<string> hostFolders)
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
        allProjects.AddRange(hostFolders.Select(f => Path.Combine(projectDir, f, f + ".csproj")));
        var missing = allProjects
            .Where(p => !exists || !SolutionOps.ContainsProject(solutionPath, Path.GetFileName(p)))
            .ToList();
        if (missing.Count > 0)
            plan.Add(new PlannedAction(
                $"add {missing.Count} project(s) to {solutionName}",
                () => SolutionOps.AddProjects(solutionPath, missing)));

        if (options.IncludeWeb)
        {
            // Separator-agnostic: SolutionOps matches either / or \\ in the solution.
            var webRelative = $"{baseName}.web/{baseName}.web.csproj";
            var webIsNew = missing.Any(p => p.EndsWith($"{baseName}.web.csproj", StringComparison.OrdinalIgnoreCase));
            if (webIsNew || SolutionOps.HasSolutionBuildEntries(solutionPath, webRelative))
                plan.Add(new PlannedAction(
                    $"exclude {baseName}.web from plain solution builds (needs wasm-tools; built via dotnet publish)",
                    () =>
                    {
                        if (!SolutionOps.ExcludeFromSolutionBuild(solutionPath, webRelative))
                            Console.WriteLine($"note: could not adjust {solutionName} build configs for {baseName}.web; " +
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

    private static void PrintNextSteps(string baseName, ConvertOptions options)
    {
        Console.WriteLine(
            $"""

             Converted. Next steps:
               dotnet run --project {baseName}.2dog     # desktop host
             """);
        if (options.IncludeTests)
            Console.WriteLine($"  dotnet test {baseName}.tests             # xUnit tests (headless Godot)");
        if (options.IncludeWeb)
            Console.WriteLine($"  dotnet publish {baseName}.web            # browser bundle (needs wasm-tools workload)");
        Console.WriteLine("\nDocs: https://2dog.dev");
    }
}
