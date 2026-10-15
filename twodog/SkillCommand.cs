namespace twodog.cli;

/// <summary>
/// The skill verb: writes the REPL working guide where coding agents look for project instructions - a Claude Code
/// skill, or a marked section of AGENTS.md that later runs replace in place.
/// </summary>
internal static class SkillCommand
{
    internal const string SkillFile = ".claude/skills/2dog-repl/SKILL.md";
    internal const string AgentsFile = "AGENTS.md";
    internal const string Begin = "<!-- 2dog:repl-guide -->", End = "<!-- /2dog:repl-guide -->";

    /// <summary>The guide shared with the REPL host's ':help agents'.</summary>
    internal static string Guide { get; } = ReadGuide();

    /// <summary>Claude Code loads a skill when its description matches the task.</summary>
    internal static string Skill { get; } =
        "---\nname: 2dog-repl\ndescription: Work in a running Godot game through the 2dog REPL - inspect and change the live scene " +
        "tree with 2dog repl exec, run C# in named sessions, simulate input and take screenshots. Use when a 2dog REPL host " +
        "is running or the user asks to work in the live game.\n---\n\n" + Guide + "\n";

    public static int Run(SkillOptions options, Report report)
    {
        var dir = Path.GetFullPath(options.Path ?? Environment.CurrentDirectory);
        if (!Directory.Exists(dir)) throw new ToolException($"{dir} is not a directory");
        var actions = report.Actions = [];
        report.DryRun = options.DryRun ? true : null;
        if (options.Claude || !options.Codex) Write(dir, SkillFile, _ => Skill, options.DryRun, actions);
        if (options.Codex) Write(dir, AgentsFile, Section, options.DryRun, actions);
        return ExitCodes.Ok;
    }

    /// <summary>The project's AGENTS.md with the guide section added or replaced; everything else stays.</summary>
    internal static string Section(string? existing)
    {
        var section = $"{Begin}\n{Guide}\n{End}\n";
        if (string.IsNullOrEmpty(existing)) return section;
        var start = existing.IndexOf(Begin, StringComparison.Ordinal);
        var end = start < 0 ? -1 : existing.IndexOf(End, start, StringComparison.Ordinal);
        if (end < 0) return existing.TrimEnd() + "\n\n" + section;
        var after = end + End.Length;
        if (after < existing.Length && existing[after] == '\n') after++;
        return existing[..start] + section + existing[after..];
    }

    private static void Write(string dir, string relative, Func<string?, string> content, bool dryRun, List<ReportAction> actions)
    {
        var path = Path.Combine(dir, relative);
        var existing = File.Exists(path) ? File.ReadAllText(path) : null;
        // Line endings are the working tree's business; compare the text alone.
        var text = content(existing?.ReplaceLineEndings("\n"));
        var status = existing is null ? "created" : existing.ReplaceLineEndings("\n") == text ? "unchanged" : "updated";
        if (status == "unchanged") Out.Skip($"{relative} is up to date");
        else if (dryRun) Out.Would($"{(existing is null ? "create" : "update")} {relative}");
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            Out.Action($"{(existing is null ? "create" : "update")} {relative}");
        }
        actions.Add(new ReportAction("file", relative, dryRun && status != "unchanged" ? "planned" : status));
    }

    private static string ReadGuide()
    {
        using var stream = typeof(SkillCommand).Assembly.GetManifestResourceStream("twodog.repl-guide.md")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().ReplaceLineEndings("\n").Trim();
    }
}
