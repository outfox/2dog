using System.Text;

namespace twodog.Repl;

/// <summary>Submitted inputs, oldest first. A file keeps one base64 entry per line, so entries may span lines.</summary>
internal sealed class ReplHistory
{
    internal const int Capacity = 500;
    private readonly List<string> entries = [];
    private readonly string? path;
    private readonly Lock gate = new();

    public ReplHistory(string? path = null)
    {
        this.path = string.IsNullOrEmpty(path) ? null : path;
        if (this.path is null || !File.Exists(this.path)) return;
        try
        {
            foreach (var line in File.ReadLines(this.path).TakeLast(Capacity))
            {
                try { entries.Add(Encoding.UTF8.GetString(Convert.FromBase64String(line))); }
                catch (FormatException) { /* Skip a damaged line; history is a convenience. */ }
            }
        }
        catch (IOException) { /* Another process may hold the file; start with an empty history. */ }
    }

    public int Count { get { lock (gate) return entries.Count; } }

    public void Add(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry)) return;
        lock (gate)
        {
            entries.Add(entry);
            if (entries.Count > Capacity) entries.RemoveRange(0, entries.Count - Capacity);
            if (path is null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.AppendAllText(path, Convert.ToBase64String(Encoding.UTF8.GetBytes(entry)) + "\n");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* Keep the in-memory entry. */ }
        }
    }

    /// <summary>The nearest entry before (direction -1) or after (+1) <paramref name="from"/> that starts with
    /// <paramref name="prefix"/> and differs from <paramref name="shown"/>. <paramref name="from"/> equal to
    /// <see cref="Count"/> is the unsaved draft.</summary>
    public (int Index, string Entry)? Find(int from, int direction, string prefix, string shown)
    {
        lock (gate)
        {
            for (var i = from + direction; i >= 0 && i < entries.Count; i += direction)
                if (entries[i] != shown && entries[i].StartsWith(prefix, StringComparison.Ordinal)) return (i, entries[i]);
            return null;
        }
    }
}
