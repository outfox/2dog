using System.Diagnostics;
using System.Text.RegularExpressions;

namespace twodog.cli;

/// <summary>
/// Solution handling: reuse the single solution at the Godot project root
/// (Godot itself errors when more than one sln at res:// contains the game
/// project, so a converter must never add a second), or create one; project
/// membership goes through `dotnet sln` rather than hand-written sln text.
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
        var solutions = Directory.EnumerateFiles(projectDir, "*.sln")
            .Concat(Directory.EnumerateFiles(projectDir, "*.slnx"))
            .ToList();

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

        throw new ConvertException(
            $"Multiple solutions found in {projectDir} ({string.Join(", ", solutions.Select(Path.GetFileName))}); " +
            "the Godot editor requires exactly one solution containing the game project at the project root. " +
            "Remove the extras and re-run.");
    }

    public static void CreateSolution(string solutionPath)
    {
        // A classic-format skeleton rather than `dotnet new sln`: newer SDKs
        // default that template to .slnx, and the Godot editor and this tool's
        // build-config adjustment both expect the classic format. Seed every
        // supported configuration so `dotnet sln add` creates matching project
        // entries, including the editor variant.
        File.WriteAllText(solutionPath,
            """
            Microsoft Visual Studio Solution File, Format Version 12.00
            # Visual Studio Version 17
            VisualStudioVersion = 17.0.31903.59
            MinimumVisualStudioVersion = 10.0.40219.1
            Global
                GlobalSection(SolutionConfigurationPlatforms) = preSolution
                    Debug|Any CPU = Debug|Any CPU
                    Release|Any CPU = Release|Any CPU
                    Editor|Any CPU = Editor|Any CPU
                EndGlobalSection
            EndGlobal
            """ + Environment.NewLine);
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
    /// Excludes the web host from plain solution builds in a classic .sln by
    /// removing its ".Build.0" lines (ActiveCfg stays): building the
    /// browser-wasm host requires the wasm-tools workload, so it is built
    /// explicitly via `dotnet publish` instead of by "Build Solution".
    /// </summary>
    public static bool ExcludeFromSolutionBuild(string solutionPath, string projectRelativePath)
    {
        if (!solutionPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)) return false;
        var text = File.ReadAllText(solutionPath);
        if (FindBuildLines(text, projectRelativePath, out var updated) == 0) return false;
        File.WriteAllText(solutionPath, updated!);
        return true;
    }

    /// <summary>
    /// Whether a classic solution is missing the Editor configuration or one
    /// of the supplied project's Editor mappings. The web host deliberately
    /// maps Editor to Debug and is excluded from solution builds.
    /// </summary>
    public static bool NeedsEditorConfiguration(
        string solutionPath, IEnumerable<string> projectRelativePaths, string? webProjectRelativePath)
    {
        if (!solutionPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || !File.Exists(solutionPath))
            return false;

        var text = File.ReadAllText(solutionPath);
        if (!text.Contains("Editor|Any CPU = Editor|Any CPU", StringComparison.Ordinal))
            return true;

        var editorConfigurations = GetEditorSolutionConfigurations(text);
        foreach (var project in projectRelativePaths)
        {
            if (!TryFindProjectGuid(text, project, out var guid)) return true;
            var isWeb = string.Equals(project, webProjectRelativePath, StringComparison.OrdinalIgnoreCase);
            var activeConfig = isWeb ? "Debug" : "Editor";
            foreach (var solutionConfiguration in editorConfigurations)
            {
                if (!text.Contains($"{{{guid}}}.{solutionConfiguration}.ActiveCfg = {activeConfig}|Any CPU", StringComparison.Ordinal)) return true;
                if (!isWeb && !text.Contains($"{{{guid}}}.{solutionConfiguration}.Build.0 = Editor|Any CPU", StringComparison.Ordinal)) return true;
            }
        }

        return false;
    }

    /// <summary>Add the Editor configuration and mappings to a classic solution.</summary>
    public static void EnsureEditorConfiguration(
        string solutionPath, IEnumerable<string> projectRelativePaths, string? webProjectRelativePath)
    {
        if (!solutionPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || !File.Exists(solutionPath))
            return;

        var text = File.ReadAllText(solutionPath);
        const string editorConfiguration = "\t\tEditor|Any CPU = Editor|Any CPU";
        if (!text.Contains(editorConfiguration, StringComparison.Ordinal))
        {
            const string configurationMarker = "GlobalSection(SolutionConfigurationPlatforms) = preSolution";
            var configurationStart = text.IndexOf(configurationMarker, StringComparison.Ordinal);
            if (configurationStart < 0) return;
            var insertAt = text.IndexOf('\n', configurationStart) + 1;
            if (insertAt == 0) return;
            text = text.Insert(insertAt, editorConfiguration + Environment.NewLine);
        }

        var entries = new List<string>();
        var editorConfigurations = GetEditorSolutionConfigurations(text);
        foreach (var project in projectRelativePaths)
        {
            if (!TryFindProjectGuid(text, project, out var guid)) continue;
            var isWeb = string.Equals(project, webProjectRelativePath, StringComparison.OrdinalIgnoreCase);
            var activeConfig = isWeb ? "Debug" : "Editor";
            foreach (var solutionConfiguration in editorConfigurations)
            {
                text = SetEditorProjectConfiguration(
                    text, entries, guid, solutionConfiguration, "ActiveCfg", $"{activeConfig}|Any CPU");
                if (!isWeb)
                    text = SetEditorProjectConfiguration(
                        text, entries, guid, solutionConfiguration, "Build.0", "Editor|Any CPU");
            }
        }

        if (entries.Count > 0)
        {
            const string projectConfigurationMarker = "GlobalSection(ProjectConfigurationPlatforms) = postSolution";
            var projectConfigurationStart = text.IndexOf(projectConfigurationMarker, StringComparison.Ordinal);
            if (projectConfigurationStart < 0) return;
            var insertAt = text.IndexOf("EndGlobalSection", projectConfigurationStart, StringComparison.Ordinal);
            if (insertAt < 0) return;
            text = text.Insert(insertAt, string.Join(Environment.NewLine, entries) + Environment.NewLine);
        }

        File.WriteAllText(solutionPath, text);
    }

    private static string SetEditorProjectConfiguration(
        string text, ICollection<string> missingEntries, string guid, string solutionConfiguration, string entryName, string value)
    {
        var pattern = $@"(?m)^(?<prefix>\s*\{{{Regex.Escape(guid)}\}}\.{Regex.Escape(solutionConfiguration)}\.{Regex.Escape(entryName)}\s*=\s*)[^\r\n]*";
        if (Regex.IsMatch(text, pattern))
            return Regex.Replace(text, pattern, "${prefix}" + value);

        missingEntries.Add($"\t\t{{{guid}}}.{solutionConfiguration}.{entryName} = {value}");
        return text;
    }

    private static IReadOnlyList<string> GetEditorSolutionConfigurations(string text)
    {
        var configurations = new List<string> { "Editor|Any CPU" };
        if (text.Contains("Editor|x64 = Editor|x64", StringComparison.Ordinal)) configurations.Add("Editor|x64");
        if (text.Contains("Editor|x86 = Editor|x86", StringComparison.Ordinal)) configurations.Add("Editor|x86");
        return configurations;
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
            throw new ConvertException($"'dotnet {string.Join(' ', args)}' failed (see output above)");
    }

    private static bool TryRun(string workingDir, params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet") { WorkingDirectory = workingDir, UseShellExecute = false };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi)!;
        process.WaitForExit();
        return process.ExitCode == 0;
    }
}
