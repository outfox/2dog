namespace twodog.cli;

/// <summary>The kinds of host project 2dog scaffolds inside a Godot project.</summary>
internal enum HostKind
{
    Desktop,
    Web,
    WebXr,
    Tests,
    NUnit,
    Repl,
    WinForms,
    WinUi,
    Avalonia,
    Blazor,
    Android,
}

/// <summary>Picker groups by the kind of application a host creates.</summary>
internal enum HostGroup
{
    Desktop,
    Browser,
    Mobile,
    Testing,
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
        [HostKind.Desktop, HostKind.Web, HostKind.WebXr, HostKind.Tests, HostKind.NUnit, HostKind.Repl, HostKind.WinForms,
         HostKind.WinUi, HostKind.Avalonia, HostKind.Blazor, HostKind.Android];

    /// <summary>
    /// Whether a bare run without host flags creates this kind. Opt-in: WinForms/WinUI (Windows-only), Avalonia
    /// (pulls in the whole UI framework), WebXr (needs project-side XR setup), Blazor (a server + client pair),
    /// Android (needs the android workload), REPL (runtime C# compiler), xUnit and NUnit (choose a test framework explicitly).
    /// All remain available via flags/prompts.
    /// </summary>
    public static bool InDefaultSet(HostKind kind) =>
        kind is not (HostKind.WebXr or HostKind.WinForms or HostKind.WinUi or HostKind.Avalonia or HostKind.Blazor
            or HostKind.Android or HostKind.Tests or HostKind.NUnit or HostKind.Repl);

    /// <summary>The template subtree suffix - also the default folder suffix.</summary>
    public static string Suffix(HostKind kind) => kind switch
    {
        HostKind.Desktop => "2dog",
        HostKind.Web => "web",
        HostKind.WebXr => "webxr",
        HostKind.Tests => "xunit",
        HostKind.NUnit => "nunit",
        HostKind.Repl => "repl",
        HostKind.WinForms => "winforms",
        HostKind.WinUi => "winui",
        HostKind.Avalonia => "avalonia",
        HostKind.Blazor => "blazor",
        HostKind.Android => "android",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Stable host-kind identifiers for machine-readable reports.</summary>
    public static string Id(HostKind kind) => kind switch
    {
        HostKind.Desktop => "generic",
        HostKind.Web => "browser",
        HostKind.WebXr => "webxr",
        HostKind.Tests => "tests",
        HostKind.NUnit => "nunit",
        HostKind.Repl => "repl",
        HostKind.WinForms => "winforms",
        HostKind.WinUi => "winui",
        HostKind.Avalonia => "avalonia",
        HostKind.Blazor => "blazor",
        HostKind.Android => "android",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static string Label(HostKind kind) => kind switch
    {
        HostKind.Desktop => ".NET (generic)",
        HostKind.Web => "web",
        HostKind.Tests => "xUnit",
        HostKind.NUnit => "NUnit",
        _ => Id(kind),
    };

    public static string Blurb(HostKind kind) => kind switch
    {
        HostKind.Desktop => "your own Main(), runs the game as a .NET app",
        HostKind.Web => "WebAssembly host, published as a static bundle",
        HostKind.WebXr => "WebAssembly host with the WebXR Layers polyfill for VR",
        HostKind.Tests => "xUnit project driving a headless engine",
        HostKind.NUnit => "NUnit project driving a headless engine",
        HostKind.Repl => "interactive C# prompt inside the running game",
        HostKind.WinForms => "game embedded in a WinForms window (Windows-only)",
        HostKind.WinUi => "game embedded in a WinUI 3 window (Windows-only)",
        HostKind.Avalonia => "game embedded in an Avalonia app (cross-platform GUI)",
        HostKind.Blazor => "game embedded in a Blazor Web App page (WebAssembly)",
        HostKind.Android => "Android app, published as an APK (experimental)",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>The command-line flag that selects this kind.</summary>
    public static string Flag(HostKind kind) => kind switch
    {
        HostKind.Desktop => "--generic",
        HostKind.Web => "--web",
        HostKind.WebXr => "--webxr",
        HostKind.Tests => "--xunit",
        HostKind.NUnit => "--nunit",
        HostKind.Repl => "--repl",
        HostKind.WinForms => "--winforms",
        HostKind.WinUi => "--winui",
        HostKind.Avalonia => "--avalonia",
        HostKind.Blazor => "--blazor",
        HostKind.Android => "--android",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Alternative spellings of the flag, accepted but not advertised.</summary>
    public static IEnumerable<string> FlagAliases(HostKind kind) => kind switch
    {
        HostKind.Desktop => ["--2dog"],
        HostKind.Web => ["--browser"],
        HostKind.Tests => ["--tests", "--test"],
        _ => [],
    };

    /// <summary>The flag that leaves this kind out of the default set: the host flag, negated.</summary>
    public static string NoFlag(HostKind kind) => "--no-" + Flag(kind)[2..];

    /// <summary>Browser-side hosts: they carry TwoDogWebBoot.cs, need wasm-tools, and publish a pck.</summary>
    public static bool IsWebLike(HostKind kind) => kind is HostKind.Web or HostKind.WebXr or HostKind.Blazor;

    /// <summary>
    /// Kinds kept out of plain solution builds: the browser ones need wasm-tools, Android the android workload (a
    /// build-excluded project is not restored either), WinUI builds only on Windows.
    /// </summary>
    public static bool ExcludedFromSolutionBuild(HostKind kind) =>
        IsWebLike(kind) || kind is HostKind.WinUi or HostKind.Android;

    /// <summary>The help row for the host flag: what the host is plus availability notes.</summary>
    public static string HelpText(HostKind kind) => kind switch
    {
        HostKind.Desktop => "Generic .NET host (your own Main entry point)",
        HostKind.Web => "Browser (WebAssembly) host",
        HostKind.WebXr => "Browser host with the WebXR Layers polyfill wired into its page (opt-in)",
        HostKind.Tests => "xUnit test project (opt-in)",
        HostKind.NUnit => "NUnit test project (opt-in)",
        HostKind.Repl => "Interactive C# REPL host with highlighting and completion (opt-in)",
        HostKind.WinForms => "WinForms host embedding the game window (Windows-only; never part of the default set)",
        HostKind.WinUi => "WinUI 3 host embedding the game window (Windows-only, like --winforms; builds only on Windows)",
        HostKind.Avalonia => "Avalonia host embedding the game in a cross-platform GUI (opt-in, like --winforms)",
        HostKind.Blazor => "Blazor Web App host: ASP.NET Core server plus a WebAssembly client page embedding the " +
                           "game (opt-in; needs wasm-tools)",
        HostKind.Android => "Android host publishing an APK (experimental, opt-in; needs the android workload)",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>How the interactive picker groups the kinds.</summary>
    public static HostGroup Group(HostKind kind) => kind switch
    {
        HostKind.Desktop or HostKind.WinForms or HostKind.WinUi or HostKind.Avalonia or HostKind.Repl => HostGroup.Desktop,
        HostKind.Web or HostKind.WebXr or HostKind.Blazor => HostGroup.Browser,
        HostKind.Android => HostGroup.Mobile,
        HostKind.Tests or HostKind.NUnit => HostGroup.Testing,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
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
    /// Reduce a name to a stem usable as folder, assembly name AND C# namespace, so the scaffolded files agree
    /// with each other: whitespace and punctuation are dropped, '-' becomes '_' (a name like GWJ-97 would
    /// otherwise land in 'namespace GWJ-97.Tests'), a digit-leading or keyword '.'-segment gets a '_' prefix,
    /// and a name Godot reserves for its own assemblies gets the '_' suffix Godot itself would apply. Null when
    /// nothing usable remains: '.' and '..' would otherwise write outside the project.
    /// </summary>
    public static string? SanitizeName(string? name)
    {
        if (name == null) return null;
        var segments = name.Split('.')
            .Select(segment => new string(segment
                .Select(c => c == '-' ? '_' : c)
                .Where(c => char.IsLetterOrDigit(c) || c == '_')
                .ToArray()))
            .Where(segment => segment.Length > 0)
            .Select(IdentifierSegment)
            .ToList();
        if (!segments.Any(segment => segment.Any(char.IsLetterOrDigit))) return null;
        var stem = string.Join(".", segments);
        return GodotReservedAssemblyNames.Contains(stem) ? stem + "_" : stem;
    }

    /// <summary>
    /// The C# namespace for a base name the project dictates (an existing assembly_name / csproj): every
    /// character that cannot appear in an identifier becomes '_', digit-leading and keyword segments get a '_'
    /// prefix. The same transform dotnet new applies to its sourceName in file contents (its safe_namespace
    /// form), plus the keyword guard dotnet new lacks.
    /// </summary>
    public static string NamespaceName(string baseName)
    {
        var segments = baseName.Split('.')
            .Select(segment => new string(segment.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray()))
            .Where(segment => segment.Length > 0)
            .Select(IdentifierSegment);
        return string.Join(".", segments);
    }

    /// <summary>
    /// The base name as the last part of an Android application id: lowercase, characters other than letters,
    /// digits and '_' become '_', empty segments (repeated or edge dots) are dropped, and a segment that does not
    /// start with a letter gets an 'app' prefix (Android requires each segment to). The template's derived
    /// 'androidName' symbol applies the same rules.
    /// </summary>
    public static string AndroidPackageName(string baseName)
    {
        var segments = baseName.ToLowerInvariant().Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => System.Text.RegularExpressions.Regex.Replace(segment, "[^a-z0-9_]", "_"))
            .Select(segment => segment[0] is >= 'a' and <= 'z' ? segment : "app" + segment);
        return string.Join(".", segments) is { Length: > 0 } name ? name : "app";
    }

    /// <summary>A '.'-segment that C# accepts as a plain identifier: 'namespace event.Tests;' does not parse.</summary>
    private static string IdentifierSegment(string segment) =>
        char.IsDigit(segment[0]) || CSharpKeywords.Contains(segment) ? "_" + segment : segment;

    /// <summary>Godot appends '_' to these (modules/mono/utils/path_utils.cpp); the csproj name must match.</summary>
    private static readonly HashSet<string> GodotReservedAssemblyNames =
        ["GodotSharp", "GodotSharpEditor", "Godot.SourceGenerators"];

    /// <summary>The reserved keywords; contextual ones (var, record, async, ...) are valid identifiers.</summary>
    private static readonly HashSet<string> CSharpKeywords =
    [
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
        "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
        "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
        "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
        "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
    ];
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

        var isTwoDog = Package("2dog.engine") || Package("2dog.repl") || Package("2dog.xunit") || Package("2dog.nunit") || Package("2dog.avalonia")
                       || Property("TwoDogBlazor") != null || Property("GodotProjectDir") != null;
        if (!isTwoDog) return null;

        if (Package("2dog.repl")) return HostKind.Repl;
        if (PropertyTrue("TwoDogBlazor")) return HostKind.Blazor;
        if (PropertyTrue("TwoDogWebXR")) return HostKind.WebXr;
        if ((Property("RuntimeIdentifier") ?? "").Contains("browser-wasm", StringComparison.OrdinalIgnoreCase)
            || (Property("RuntimeIdentifiers") ?? "").Contains("browser-wasm", StringComparison.OrdinalIgnoreCase)
            || sdk.Contains("BlazorWebAssembly", StringComparison.OrdinalIgnoreCase)) return HostKind.Web;
        if (Package("2dog.xunit") || packages.Any(p => p.StartsWith("xunit", StringComparison.OrdinalIgnoreCase)))
            return HostKind.Tests;
        if (Package("2dog.nunit") || Package("NUnit")) return HostKind.NUnit;
        if (PropertyTrue("UseWindowsForms")) return HostKind.WinForms;
        if (PropertyTrue("UseWinUI")) return HostKind.WinUi;
        // Before the Desktop OutputType check: Android and Avalonia hosts are Exe/WinExe too.
        if (Package("2dog.android")
            || (Property("TargetFramework") ?? "").Contains("-android", StringComparison.OrdinalIgnoreCase)
            || (Property("TargetFrameworks") ?? "").Contains("-android", StringComparison.OrdinalIgnoreCase))
            return HostKind.Android;
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
                       || csproj.Contains("2dog.repl", StringComparison.OrdinalIgnoreCase)
                       || csproj.Contains("2dog.xunit", StringComparison.OrdinalIgnoreCase)
                       || csproj.Contains("2dog.nunit", StringComparison.OrdinalIgnoreCase)
                       || csproj.Contains("2dog.avalonia", StringComparison.OrdinalIgnoreCase)
                       || csproj.Contains("<TwoDogBlazor", StringComparison.OrdinalIgnoreCase)
                       || csproj.Contains("<GodotProjectDir>", StringComparison.OrdinalIgnoreCase);
        if (!isTwoDog) return null;

        if (csproj.Contains("2dog.repl", StringComparison.OrdinalIgnoreCase)) return HostKind.Repl;

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
        if (csproj.Contains("2dog.nunit", StringComparison.OrdinalIgnoreCase)
            || System.Text.RegularExpressions.Regex.IsMatch(csproj, @"Include\s*=\s*[""']NUnit[""']",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return HostKind.NUnit;
        if (csproj.Contains("UseWindowsForms", StringComparison.OrdinalIgnoreCase)) return HostKind.WinForms;
        if (csproj.Contains("UseWinUI", StringComparison.OrdinalIgnoreCase)) return HostKind.WinUi;
        // Before the Desktop OutputType check: Android and Avalonia hosts are Exe/WinExe too.
        if (csproj.Contains("2dog.android", StringComparison.OrdinalIgnoreCase)
            || System.Text.RegularExpressions.Regex.IsMatch(csproj, @"<TargetFrameworks?>[^<]*-android",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return HostKind.Android;
        // 2dog.avalonia catches scaffolded hosts, Avalonia.Desktop hand-rolled ones.
        if (csproj.Contains("2dog.avalonia", StringComparison.OrdinalIgnoreCase)
            || csproj.Contains("Avalonia.Desktop", StringComparison.OrdinalIgnoreCase)) return HostKind.Avalonia;
        if (csproj.Contains("<OutputType>Exe</OutputType>", StringComparison.OrdinalIgnoreCase)
            || csproj.Contains("<OutputType>WinExe</OutputType>", StringComparison.OrdinalIgnoreCase)) return HostKind.Desktop;

        return BySuffix(folder);
    }

    /// <summary>Wired to a Godot project but unrecognizable otherwise: the folder suffix decides, default generic.</summary>
    private static HostKind BySuffix(string folder) =>
        folder.EndsWith(".tests", StringComparison.OrdinalIgnoreCase) ? HostKind.Tests : Hosts.All.FirstOrDefault(
            k => folder.EndsWith("." + Hosts.Suffix(k), StringComparison.OrdinalIgnoreCase),
            HostKind.Desktop);
}
