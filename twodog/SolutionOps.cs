using System.Diagnostics;
using System.Text.RegularExpressions;

namespace twodog.cli;

/// <summary>
/// Solution handling: reuse the single solution at the Godot project root
/// (Godot itself errors when more than one solution at res:// contains the
/// game project, so the tool must never add a second), or create one.
/// </summary>
internal static class SolutionOps
{
    /// <summary>
    /// The solution to use: an existing root sln/slnx (preferring one that
    /// references the Godot csproj), or the path a new one should be created
    /// at. Throws on ambiguity.
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

        // The Editor build type is 2dog's own configuration (editor-variant
        // natives); declaring it here is what makes `dotnet build -c Editor`
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

        Run(Path.GetDirectoryName(classicSolutionPath)!, "sln", classicSolutionPath, "migrate");
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
        Run(Path.GetDirectoryName(solutionPath)!, args.ToArray());
    }

    /// <summary>Whether the solution already references the given project file name.</summary>
    public static bool ContainsProject(string solutionPath, string projectFileName) =>
        File.Exists(solutionPath) &&
        File.ReadAllText(solutionPath).Contains(projectFileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a classic .sln still has ".Build.0" entries for the project.</summary>
    public static bool HasSolutionBuildEntries(string solutionPath, string projectRelativePath) =>
        solutionPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) &&
        File.Exists(solutionPath) &&
        FindBuildLines(File.ReadAllText(solutionPath), projectRelativePath, out _) > 0;

    /// <summary>
    /// Excludes a host from plain solution builds in a classic .sln by
    /// removing its ".Build.0" lines (ActiveCfg stays): building the
    /// browser-wasm hosts requires the wasm-tools workload and the WinUI host
    /// only builds on Windows, so they are built explicitly instead of by
    /// "Build Solution".
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
        var path = projectRelativePath.Replace('\\', '/');
        var escapedPath = Regex.Escape(path);

        // Already expanded by an earlier run (or by hand): nothing to do.
        if (Regex.IsMatch(text, $"<Project Path=\"{escapedPath}\"\\s*>.*?<Build Project=\"false\"\\s*/>.*?</Project>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            return true;

        var pattern = $"(?m)^(?<indent>\\s*)<Project Path=\"{escapedPath}\"\\s*/>";
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        if (!match.Success) return false;

        var indent = match.Groups["indent"].Value;
        var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        // The browser-wasm host has no Editor configuration of its own, so map
        // the solution's Editor build type onto its Debug one - but only when
        // the solution actually declares that build type.
        var editorMap = mapEditorToDebug
                        && text.Contains("<BuildType Name=\"Editor\"", StringComparison.OrdinalIgnoreCase)
            ? $"{indent}  <BuildType Solution=\"Editor|*\" Project=\"Debug\" />{newLine}"
            : "";
        var replacement = $"{indent}<Project Path=\"{path}\">{newLine}" +
                          editorMap +
                          $"{indent}  <Build Project=\"false\" />{newLine}" +
                          $"{indent}</Project>";
        File.WriteAllText(solutionPath, text[..match.Index] + replacement + text[(match.Index + match.Length)..]);
        return true;
    }

    /// <summary>
    /// Counts the project's ".Build.0" lines in classic sln text; when found,
    /// yields the text with those lines removed.
    /// </summary>
    private static int FindBuildLines(string text, string projectRelativePath, out string? updated)
    {
        updated = null;

        // Project("{type-guid}") = "name", "rel\path.csproj", "{project-guid}"
        // The classic sln format specifies backslash separators, but tolerate
        // either in the file: `dotnet sln add` implementations have differed
        // across SDK versions and platforms.
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

    public static void Restore(string solutionPath, out bool succeeded)
    {
        succeeded = TryRun(Path.GetDirectoryName(solutionPath)!, "restore", Path.GetFileName(solutionPath));
    }

    private static void Run(string workingDir, params string[] args)
    {
        if (!TryRun(workingDir, args))
            throw new ToolException($"'dotnet {string.Join(' ', args)}' failed (see output above)");
    }

    private static bool TryRun(string workingDir, params string[] args)
    {
        // On a real terminal, inherit the handles so the child dotnet keeps
        // its TTY features (progress, color). With redirected stdio (which is
        // how the test host runs), echo through Console instead, so hosts
        // that swap Console.Out/Error (the tests) can capture the output.
        var inherit = !Console.IsOutputRedirected && !Console.IsErrorRedirected;
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = !inherit,
            RedirectStandardError = !inherit,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi)!;
        if (!inherit)
        {
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) Console.Out.WriteLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Console.Error.WriteLine(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        process.WaitForExit();
        return process.ExitCode == 0;
    }
}
