using System.Text;

namespace twodog.cli;

/// <summary>
/// Minimal line-oriented reader/patcher for project.godot. Append-only, except <see cref="Set"/> which replaces or
/// inserts a single key line. The file's newline flavour and byte-order mark survive every edit.
/// </summary>
internal sealed class GodotProjectFile
{
    public string Path { get; }
    private readonly List<string> _lines;
    private readonly string _newline;
    private readonly bool _bom;

    public GodotProjectFile(string path)
    {
        Path = path;
        var bytes = File.ReadAllBytes(path);
        _bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var text = Encoding.UTF8.GetString(bytes, _bom ? 3 : 0, bytes.Length - (_bom ? 3 : 0));
        _newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        _lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (_lines.Count > 0 && _lines[^1].Length == 0) _lines.RemoveAt(_lines.Count - 1);
    }

    /// <summary>Value of key inside [section], unquoted and unescaped, or null.</summary>
    public string? Get(string section, string key)
    {
        if (FindLine(section, key) is not { } index) return null;
        var line = _lines[index].Trim();
        var value = line[key.Length..].TrimStart()[1..].Trim();
        return Unquote(value);
    }

    private static string Unquote(string value)
    {
        if (value.Length < 2 || value[0] != '"' || value[^1] != '"') return value;
        return value[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
    }

    public bool HasSection(string section) =>
        _lines.Any(l => l.Trim() == $"[{section}]");

    /// <summary>
    /// The text to append for a missing [dotnet] section. The editor sorts sections alphabetically on its next save;
    /// appending is valid syntax and survives that - we only ever append, never reorder.
    /// </summary>
    public static string DotnetSectionText(string assemblyName) => SectionText("dotnet", "project/assembly_name", assemblyName);

    private static string SectionText(string section, string key, string value, bool raw = false) =>
        $"""

         [{section}]

         {key}={(raw ? value : $"\"{Escape(value)}\"")}
         """;

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    public void AppendDotnetSection(string assemblyName)
    {
        if (HasSection("dotnet")) throw new InvalidOperationException("[dotnet] section already present");
        AppendSection("dotnet", "project/assembly_name", assemblyName);
    }

    private void AppendSection(string section, string key, string value, bool raw = false)
    {
        var text = SectionText(section, key, value, raw).ReplaceLineEndings(_newline) + _newline;
        var existing = File.ReadAllText(Path);
        // Never glue onto a last line that lacks its newline.
        if (existing.Length > 0 && !existing.EndsWith('\n')) text = _newline + text;
        File.AppendAllText(Path, text, new UTF8Encoding(false));
        _lines.AddRange(SectionText(section, key, value, raw).Split('\n').Select(l => l.TrimEnd('\r')));
    }

    /// <summary>Sets [dotnet] project/assembly_name (the --rename fix).</summary>
    public void SetAssemblyName(string assemblyName) => Set("dotnet", "project/assembly_name", assemblyName);

    /// <summary>
    /// Sets key inside [section]: replaces an existing value in place, inserts the key into an existing section,
    /// or appends the whole section. Everything else in the file stays byte-identical. Raw values (booleans,
    /// numbers) are written unquoted.
    /// </summary>
    public void Set(string section, string key, string value, bool raw = false)
    {
        var keyLine = raw ? $"{key}={value}" : $"{key}=\"{Escape(value)}\"";

        if (FindLine(section, key) is { } index)
        {
            // Replace just that line inside the raw text so every surrounding byte survives.
            var text = File.ReadAllText(Path);
            var existing = _lines[index];
            var at = text.IndexOf(existing, StringComparison.Ordinal);
            if (at < 0) throw new InvalidOperationException($"line '{existing}' not found in {Path}");
            Save(text[..at] + keyLine + text[(at + existing.Length)..]);
            _lines[index] = keyLine;
            return;
        }

        if (!HasSection(section))
        {
            AppendSection(section, key, value, raw);
            return;
        }

        // The section exists without the key: insert right after the header (past its conventional blank line).
        var content = File.ReadAllText(Path);
        var header = _lines.FindIndex(l => l.Trim() == $"[{section}]");
        var insertAt = header + 1;
        if (insertAt < _lines.Count && _lines[insertAt].Trim().Length == 0) insertAt++;
        _lines.Insert(insertAt, keyLine);
        var trailingNewline = content.EndsWith('\n') ? _newline : "";
        Save(string.Join(_newline, _lines) + trailingNewline);
    }

    private void Save(string content) => File.WriteAllText(Path, content, new UTF8Encoding(_bom));

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
