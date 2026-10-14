using System.Text;
using System.Text.Json;

namespace twodog.GdUnit;

/// <summary>A writable copy whose resource paths match the source game, including selected ignored test roots.</summary>
internal sealed class StagedProject
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".godot", ".packages", ".idea", ".vs", ".vscode", "bin", "obj", "packages", "temp"
    };
    private static readonly UTF8Encoding Utf8 = new(false);

    public string SourceRoot { get; }
    public string Root { get; }

    public StagedProject(string sourceRoot, IEnumerable<string> testRoots, string gdunitRoot, string bridgeRoot, string destination,
        bool pauseAfterDiscovery = false, CancellationToken cancellationToken = default)
    {
        _cancellationToken = cancellationToken;
        SourceRoot = Path.GetFullPath(sourceRoot);
        Root = Path.GetFullPath(destination);
        if (!File.Exists(Path.Combine(SourceRoot, "project.godot")))
            throw new ArgumentException("Source must contain project.godot.", nameof(sourceRoot));
        if (Directory.Exists(Root) || File.Exists(Root))
            throw new IOException($"Staging destination already exists: {Root}");
        if (IsWithin(Root, SourceRoot))
            throw new ArgumentException("Staging destination cannot contain the source project.", nameof(destination));

        var selectedRoots = testRoots.Select(path => ResolveSource(path)).Distinct(StringComparer.Ordinal).ToArray();
        if (selectedRoots.Length == 0) throw new ArgumentException("Select at least one test root.", nameof(testRoots));
        foreach (var selected in selectedRoots)
            if (!Directory.Exists(selected)) throw new DirectoryNotFoundException(selected);
        CopyTree(SourceRoot, Root);
        foreach (var selected in selectedRoots)
        {
            var staged = Path.Combine(Root, Path.GetRelativePath(SourceRoot, selected));
            CopyTree(selected, staged, selectedRoot: true);
        }

        var addonDestination = Path.Combine(Root, "addons", "gdUnit4");
        if (Directory.Exists(addonDestination))
            throw new IOException("The source already contains addons/gdUnit4; choose one framework explicitly.");
        CopyTree(Path.Combine(gdunitRoot, "addons", "gdUnit4"), addonDestination, framework: true);
        if (!File.Exists(Path.Combine(addonDestination, "LICENSE")))
            File.Copy(Path.Combine(gdunitRoot, "LICENSE"), Path.Combine(addonDestination, "LICENSE"));
        var bridgeDestination = Path.Combine(Root, "twodog_test_host");
        if (Directory.Exists(bridgeDestination)) throw new IOException("Reserved resource path twodog_test_host already exists.");
        CopyTree(bridgeRoot, bridgeDestination);
        var settingsPath = Path.Combine(Root, "project.godot");
        var settings = SetSetting(File.ReadAllText(settingsPath), "application", "run/main_scene", "\"res://twodog_test_host/runner.tscn\"");
        // Release natives otherwise omit the call stacks GdUnit uses for assertion source locations.
        settings = SetSetting(settings, "debug", "settings/gdscript/always_track_call_stacks", "true");
        File.WriteAllText(settingsPath, settings, Utf8);
        File.WriteAllText(Path.Combine(bridgeDestination, "request.json"), JsonSerializer.Serialize(new
        {
            roots = selectedRoots.Select(path => "res://" + Path.GetRelativePath(SourceRoot, path).Replace('\\', '/')),
            pause_after_discovery = pauseAfterDiscovery
        }), Utf8);
    }

    public string OriginalFile(string resourcePath) => ResolveSource(resourcePath.StartsWith("res://", StringComparison.Ordinal)
        ? resourcePath[6..] : throw new ArgumentException("Expected a res:// resource path.", nameof(resourcePath)));

    private string ResolveSource(string relativePath)
    {
        if (Path.IsPathRooted(relativePath)) throw new ArgumentException("Test paths must be relative to the source game.");
        var resolved = Path.GetFullPath(Path.Combine(SourceRoot, relativePath));
        if (!IsWithin(SourceRoot, resolved) || resolved == SourceRoot)
            throw new ArgumentException($"Path escapes the source project: {relativePath}");
        return resolved;
    }

    private readonly CancellationToken _cancellationToken;

    private void CopyTree(string source, string destination, bool selectedRoot = false, bool framework = false)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
        if (File.GetAttributes(source).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException($"Staging linked directories is unsupported: {source}");
        // Framework templates deliberately use .gdignore yet are read through FileAccess at runtime.
        // Keep their bytes and exclusion markers; only the selected game's ignored trees are omitted.
        if (!selectedRoot && !framework && File.Exists(Path.Combine(source, ".gdignore"))) return;
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source).Order(StringComparer.Ordinal))
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(file);
            if ((name == ".gdignore" && !framework) || name == ".git" || name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)) continue;
            if (File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException($"Staging linked files is unsupported: {file}");
            File.Copy(file, Path.Combine(destination, name), overwrite: true);
        }
        foreach (var directory in Directory.EnumerateDirectories(source).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(directory);
            if (ExcludedDirectories.Contains(name) || (framework && name == "test")
                || Path.GetFullPath(directory).Equals(Root, StringComparison.OrdinalIgnoreCase)) continue;
            CopyTree(directory, Path.Combine(destination, name), framework: framework);
        }
    }

    private static bool IsWithin(string parent, string child)
    {
        var relative = Path.GetRelativePath(parent, child);
        return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    // Preserve unrelated settings, including autoloads. Only the private copy receives host overrides.
    private static string SetSetting(string settings, string section, string key, string value)
    {
        var lines = settings.Replace("\r\n", "\n").Split('\n').ToList();
        var start = lines.FindIndex(line => line.Trim() == $"[{section}]");
        if (start < 0)
        {
            lines.Add($"[{section}]");
            start = lines.Count - 1;
        }
        var end = lines.FindIndex(start + 1, line => line.TrimStart().StartsWith('['));
        if (end < 0) end = lines.Count;
        for (var index = end - 1; index > start; index--)
            if (lines[index].Split('=', 2)[0].Trim() == key) lines.RemoveAt(index);
        lines.Insert(start + 1, $"{key}={value}");
        return string.Join("\r\n", lines);
    }
}
