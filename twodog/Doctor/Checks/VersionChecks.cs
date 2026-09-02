namespace twodog.cli;

/// <summary>Package versions: consistent, current, and on the same Godot line as the SDK.</summary>
internal static class VersionChecks
{
    public static readonly CheckInfo[] Checks =
    [
        new("ver.managed-elsewhere", Category.Versions, "versions expressed through your own properties are noted, not judged"),
        new("ver.literal-versions", Category.Versions, "host csprojs use the shared version properties, not literals"),
        new("ver.twodog-consistent", Category.Versions, "every 2dog package uses the same version"),
        new("ver.twodog-outdated", Category.Versions, "the 2dog packages are at this tool's version"),
        new("ver.twodog-newer", Category.Versions, "the project is not ahead of this tool"),
        new("ver.natives", Category.Versions, "the native packages are exact-pinned on the engine's Godot line"),
        new("ver.godot-line-consistent", Category.Versions, "TwoDogGodotVersion matches the game project's Godot.NET.Sdk"),
        new("ver.godotsharp-editor", Category.Versions, "GodotSharpEditor in the test host matches the Godot.NET.Sdk"),
        new("ver.companions", Category.Versions, "Avalonia and the other companion packages are not mixed"),
        new("ver.tool-latest", Category.Versions, "a newer 2dog tool on nuget.org is mentioned"),
    ];

    public static IEnumerable<Finding> Run(DoctorContext ctx)
    {
        const Category c = Category.Versions;
        var p = ctx.Project;
        var refs = p.Hosts.SelectMany(h => h.Packages.Select(r => (Host: h, Ref: r))).ToList();

        var foreign = refs.Where(x => x.Ref.IsProperty && !x.Ref.RawVersion.Contains("$(TwoDog")).Select(x => x.Ref.RawVersion).Distinct().ToList();
        if (foreign.Count > 0)
            yield return new Finding("ver.managed-elsewhere", c, Severity.Info,
                $"versions come from your own properties ({string.Join(", ", foreign.Take(3))}) - not checked");

        var literals = refs.Where(x => x.Ref.IsManagedLiteral).ToList();
        if (literals.Count > 0)
            yield return new Finding("ver.literal-versions", c, Severity.Warn,
                $"{literals.Count} literal package version(s) in {literals.Select(x => x.Host.Folder).Distinct().Count()} host csproj(s)",
                "2dog update moves them into Directory.Build.props, so one edit updates every host", "2dog update");

        var twoDog = literals.Where(x => VersionRewriter.IsTwoDogPackage(x.Ref.Id)).Select(x => x.Ref.Parsed).OfType<Version>()
            .Concat(p.PropsValues.TryGetValue("TwoDogVersion", out var propsValue) && Version.TryParse(propsValue, out var pv) ? [pv] : [])
            .Distinct().OrderBy(v => v).ToList();
        var tool = Version.Parse(ToolVersions.TwoDogVersion);
        if (twoDog.Count > 1)
            yield return new Finding("ver.twodog-consistent", c, Severity.Fail,
                $"2dog packages at mixed versions: {string.Join(", ", twoDog)}", "hosts must share one engine version", "2dog update");
        else if (twoDog.Count == 1 && twoDog[0] < tool)
            yield return new Finding("ver.twodog-outdated", c, Severity.Warn, $"2dog packages {twoDog[0]} -> {tool} available", null, "2dog update");
        else if (twoDog.Count == 1 && twoDog[0] > tool)
            yield return new Finding("ver.twodog-newer", c, Severity.Info, $"2dog packages {twoDog[0]} are newer than this tool ({tool})",
                null, "run the newest tool: dnx 2dog doctor");
        else if (twoDog.Count == 1)
            yield return Finding.Pass("ver.twodog-outdated", c, $"2dog {tool}");

        var toolNatives = Version.Parse(ToolVersions.NativesVersion);
        if (ctx.Versions.TryGetValue("TwoDogNativesVersion", out var natives) && twoDog.Count > 0)
        {
            var engine = twoDog[^1];
            if (natives.Major != engine.Major || natives.Minor != engine.Minor || natives.Build != engine.Build)
                yield return new Finding("ver.natives", c, Severity.Fail, $"natives {natives} are not on the engine's Godot line ({engine})",
                    "the browser payload and the engine must come from the same Godot build", "2dog update");
            else if (literals.Any(x => x.Ref.Id.Equals("2dog.browser-wasm", StringComparison.OrdinalIgnoreCase) && !x.Ref.IsPinned))
                yield return new Finding("ver.natives", c, Severity.Warn, "2dog.browser-wasm is not exact-pinned ([version])",
                    "a floating reference can drift away from the engine's build", "2dog update");
            else if (engine == tool && EnvironmentChecks.Normalize(natives) != EnvironmentChecks.Normalize(toolNatives))
                yield return new Finding("ver.natives", c, Severity.Warn, $"natives {natives} -> {ToolVersions.NativesVersion} available", null, "2dog update");
            else
                yield return Finding.Pass("ver.natives", c, $"natives [{natives}]");
        }

        if (p.PropsValues.TryGetValue("TwoDogGodotVersion", out var godotProp) && p.GameCsprojText is { } game
            && VersionRewriter.GodotSdkVersion(game) is { } sdk)
        {
            if (godotProp != sdk)
                yield return new Finding("ver.godot-line-consistent", c, Severity.Fail,
                    $"Directory.Build.props says TwoDogGodotVersion {godotProp} but {p.GameCsprojName} uses Godot.NET.Sdk/{sdk}",
                    "the test host's GodotSharpEditor follows the property", "2dog update");
            else
                yield return Finding.Pass("ver.godot-line-consistent", c, $"Godot.NET.Sdk {sdk}");
        }

        foreach (var (host, r) in literals.Where(x => x.Ref.Id.Equals("GodotSharpEditor", StringComparison.OrdinalIgnoreCase)))
        {
            var gameSdk = p.GameCsprojText is { } text ? VersionRewriter.GodotSdkVersion(text) : null;
            if (gameSdk != null && r.RawVersion != gameSdk)
                yield return new Finding("ver.godotsharp-editor", c, Severity.Warn,
                    $"{host.Folder} references GodotSharpEditor {r.RawVersion} but the game uses Godot.NET.Sdk/{gameSdk}", null, "2dog update");
        }

        foreach (var host in p.Hosts)
        {
            var avalonia = host.Packages.Where(r => r.Id.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase) && r.IsLiteral)
                .Select(r => r.Parsed!).Distinct().ToList();
            if (avalonia.Count > 1)
                yield return new Finding("ver.companions", c, Severity.Warn,
                    $"{host.Folder} mixes Avalonia versions: {string.Join(", ", avalonia)}", "Avalonia packages must match", "align them (2dog update raises the template's set)");
        }

        if (ctx.LatestTool is { } newest && Version.TryParse(newest, out var newestVersion) && newestVersion > tool)
            yield return new Finding("ver.tool-latest", c, Severity.Info, $"a newer 2dog tool exists: {newest}", null, $"dnx 2dog@{newest} doctor");
    }
}
