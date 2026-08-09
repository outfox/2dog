namespace twodog.cli;

/// <summary>The kinds of host project 2dog scaffolds inside a Godot project.</summary>
internal enum HostKind
{
    Desktop,
    Web,
    Tests,
    WinForms,
    Avalonia,
}

/// <summary>One host to create: its kind and the folder (and csproj) name it gets.</summary>
internal sealed record HostSpec(HostKind Kind, string Folder);

/// <summary>One host that is already present in a project.</summary>
internal sealed record ExistingHost(HostKind Kind, string Folder);

/// <summary>
/// Static facts about host kinds plus the naming rules that let a project hold
/// several hosts of the same kind: the template subtree a kind is scaffolded
/// from is fixed, the folder name is not.
/// </summary>
internal static class Hosts
{
    public static readonly IReadOnlyList<HostKind> All =
        [HostKind.Desktop, HostKind.Web, HostKind.Tests, HostKind.WinForms, HostKind.Avalonia];

    /// <summary>
    /// Whether a bare run without host flags creates this kind. WinForms is
    /// opt-in: the host only runs on Windows, so it never joins the default
    /// set. Avalonia is opt-in too - cross-platform, but it pulls the whole
    /// Avalonia UI framework plus the 2dog.avalonia package into the project.
    /// Both are still offered interactively and via their flags.
    /// </summary>
    public static bool InDefaultSet(HostKind kind) => kind is not (HostKind.WinForms or HostKind.Avalonia);

    /// <summary>The template subtree suffix - also the default folder suffix.</summary>
    public static string Suffix(HostKind kind) => kind switch
    {
        HostKind.Desktop => "2dog",
        HostKind.Web => "web",
        HostKind.Tests => "tests",
        HostKind.WinForms => "winforms",
        HostKind.Avalonia => "avalonia",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static string Label(HostKind kind) => kind switch
    {
        HostKind.Desktop => "desktop",
        HostKind.Web => "browser",
        HostKind.Tests => "tests",
        HostKind.WinForms => "winforms",
        HostKind.Avalonia => "avalonia",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static string Blurb(HostKind kind) => kind switch
    {
        HostKind.Desktop => "your own Main(), runs the game on desktop",
        HostKind.Web => "WebAssembly host, published as a static bundle",
        HostKind.Tests => "xUnit project driving a headless engine",
        HostKind.WinForms => "game embedded in a WinForms window (Windows-only)",
        HostKind.Avalonia => "game embedded in an Avalonia app (cross-platform GUI)",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>The command-line flag that selects this kind.</summary>
    public static string Flag(HostKind kind) => kind switch
    {
        HostKind.Desktop => "--desktop",
        HostKind.Web => "--web",
        HostKind.Tests => "--tests",
        HostKind.WinForms => "--winforms",
        HostKind.Avalonia => "--avalonia",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static string DefaultFolder(HostKind kind, string baseName) => $"{baseName}.{Suffix(kind)}";

    /// <summary>
    /// A folder name for a new host of this kind that no existing folder (nor
    /// any folder already planned in this run) uses: the default name, then
    /// the default with 2, 3, ... appended.
    /// </summary>
    public static string AllocateFolder(HostKind kind, string baseName, IEnumerable<string> taken)
    {
        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        var candidate = DefaultFolder(kind, baseName);
        for (var n = 2; used.Contains(candidate); n++)
            candidate = DefaultFolder(kind, baseName) + n;
        return candidate;
    }

    /// <summary>
    /// Reduce a name to a safe folder/assembly stem (dotnet-new style). A stem
    /// needs a letter or a digit: '.' and '..' survive the character filter and
    /// would otherwise write outside the project once combined into a path.
    /// </summary>
    public static string? SanitizeName(string? name)
    {
        if (name == null) return null;
        var chars = name.Where(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-').ToArray();
        return chars.Any(char.IsLetterOrDigit) ? new string(chars) : null;
    }
}

/// <summary>Recognizes the host projects an existing 2dog project already has.</summary>
internal static class HostScan
{
    /// <summary>
    /// Every immediate subdirectory that looks like a 2dog host, i.e. holds a
    /// csproj named after the folder that references the engine. Ordered by
    /// folder name so output is stable.
    /// </summary>
    public static List<ExistingHost> Find(string projectDir)
    {
        var hosts = new List<ExistingHost>();
        if (!Directory.Exists(projectDir)) return hosts;

        foreach (var dir in Directory.EnumerateDirectories(projectDir).Order(StringComparer.OrdinalIgnoreCase))
        {
            var folder = Path.GetFileName(dir);
            var csproj = Path.Combine(dir, folder + ".csproj");
            if (!File.Exists(csproj)) continue;

            string text;
            try { text = File.ReadAllText(csproj); }
            catch (IOException) { continue; }

            if (Classify(text, folder) is { } kind)
                hosts.Add(new ExistingHost(kind, folder));
        }

        return hosts;
    }

    /// <summary>
    /// The kind of host a csproj is, or null when it is not a 2dog host.
    /// Content decides first (the folder name is only a hint, since hosts may
    /// be named freely); the checks run most-specific first.
    /// </summary>
    internal static HostKind? Classify(string csproj, string folder)
    {
        var isTwoDog = csproj.Contains("2dog.engine", StringComparison.OrdinalIgnoreCase)
                       || csproj.Contains("2dog.xunit", StringComparison.OrdinalIgnoreCase)
                       || csproj.Contains("2dog.avalonia", StringComparison.OrdinalIgnoreCase)
                       || csproj.Contains("<GodotProjectDir>", StringComparison.OrdinalIgnoreCase);
        if (!isTwoDog) return null;

        if (csproj.Contains("browser-wasm", StringComparison.OrdinalIgnoreCase)) return HostKind.Web;
        if (csproj.Contains("2dog.xunit", StringComparison.OrdinalIgnoreCase)
            || csproj.Contains("xunit.v3", StringComparison.OrdinalIgnoreCase)) return HostKind.Tests;
        if (csproj.Contains("UseWindowsForms", StringComparison.OrdinalIgnoreCase)) return HostKind.WinForms;
        // Before the Desktop OutputType check: Avalonia hosts are Exe/WinExe too.
        // 2dog.avalonia catches scaffolded hosts, Avalonia.Desktop hand-rolled ones.
        if (csproj.Contains("2dog.avalonia", StringComparison.OrdinalIgnoreCase)
            || csproj.Contains("Avalonia.Desktop", StringComparison.OrdinalIgnoreCase)) return HostKind.Avalonia;
        if (csproj.Contains("<OutputType>Exe</OutputType>", StringComparison.OrdinalIgnoreCase)
            || csproj.Contains("<OutputType>WinExe</OutputType>", StringComparison.OrdinalIgnoreCase)) return HostKind.Desktop;

        // Wired to a Godot project but unrecognizable otherwise: fall back to
        // the folder suffix rather than treating it as "not a host".
        return Hosts.All.FirstOrDefault(
            k => folder.EndsWith("." + Hosts.Suffix(k), StringComparison.OrdinalIgnoreCase),
            HostKind.Desktop);
    }
}
