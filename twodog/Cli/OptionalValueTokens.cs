namespace twodog.cli;

/// <summary>
/// Pre-pass giving every optional-value flag (host flags, doctor's --build) an explicit value before
/// System.CommandLine sees it: `--web MyGame.web2` becomes `--web=MyGame.web2`, a bare `--web` becomes `--web=*`
/// (pick a name). Without it the parser would take the next token, so `2dog add --web ./MyGame` could no longer
/// read `./MyGame` as the project path.
/// </summary>
internal static class OptionalValueTokens
{
    public static string[] Normalize(IReadOnlyList<string> args)
    {
        var flags = CliTree.OptionalValueOptions.SelectMany(CliTree.NamesOf).ToHashSet(StringComparer.Ordinal);
        var result = new List<string>(args.Count);
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg == "--")
            {
                result.AddRange(args.Skip(i));
                break;
            }

            if (!flags.Contains(arg))
            {
                result.Add(arg);
                continue;
            }

            if (i + 1 < args.Count && IsFolderToken(args[i + 1]))
                result.Add($"{arg}={args[++i]}");
            else
                result.Add($"{arg}={CliTree.AnyFolder}");
        }

        return result.ToArray();
    }

    /// <summary>
    /// Whether a token after a host flag is a folder name for it. Path-like tokens are left alone so they still
    /// read as the project path.
    /// </summary>
    internal static bool IsFolderToken(string token) =>
        !token.StartsWith('-') && !token.Contains('/') && !token.Contains('\\') && token != ".";
}
