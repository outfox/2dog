using System.Text.RegularExpressions;

namespace twodog.cli;

/// <summary>
/// Solution handling: reuse the single solution at the Godot project root (Godot errors when more than one solution
/// at res:// contains the game project, so never add a second), or create one.
/// </summary>
internal static class SolutionOps
{
    /// <summary>
    /// The solution to use: an existing root sln/slnx (preferring one referencing the Godot csproj), or the path
    /// a new one should be created at. Throws on ambiguity.
    /// </summary>
    public static (string Path, bool Exists) Locate(string projectDir, string baseName)
    {
        // The directory itself may still be part of the plan (`2dog new`).
        var solutions = Directory.Exists(projectDir)
            ? Directory.EnumerateFiles(projectDir, "*.sln").Concat(Directory.EnumerateFiles(projectDir, "*.slnx")).ToList()
            : [];

        switch (solutions.Count)
        {
            case 0:
                return (Path.Combine(projectDir, baseName + ".sln"), false);
            case 1:
                return (solutions[0], true);
        }

        // Same disambiguation Godot applies: the sln containing the project.
        var containing = solutions
            .Where(s => File.ReadAllText(s).Contains($"{baseName}.csproj", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (containing.Count == 1) return (containing[0], true);

        throw new ToolException(
            $"Multiple solutions found in {projectDir} ({string.Join(", ", solutions.Select(Path.GetFileName))}); " +
            "the Godot editor requires exactly one solution containing the game project at the project root. " +
            "Remove the extras and re-run.");
    }

    public static void CreateSolution(string solutionPath)
    {
        if (!solutionPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("New 2dog solutions must use the .slnx format.", nameof(solutionPath));

        // Editor is 2dog's own build type (editor-variant natives); declaring it makes `dotnet build -c Editor`
        // work across the solution, matching `dotnet new 2dog` output.
        File.WriteAllText(solutionPath,
            """
            <Solution>
              <Configurations>
                <BuildType Name="Debug" />
                <BuildType Name="Editor" />
                <BuildType Name="Release" />
              </Configurations>
            </Solution>
            """ + Environment.NewLine);
    }

    /// <summary>Converts a classic solution to .slnx, then removes the old file.</summary>
    public static void MigrateToSlnx(string classicSolutionPath)
    {
        if (!classicSolutionPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only classic .sln files can be migrated.", nameof(classicSolutionPath));

        var slnxPath = Path.ChangeExtension(classicSolutionPath, ".slnx");
        if (File.Exists(slnxPath))
            throw new ToolException($"Cannot migrate {Path.GetFileName(classicSolutionPath)} because {Path.GetFileName(slnxPath)} already exists.");

        Run(Path.GetDirectoryName(classicSolutionPath)!, $"migrating {Path.GetFileName(classicSolutionPath)}",
            "sln", classicSolutionPath, "migrate");
        if (!File.Exists(slnxPath))
            throw new ToolException($"Migration did not create {Path.GetFileName(slnxPath)}.");

        File.Delete(classicSolutionPath);
    }

    public static void AddProjects(string solutionPath, IEnumerable<string> projectPaths)
    {
        // `dotnet sln add` is idempotent (already-present projects are reported
        // and skipped) and handles both .sln and .slnx.
        var args = new List<string> { "sln", solutionPath, "add" };
        args.AddRange(projectPaths);
        Run(Path.GetDirectoryName(solutionPath)!, $"updating {Path.GetFileName(solutionPath)}", args.ToArray());
    }

    /// <summary>Whether the solution already references the given project file name.</summary>
    public static bool ContainsProject(string solutionPath, string projectFileName) =>
        File.Exists(solutionPath) &&
        File.ReadAllText(solutionPath).Contains(projectFileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Points a solution's root game project entries at a renamed csproj (--rename fix). Textual on purpose: the
    /// game csproj sits at the solution root, so its reference is just the file name. Returns whether rewritten.
    /// </summary>
    public static bool RenameProject(string solutionPath, string oldBaseName, string newBaseName)
    {
        if (!File.Exists(solutionPath)) return false;

        var text = File.ReadAllText(solutionPath);
        var updated = text.Replace($"\"{oldBaseName}.csproj\"", $"\"{newBaseName}.csproj\"",
            StringComparison.OrdinalIgnoreCase);
        // Classic .sln also names the project: Project("{guid}") = "Old Name", "Old Name.csproj", ...
        if (solutionPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            updated = Regex.Replace(updated, $"= \"{Regex.Escape(oldBaseName)}\",", $"= \"{newBaseName}\",");

        if (updated == text) return false;
        File.WriteAllText(solutionPath, updated);
        return true;
    }

    /// <summary>Whether a classic .sln still has ".Build.0" entries for the project.</summary>
    public static bool HasSolutionBuildEntries(string solutionPath, string projectRelativePath) =>
        solutionPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) &&
        File.Exists(solutionPath) &&
        FindBuildLines(File.ReadAllText(solutionPath), projectRelativePath, out _) > 0;

    /// <summary>
    /// Excludes a host from plain solution builds in a classic .sln by removing its ".Build.0" lines (ActiveCfg
    /// stays): browser-wasm needs the wasm-tools workload and WinUI only builds on Windows.
    /// </summary>
    public static bool ExcludeFromSolutionBuild(string solutionPath, string projectRelativePath,
        bool mapEditorToDebug = true)
    {
        if (solutionPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            return ExcludeSlnxProjectFromBuild(solutionPath, projectRelativePath, mapEditorToDebug);
        if (!solutionPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)) return false;
        var text = File.ReadAllText(solutionPath);
        if (FindBuildLines(text, projectRelativePath, out var updated) == 0) return false;
        File.WriteAllText(solutionPath, updated!);
        return true;
    }

    private static bool ExcludeSlnxProjectFromBuild(string solutionPath, string projectRelativePath,
        bool mapEditorToDebug)
    {
        if (!File.Exists(solutionPath)) return false;

        var text = File.ReadAllText(solutionPath);
        var path = SlnxPathPattern(projectRelativePath);
        var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        // The browser-wasm host has no Editor configuration, so map the solution's Editor build type onto Debug -
        // only when the solution actually declares that build type.
        var wantsMap = mapEditorToDebug && text.Contains("<BuildType Name=\"Editor\"", StringComparison.OrdinalIgnoreCase);

        // Already expanded by an earlier run (or by hand): nothing to do.
        if (IsExcludedSlnx(text, path)) return true;

        // Self-closing entry: expand it.
        var selfClosing = Regex.Match(text, $"(?m)^(?<indent>\\s*)<Project Path=\"{path}\"\\s*/>", RegexOptions.IgnoreCase);
        if (selfClosing.Success)
        {
            var indent = selfClosing.Groups["indent"].Value;
            var actual = Regex.Match(selfClosing.Value, "Path=\"(?<p>[^\"]+)\"").Groups["p"].Value;
            var editorMap = wantsMap ? $"{indent}  <BuildType Solution=\"Editor|*\" Project=\"Debug\" />{newLine}" : "";
            var replacement = $"{indent}<Project Path=\"{actual}\">{newLine}" + editorMap +
                              $"{indent}  <Build Project=\"false\" />{newLine}" +
                              $"{indent}</Project>";
            File.WriteAllText(solutionPath, text[..selfClosing.Index] + replacement + text[(selfClosing.Index + selfClosing.Length)..]);
            return true;
        }

        // Expanded entry without the exclusion (dotnet sln add writes the Editor mapping itself): insert it.
        var block = Regex.Match(text, $"(?m)^(?<indent>\\s*)<Project Path=\"{path}\"\\s*>(?<body>.*?)^(?<close>\\s*)</Project>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!block.Success) return false;
        var blockIndent = block.Groups["indent"].Value;
        var insert = "";
        if (wantsMap && !block.Groups["body"].Value.Contains("<BuildType", StringComparison.OrdinalIgnoreCase))
            insert += $"{blockIndent}  <BuildType Solution=\"Editor|*\" Project=\"Debug\" />{newLine}";
        insert += $"{blockIndent}  <Build Project=\"false\" />{newLine}";
        var at = block.Groups["close"].Index;
        File.WriteAllText(solutionPath, text[..at] + insert + text[at..]);
        return true;
    }

    /// <summary>Whether the solution already keeps the project out of plain builds (either format).</summary>
    public static bool IsExcludedFromSolutionBuild(string solutionPath, string projectRelativePath)
    {
        if (!File.Exists(solutionPath)) return false;
        if (!solutionPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            return !HasSolutionBuildEntries(solutionPath, projectRelativePath);
        return IsExcludedSlnx(File.ReadAllText(solutionPath), SlnxPathPattern(projectRelativePath));
    }

    // The body must stay inside this project's block: a lazy .*? would run on into the next block's exclusion.
    private static bool IsExcludedSlnx(string text, string pathPattern) =>
        Regex.IsMatch(text, $"<Project Path=\"{pathPattern}\"\\s*>(?:(?!</Project>).)*?<Build Project=\"false\"\\s*/>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

    /// <summary>The project path as a regex accepting either separator (dotnet sln writes both over time).</summary>
    private static string SlnxPathPattern(string projectRelativePath) =>
        string.Join("[\\\\/]", projectRelativePath.Split('/', '\\').Select(Regex.Escape));

    /// <summary>
    /// Counts the project's ".Build.0" lines in classic sln text; when found, yields the text with them removed.
    /// </summary>
    private static int FindBuildLines(string text, string projectRelativePath, out string? updated)
    {
        updated = null;

        // Project("{type-guid}") = "name", "rel\path.csproj", "{project-guid}"
        // Tolerate either separator: `dotnet sln add` has differed across SDK versions and platforms.
        if (!TryFindProjectGuid(text, projectRelativePath, out var guid)) return 0;
        var pattern = $@"^\s*\{{{Regex.Escape(guid)}\}}\.[^\r\n]*\.Build\.0\s*=[^\r\n]*\r?\n";
        var count = Regex.Matches(text, pattern, RegexOptions.Multiline | RegexOptions.IgnoreCase).Count;
        if (count > 0) updated = Regex.Replace(text, pattern, "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        return count;
    }

    private static bool TryFindProjectGuid(string text, string projectRelativePath, out string guid)
    {
        var requestedPath = projectRelativePath.Replace('/', '\\');
        var matches = Regex.Matches(text,
            @"Project\(""\{[^}]+\}""\)\s*=\s*""[^""]*"",\s*""(?<path>[^""]+)"",\s*""\{(?<guid>[^}]+)\}""",
            RegexOptions.IgnoreCase);
        foreach (Match match in matches)
        {
            var candidatePath = match.Groups["path"].Value.Replace('/', '\\');
            if (!candidatePath.EndsWith(requestedPath, StringComparison.OrdinalIgnoreCase)) continue;
            guid = match.Groups["guid"].Value;
            return true;
        }

        guid = string.Empty;
        return false;
    }

    /// <summary>Restores the solution; the caller decides what a failure means.</summary>
    public static ProcessResult Restore(string solutionPath) =>
        ProcessRunner.Default.Run(ProcessRunner.Dotnet(Path.GetDirectoryName(solutionPath)!,
            $"restoring {Path.GetFileName(solutionPath)}", TimeSpan.FromMinutes(10),
            "restore", Path.GetFileName(solutionPath)), Cancellation.Token);

    private static void Run(string workingDir, string label, params string[] args)
    {
        var result = ProcessRunner.Default.Run(
            ProcessRunner.Dotnet(workingDir, label, TimeSpan.FromMinutes(2), args), Cancellation.Token);
        if (result.Ok) return;
        ProcessRunner.ReportFailure(result);
        throw new ToolException($"'{result.CommandLine}' failed ({result.Outcome})");
    }
}
