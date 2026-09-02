namespace twodog.cli;

/// <summary>Facts about the process console, captured once at startup (tests pin their own).</summary>
internal sealed record ConsoleFacts(bool InputRedirected, bool OutputRedirected, bool ErrorRedirected, bool OutputIsUtf8)
{
    public static ConsoleFacts Capture() => new(
        Console.IsInputRedirected, Console.IsOutputRedirected, Console.IsErrorRedirected,
        Console.OutputEncoding.CodePage == 65001);

    /// <summary>What tests see: every stream redirected, so nothing prompts, animates or colours.</summary>
    public static readonly ConsoleFacts Redirected = new(true, true, true, false);
}

/// <summary>
/// How output is rendered, resolved once from the raw command line, the environment and the console facts. The
/// command line is pre-scanned (not parsed) so that even a usage error already renders in the requested mode.
/// </summary>
internal sealed record OutputMode
{
    /// <summary>One JSON document on stdout, nothing else; implies --yes.</summary>
    public bool Json { get; init; }

    /// <summary>Results and problems only: no header, plan, progress or next steps.</summary>
    public bool Quiet { get; init; }

    /// <summary>No ANSI at all (--plain, TERM=dumb).</summary>
    public bool Plain { get; init; }

    /// <summary>Cursor movement stays, colour goes (--no-color, NO_COLOR).</summary>
    public bool NoColor { get; init; }

    /// <summary>Colour even when redirected (CLICOLOR_FORCE, FORCE_COLOR).</summary>
    public bool ForceColor { get; init; }

    public bool Verbose { get; init; }

    /// <summary>Sequential yes/no prompts instead of lists, static lines instead of spinners.</summary>
    public bool Accessible { get; init; }

    /// <summary>A CI environment: never prompt, never animate.</summary>
    public bool Ci { get; init; }

    public required ConsoleFacts Console { get; init; }

    /// <summary>Whether glyphs may be UTF-8; redirected output gets ASCII markers so it stays greppable.</summary>
    public bool Unicode => Console.OutputIsUtf8 && !Plain && !Accessible && !Console.OutputRedirected;

    /// <summary>Whether spinners are possible at all (--verbose streams output instead, so no spinner then).</summary>
    public bool Animate =>
        !Json && !Quiet && !Plain && !Accessible && !Ci && !Verbose
        && !Console.OutputRedirected && !Console.ErrorRedirected;

    /// <summary>Whether prompting is possible (the command line decides separately whether it is wanted).</summary>
    public bool CanPrompt => !Json && !Ci && !Console.InputRedirected && !Console.OutputRedirected;

    private static readonly string[] CiVariables =
    [
        "CI", "GITHUB_ACTIONS", "TF_BUILD", "GITLAB_CI", "TEAMCITY_VERSION", "BUILD_NUMBER", "JENKINS_URL",
        "BUILDKITE", "CIRCLECI", "TRAVIS", "APPVEYOR",
    ];

    public static OutputMode Resolve(IReadOnlyList<string> args, Func<string, string?> env, ConsoleFacts console)
    {
        var flags = new HashSet<string>(StringComparer.Ordinal);
        foreach (var arg in args)
        {
            if (arg == "--") break;
            flags.Add(arg.Split(['=', ':'], 2)[0]);
        }

        bool Has(System.CommandLine.Option option) => CliTree.NamesOf(option).Any(flags.Contains);

        var plain = Has(CliTree.PlainOption) || string.Equals(env("TERM"), "dumb", StringComparison.OrdinalIgnoreCase);
        return new OutputMode
        {
            Json = Has(CliTree.Json),
            Quiet = Has(CliTree.Quiet),
            Plain = plain,
            NoColor = Has(CliTree.NoColor) || !string.IsNullOrEmpty(env("NO_COLOR")),
            ForceColor = !plain && (Set(env("CLICOLOR_FORCE")) || Set(env("FORCE_COLOR"))),
            Verbose = Has(CliTree.Verbose),
            Accessible = plain || Has(CliTree.Accessible) || Set(env("TWODOG_ACCESSIBLE")),
            Ci = CiVariables.Any(v => Set(env(v))),
            Console = console,
        };
    }

    /// <summary>An environment switch counts when set to anything but "", "0" or "false".</summary>
    private static bool Set(string? value) =>
        !string.IsNullOrEmpty(value) && value != "0" && !value.Equals("false", StringComparison.OrdinalIgnoreCase);
}
