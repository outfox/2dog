namespace twodog.cli;

/// <summary>
/// Minimal line-oriented reader/patcher for project.godot. Append-only, except <see cref="SetAssemblyName"/>
/// (the --rename fix) which replaces or inserts the single project/assembly_name line.
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
    /// The text to append for a missing [dotnet] section. The editor sorts sections alphabetically on its next save;
    /// appending is valid syntax and survives that - we only ever append, never reorder.
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
        _lines.AddRange(DotnetSectionText(assemblyName).Split('\n').Select(l => l.TrimEnd('\r')));
    }

    /// <summary>
    /// Sets [dotnet] project/assembly_name: replaces an existing value in place, inserts the key into an existing
    /// [dotnet] section, or appends the whole section.
    /// </summary>
    public void SetAssemblyName(string assemblyName)
    {
        var keyLine = $"project/assembly_name=\"{assemblyName}\"";

        if (FindLine("dotnet", "project/assembly_name") is { } index)
        {
            // Replace just that line inside the raw text so newline style and
            // every surrounding byte survive.
            var text = File.ReadAllText(Path);
            var raw = _lines[index];
            var at = text.IndexOf(raw, StringComparison.Ordinal);
            if (at < 0) throw new InvalidOperationException($"line '{raw}' not found in {Path}");
            File.WriteAllText(Path, text[..at] + keyLine + text[(at + raw.Length)..]);
            _lines[index] = keyLine;
            return;
        }

        if (!HasSection("dotnet"))
        {
            AppendDotnetSection(assemblyName);
            return;
        }

        // [dotnet] exists without the key: insert right after the header (past
        // its conventional blank line), preserving the file's newline flavor.
        var content = File.ReadAllText(Path);
        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var header = _lines.FindIndex(l => l.Trim() == "[dotnet]");
        var insertAt = header + 1;
        if (insertAt < _lines.Count && _lines[insertAt].Trim().Length == 0) insertAt++;
        _lines.Insert(insertAt, keyLine);
        var trailingNewline = content.EndsWith('\n') ? newline : "";
        File.WriteAllText(Path, string.Join(newline, _lines) + trailingNewline);
    }

    /// <summary>Index in _lines of key inside [section], or null.</summary>
    private int? FindLine(string section, string key)
    {
        var inSection = false;
        for (var i = 0; i < _lines.Count; i++)
        {
            var line = _lines[i].Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inSection = line == $"[{section}]";
                continue;
            }

            if (!inSection || !line.StartsWith(key, StringComparison.Ordinal)) continue;
            if (line[key.Length..].TrimStart().StartsWith('=')) return i;
        }

        return null;
    }
}
