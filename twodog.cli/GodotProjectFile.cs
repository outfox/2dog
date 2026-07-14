namespace twodog.cli;

/// <summary>
/// Minimal line-oriented reader/patcher for project.godot. Never rewrites the
/// file: patching inserts lines at a computed position and leaves every
/// existing byte untouched.
/// </summary>
internal sealed class GodotProjectFile
{
    public string Path { get; }
    private readonly List<string> _lines;

    public GodotProjectFile(string path)
    {
        Path = path;
        _lines = File.ReadAllLines(path).ToList();
    }

    /// <summary>Value of key inside [section], unquoted, or null.</summary>
    public string? Get(string section, string key)
    {
        var inSection = false;
        foreach (var raw in _lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inSection = line == $"[{section}]";
                continue;
            }

            if (!inSection || !line.StartsWith(key, StringComparison.Ordinal)) continue;
            var rest = line[key.Length..].TrimStart();
            if (!rest.StartsWith('=')) continue;
            return rest[1..].Trim().Trim('"');
        }

        return null;
    }

    public bool HasSection(string section) =>
        _lines.Any(l => l.Trim() == $"[{section}]");

    /// <summary>
    /// The text to append for a missing [dotnet] section. Sections in
    /// project.godot are sorted alphabetically by the editor, but appending
    /// is valid config syntax and survives the next editor save (which
    /// re-sorts) - we only ever append, never reorder existing content.
    /// </summary>
    public static string DotnetSectionText(string assemblyName) =>
        $"""

         [dotnet]

         project/assembly_name="{assemblyName}"
         """;

    public void AppendDotnetSection(string assemblyName)
    {
        if (HasSection("dotnet")) throw new InvalidOperationException("[dotnet] section already present");
        File.AppendAllText(Path, DotnetSectionText(assemblyName) + Environment.NewLine);
    }
}
