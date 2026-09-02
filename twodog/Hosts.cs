namespace twodog.cli;

/// <summary>The kinds of host project 2dog scaffolds inside a Godot project.</summary>
internal enum HostKind
{
    Desktop,
    Web,
    WebXr,
    Tests,
    WinForms,
    WinUi,
    Avalonia,
    Blazor,
}

/// <summary>Picker groups: created by default, opt-in, or opt-in and Windows-only.</summary>
internal enum HostGroup
{
    Default,
    OptIn,
    WindowsOnly,
}

/// <summary>One host to create: its kind and the folder (and csproj) name it gets.</summary>
internal sealed record HostSpec(HostKind Kind, string Folder);

/// <summary>One host that is already present in a project.</summary>
internal sealed record ExistingHost(HostKind Kind, string Folder);

/// <summary>
/// Static facts about host kinds plus the naming rules that let a project hold several hosts of the same kind:
/// the template subtree a kind is scaffolded from is fixed, the folder name is not.
/// </summary>
internal static class Hosts
{
    public static readonly IReadOnlyList<HostKind> All =
        [HostKind.Desktop, HostKind.Web, HostKind.WebXr, HostKind.Tests, HostKind.WinForms, HostKind.WinUi, HostKind.Avalonia, HostKind.Blazor];

    /// <summary>
    /// Whether a bare run without host flags creates this kind. Opt-in: WinForms/WinUI (Windows-only), Avalonia
    /// (pulls in the whole UI framework), WebXr (needs project-side XR setup), Blazor (a server + client pair).
    /// All remain available via flags/prompts.
    /// </summary>
    public static bool InDefaultSet(HostKind kind) =>
        kind is not (HostKind.WebXr or HostKind.WinForms or HostKind.WinUi or HostKind.Avalonia or HostKind.Blazor);

    /// <summary>The template subtree suffix - also the default folder suffix.</summary>
    public static string Suffix(HostKind kind) => kind switch
    {
        HostKind.Desktop => "2dog",
        HostKind.Web => "web",
        HostKind.WebXr => "webxr",
        HostKind.Tests => "tests",
        HostKind.WinForms => "winforms",
        HostKind.WinUi => "winui",
        HostKind.Avalonia => "avalonia",
        HostKind.Blazor => "blazor",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static string Label(HostKind kind) => kind switch
    {
        HostKind.Desktop => "desktop",
        HostKind.Web => "browser",
        HostKind.WebXr => "webxr",
        HostKind.Tests => "tests",
        HostKind.WinForms => "winforms",
        HostKind.WinUi => "winui",
        HostKind.Avalonia => "avalonia",
        HostKind.Blazor => "blazor",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static string Blurb(HostKind kind) => kind switch
    {
        HostKind.Desktop => "your own Main(), runs the game on desktop",
        HostKind.Web => "WebAssembly host, published as a static bundle",
        HostKind.WebXr => "WebAssembly host with the WebXR Layers polyfill for VR",
        HostKind.Tests => "xUnit project driving a headless engine",
        HostKind.WinForms => "game embedded in a WinForms window (Windows-only)",
        HostKind.WinUi => "game embedded in a WinUI 3 window (Windows-only)",
        HostKind.Avalonia => "game embedded in an Avalonia app (cross-platform GUI)",
        HostKind.Blazor => "game embedded in a Blazor Web App page (WebAssembly)",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>The command-line flag that selects this kind.</summary>
    public static string Flag(HostKind kind) => kind switch
    {
        HostKind.Desktop => "--desktop",
        HostKind.Web => "--web",
        HostKind.WebXr => "--webxr",
        HostKind.Tests => "--tests",
        HostKind.WinForms => "--winforms",
        HostKind.WinUi => "--winui",
        HostKind.Avalonia => "--avalonia",
        HostKind.Blazor => "--blazor",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Alternative spellings of the flag, accepted but not advertised.</summary>
    public static IEnumerable<string> FlagAliases(HostKind kind) => kind switch
    {
        HostKind.Desktop => ["--2dog"],
        HostKind.Web => ["--browser"],
        HostKind.Tests => ["--test"],
        _ => [],
    };

    /// <summary>The flag that leaves this kind out of the default set: the host flag, negated.</summary>
    public static string NoFlag(HostKind kind) => "--no-" + Flag(kind)[2..];

    /// <summary>Browser-side hosts: they carry TwoDogWebBoot.cs, need wasm-tools, and publish a pck.</summary>
    public static bool IsWebLike(HostKind kind) => kind is HostKind.Web or HostKind.WebXr or HostKind.Blazor;

    /// <summary>
    /// Kinds kept out of plain solution builds: the browser ones need wasm-tools, WinUI builds only on Windows.
    /// </summary>
    public static bool ExcludedFromSolutionBuild(HostKind kind) => IsWebLike(kind) || kind == HostKind.WinUi;

    /// <summary>The help row for the host flag: what the host is plus availability notes.</summary>
    public static string HelpText(HostKind kind) => kind switch
    {
        HostKind.Desktop => "Desktop host (your own Main entry point)",
        HostKind.Web => "Browser (WebAssembly) host",
        HostKind.WebXr => "Browser host with the WebXR Layers polyfill wired into its page (opt-in)",
        HostKind.Tests => "xUnit test project",
        HostKind.WinForms => "WinForms host embedding the game window (Windows-only; never part of the default set)",
        HostKind.WinUi => "WinUI 3 host embedding the game window (Windows-only, like --winforms; builds only on Windows)",
        HostKind.Avalonia => "Avalonia host embedding the game in a cross-platform GUI (opt-in, like --winforms)",
        HostKind.Blazor => "Blazor Web App host: ASP.NET Core server plus a WebAssembly client page embedding the " +
                           "game (opt-in; needs wasm-tools)",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>How the interactive picker groups the kinds.</summary>
    public static HostGroup Group(HostKind kind) => kind switch
    {
        HostKind.WinForms or HostKind.WinUi => HostGroup.WindowsOnly,
        _ when InDefaultSet(kind) => HostGroup.Default,
        _ => HostGroup.OptIn,
    };

    /// <summary>
    /// The Blazor host is a pair: the server csproj named after the folder plus the WebAssembly client project
    /// nested in Client/ (the one that links Godot). Relative to the project root, forward slashes.
    /// </summary>
    public static string BlazorClientProject(string folder) => $"{folder}/Client/{folder}.Client.csproj";

    public static string DefaultFolder(HostKind kind, string baseName) => $"{baseName}.{Suffix(kind)}";

    /// <summary>
    /// A folder name for a new host of this kind unused by existing or already-planned folders: the default name,
    /// then the default with 2, 3, ... appended.
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
    /// Reduce a name to a safe folder/assembly stem (dotnet-new style). A stem needs a letter or digit: '.' and '..'
    /// survive the character filter and would otherwise write outside the project.
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
    /// Every immediate subdirectory holding a csproj named after the folder that references the engine.
    /// Ordered by folder name so output is stable.
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
    /// The kind of host a csproj is, or null when it is not a 2dog host. Content decides (the folder name is only
    /// a hint, hosts may be named freely); the checks run most-specific first. Parsed as XML when possible so
    /// namespaces, attributes and comments cannot fool the substring matcher, which stays as the fallback.
    /// </summary>
    internal static HostKind? Classify(string csproj, string folder)
    {
        System.Xml.Linq.XDocument doc;
        try
        {
            doc = System.Xml.Linq.XDocument.Parse(csproj);
        }
        catch (System.Xml.XmlException)
        {
            return ClassifyText(csproj, folder);
        }

        if (doc.Root is not { } root) return ClassifyText(csproj, folder);

        var packages = root.Descendants()
            .Where(e => e.Name.LocalName == "PackageReference")
            .Select(e => (string?)e.Attribute("Include") ?? "")
            .ToList();
        bool Package(string id) => packages.Any(p => p.Equals(id, StringComparison.OrdinalIgnoreCase));
        string? Property(string name) => MsBuildXml.Property(root, name);
        bool PropertyTrue(string name) => Property(name)?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
        var sdk = (string?)root.Attribute("Sdk") ?? "";

        var isTwoDog = Package("2dog.engine") || Package("2dog.xunit") || Package("2dog.avalonia")
                       || Property("TwoDogBlazor") != null || Property("GodotProjectDir") != null;
        if (!isTwoDog) return null;

        if (PropertyTrue("TwoDogBlazor")) return HostKind.Blazor;
        if (PropertyTrue("TwoDogWebXR")) return HostKind.WebXr;
        if ((Property("RuntimeIdentifier") ?? "").Contains("browser-wasm", StringComparison.OrdinalIgnoreCase)
            || (Property("RuntimeIdentifiers") ?? "").Contains("browser-wasm", StringComparison.OrdinalIgnoreCase)
            || sdk.Contains("BlazorWebAssembly", StringComparison.OrdinalIgnoreCase)) return HostKind.Web;
        if (Package("2dog.xunit") || packages.Any(p => p.StartsWith("xunit", StringComparison.OrdinalIgnoreCase)))
            return HostKind.Tests;
        if (PropertyTrue("UseWindowsForms")) return HostKind.WinForms;
        if (PropertyTrue("UseWinUI")) return HostKind.WinUi;
        // Before the Desktop OutputType check: Avalonia hosts are Exe/WinExe too.
        if (Package("2dog.avalonia") || Package("Avalonia.Desktop")) return HostKind.Avalonia;
        if (Property("OutputType") is { } outputType
            && (outputType.Equals("Exe", StringComparison.OrdinalIgnoreCase)
                || outputType.Equals("WinExe", StringComparison.OrdinalIgnoreCase))) return HostKind.Desktop;

        return BySuffix(folder);
    }

    /// <summary>Substring classification for csprojs that do not parse as XML.</summary>
    internal static HostKind? ClassifyText(string csproj, string folder)
    {
        var isTwoDog = csproj.Contains("2dog.engine", StringComparison.OrdinalIgnoreCase)
                       || csproj.Contains("2dog.xunit", StringComparison.OrdinalIgnoreCase)
                       || csproj.Contains("2dog.avalonia", StringComparison.OrdinalIgnoreCase)
                       || csproj.Contains("<TwoDogBlazor", StringComparison.OrdinalIgnoreCase)
                       || csproj.Contains("<GodotProjectDir>", StringComparison.OrdinalIgnoreCase);
        if (!isTwoDog) return null;

        // Before the plain Web check: Blazor (server csproj marked TwoDogBlazor; its client is browser-wasm) and
        // WebXR hosts (browser-wasm too, marked by the TwoDogWebXR property). Tolerant of whitespace and
        // attributes so hand-formatted csprojs still match.
        if (System.Text.RegularExpressions.Regex.IsMatch(csproj,
                @"<TwoDogBlazor(\s[^>]*)?>\s*true\s*</TwoDogBlazor\s*>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return HostKind.Blazor;
        if (System.Text.RegularExpressions.Regex.IsMatch(csproj,
                @"<TwoDogWebXR(\s[^>]*)?>\s*true\s*</TwoDogWebXR\s*>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return HostKind.WebXr;
        if (csproj.Contains("browser-wasm", StringComparison.OrdinalIgnoreCase)) return HostKind.Web;
        if (csproj.Contains("2dog.xunit", StringComparison.OrdinalIgnoreCase)
            || csproj.Contains("xunit.v3", StringComparison.OrdinalIgnoreCase)) return HostKind.Tests;
        if (csproj.Contains("UseWindowsForms", StringComparison.OrdinalIgnoreCase)) return HostKind.WinForms;
        if (csproj.Contains("UseWinUI", StringComparison.OrdinalIgnoreCase)) return HostKind.WinUi;
        // Before the Desktop OutputType check: Avalonia hosts are Exe/WinExe too.
        // 2dog.avalonia catches scaffolded hosts, Avalonia.Desktop hand-rolled ones.
        if (csproj.Contains("2dog.avalonia", StringComparison.OrdinalIgnoreCase)
            || csproj.Contains("Avalonia.Desktop", StringComparison.OrdinalIgnoreCase)) return HostKind.Avalonia;
        if (csproj.Contains("<OutputType>Exe</OutputType>", StringComparison.OrdinalIgnoreCase)
            || csproj.Contains("<OutputType>WinExe</OutputType>", StringComparison.OrdinalIgnoreCase)) return HostKind.Desktop;

        return BySuffix(folder);
    }

    /// <summary>Wired to a Godot project but unrecognizable otherwise: the folder suffix decides, default desktop.</summary>
    private static HostKind BySuffix(string folder) =>
        Hosts.All.FirstOrDefault(
            k => folder.EndsWith("." + Hosts.Suffix(k), StringComparison.OrdinalIgnoreCase),
            HostKind.Desktop);
}
