namespace twodog.cli;

/// <summary>Package versions: consistent, current, and on the same Godot line as the SDK.</summary>
internal static class VersionChecks
{
    public static readonly CheckInfo[] Checks =
    [
        new("ver.managed-elsewhere", Category.Versions, "versions expressed through your own properties are noted, not judged"),
        new("ver.props-invalid", Category.Versions, "the TwoDog* versions in Directory.Build.props are readable versions"),
        new("ver.custom-versions", Category.Versions, "custom versions receive best-effort checks without public release upgrade advice"),
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

    public static IEnumerable<Finding> Run(DoctorContext ctx) => Run(ctx, ToolVersions.TwoDogVersion, ToolVersions.NativesVersion);

    internal static IEnumerable<Finding> Run(DoctorContext ctx, string toolVersion, string toolNativesVersion)
    {
        const Category c = Category.Versions;
        var p = ctx.Project;
        // Every host csproj plus the Blazor client nested inside its host (In names the folder for messages).
        var refs = p.Hosts.SelectMany(h => h.Packages.Select(r => (In: h.Folder, Ref: r)))
            .Concat(p.Hosts.Where(h => h.ClientText != null)
                .SelectMany(h => ClientReferences(h.ClientText!).Select(r => (In: $"{h.Folder}/Client", Ref: r))))
            .Concat(p.GameCsprojText is { } gameText ? VersionRewriter.References(gameText).Select(r => (In: p.GameCsprojName!, Ref: r)) : [])
            .ToList();

        var foreign = refs.Where(x => x.Ref.IsProperty && !x.Ref.RawVersion.Contains("$(TwoDog")).Select(x => x.Ref.RawVersion).Distinct().ToList();
        if (foreign.Count > 0)
            yield return new Finding("ver.managed-elsewhere", c, Severity.Info,
                $"versions come from your own properties ({string.Join(", ", foreign.Take(3))}) - not checked");

        var malformed = PropsPatcher.ToolValues.Select(v => v.Name)
            .Where(n => p.PropsValues.TryGetValue(n, out var v) && !v.Contains("$(") && ToolVersions.Floor(v.Trim('[', ']').Split('-', '+')[0]) == null)
            .Select(n => $"{n}='{p.PropsValues[n]}'").ToList();
        if (malformed.Count > 0)
            yield return new Finding("ver.props-invalid", c, Severity.Fail,
                $"{PropsPatcher.FileName} has unreadable version(s): {string.Join(", ", malformed)}",
                "restore fails on them and the version checks cannot read them",
                $"fix the value in {PropsPatcher.FileName} (2dog update rewrites the block)", PropsPatcher.FileName);

        var sdkVersion = p.GameCsprojText is { } sdkText ? VersionRewriter.GodotSdkVersion(sdkText) : null;
        var custom = new[] { "TwoDogVersion", "TwoDogNativesVersion", "TwoDogGodotVersion" }
            .Where(p.PropsValues.ContainsKey).Select(name => p.PropsValues[name])
            .Concat(refs.Where(x => VersionRewriter.IsTwoDogPackage(x.Ref.Id) || VersionRewriter.IsPinnedPackage(x.Ref.Id)
                                   || x.Ref.Id.Equals("GodotSharpEditor", StringComparison.OrdinalIgnoreCase)).Select(x => x.Ref.RawVersion))
            .Concat(new[] { toolVersion, toolNativesVersion, ToolVersions.GodotSdkVersion, sdkVersion }.OfType<string>())
            .Where(v => !v.Contains("$(") && v.IndexOfAny(['-', '+']) >= 0)
            .Select(v => v.Trim('[', ']')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (custom.Count > 0)
            yield return new Finding("ver.custom-versions", c, Severity.Warn,
                $"custom/prerelease versions: {string.Join(", ", custom)}",
                "version checks are best effort; matching labels do not prove binary compatibility, and public release upgrade comparisons are skipped",
                "use matching engine, native libraries, managed bindings and GDExtensions from your fork; verify package source mapping");

        // Keep suffix handling local to doctor: update must not mistake a fork for its numeric release version.
        var literals = refs.Where(x => !x.Ref.IsProperty && VersionRewriter.PropertyFor(x.Ref.Id) != null
                                       && NumericVersion(x.Ref.RawVersion) != null).ToList();
        if (literals.Count > 0)
            yield return new Finding("ver.literal-versions", c, Severity.Warn,
                $"{literals.Count} literal package version(s) in {literals.Select(x => x.In).Distinct().Count()} host csproj(s)",
                "shared version properties keep every host aligned", custom.Count > 0 ? "keep your custom versions when moving them into Directory.Build.props" : "2dog update");

        var twoDog = literals.Where(x => VersionRewriter.IsTwoDogPackage(x.Ref.Id)).Select(x => x.Ref.RawVersion.Trim('[', ']'))
            .Concat(p.PropsValues.TryGetValue("TwoDogVersion", out var propsValue) ? [propsValue.Trim('[', ']')] : [])
            .Where(v => NumericVersion(v) != null).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(NumericVersion).ToList();
        var tool = NumericVersion(toolVersion);
        if (twoDog.Count > 1)
            yield return new Finding("ver.twodog-consistent", c, Severity.Fail,
                $"2dog packages at mixed versions: {string.Join(", ", twoDog)}", "hosts must share one engine version",
                custom.Count > 0 ? "align the full package versions, including custom suffixes" : "2dog update");
        else if (custom.Count == 0 && twoDog.Count == 1 && tool != null && NumericVersion(twoDog[0]) < tool)
            yield return new Finding("ver.twodog-outdated", c, Severity.Warn, $"2dog packages {twoDog[0]} -> {tool} available", null, "2dog update");
        else if (custom.Count == 0 && twoDog.Count == 1 && tool != null && NumericVersion(twoDog[0]) > tool)
            yield return new Finding("ver.twodog-newer", c, Severity.Info, $"2dog packages {twoDog[0]} are newer than this tool ({tool})",
                null, "run the newest tool: dnx 2dog doctor");
        else if (custom.Count == 0 && twoDog.Count == 1 && tool != null)
            yield return Finding.Pass("ver.twodog-outdated", c, $"2dog {tool}");

        var nativeVersions = literals.Where(x => VersionRewriter.PropertyFor(x.Ref.Id) == "TwoDogNativesVersion")
            .Select(x => x.Ref.RawVersion.Trim('[', ']'))
            .Concat(p.PropsValues.TryGetValue("TwoDogNativesVersion", out var nativeValue) ? [nativeValue.Trim('[', ']')] : [])
            .Where(v => NumericVersion(v) != null).OrderBy(NumericVersion).ToList();
        var toolNatives = NumericVersion(toolNativesVersion);
        if (nativeVersions.LastOrDefault() is { } nativeVersion && twoDog.Count > 0)
        {
            var natives = NumericVersion(nativeVersion)!;
            var engine = NumericVersion(twoDog[^1])!;
            if (natives.Major != engine.Major || natives.Minor != engine.Minor || natives.Build != engine.Build)
                yield return new Finding("ver.natives", c, Severity.Fail, $"natives {nativeVersion} are not on the engine's Godot line ({twoDog[^1]})",
                    "the native payloads and the engine must come from the same Godot build",
                    custom.Count > 0 ? "select matching native packages from your fork" : "2dog update");
            else if (refs.FirstOrDefault(x => VersionRewriter.IsPinnedPackage(x.Ref.Id) && !x.Ref.IsPinned
                                              && (!x.Ref.IsProperty || x.Ref.RawVersion.Contains("$(TwoDog"))) is { Ref: { } floating })
                yield return new Finding("ver.natives", c, Severity.Warn, $"{floating.Id} is not exact-pinned ([version] or [$(TwoDogNativesVersion)])",
                    "a floating reference can drift away from the engine's build",
                    custom.Count > 0 ? "exact-pin the custom native version with [version] or [$(TwoDogNativesVersion)]" : "2dog update");
            else if (custom.Count == 0 && engine == tool && toolNatives != null && EnvironmentChecks.Normalize(natives) != EnvironmentChecks.Normalize(toolNatives))
                yield return new Finding("ver.natives", c, Severity.Warn, $"natives {natives} -> {toolNativesVersion} available", null, "2dog update");
            else
                yield return Finding.Pass("ver.natives", c, $"natives [{nativeVersion}]");
        }

        if (p.PropsValues.TryGetValue("TwoDogGodotVersion", out var godotProp) && p.GameCsprojText is { } game
            && VersionRewriter.GodotSdkVersion(game) is { } sdk)
        {
            if (godotProp != sdk)
                yield return new Finding("ver.godot-line-consistent", c, Severity.Fail,
                    $"Directory.Build.props says TwoDogGodotVersion {godotProp} but {p.GameCsprojName} uses Godot.NET.Sdk/{sdk}",
                    "the test host's GodotSharpEditor follows the property", custom.Count > 0 ? "align TwoDogGodotVersion and the game's Godot.NET.Sdk version" : "2dog update");
            else
                yield return Finding.Pass("ver.godot-line-consistent", c, $"Godot.NET.Sdk {sdk}");
        }

        foreach (var (folder, r) in literals.Where(x => x.Ref.Id.Equals("GodotSharpEditor", StringComparison.OrdinalIgnoreCase)))
        {
            var gameSdk = p.GameCsprojText is { } text ? VersionRewriter.GodotSdkVersion(text) : null;
            if (gameSdk != null && r.RawVersion.Trim('[', ']') != gameSdk)
                yield return new Finding("ver.godotsharp-editor", c, Severity.Warn,
                    $"{folder} references GodotSharpEditor {r.RawVersion} but the game uses Godot.NET.Sdk/{gameSdk}", null,
                    custom.Count > 0 ? "select the GodotSharpEditor package matching your custom SDK" : "2dog update");
        }

        foreach (var host in p.Hosts)
        {
            var avalonia = host.Packages.Where(r => r.Id.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase) && r.IsLiteral)
                .Select(r => r.Parsed!).Distinct().ToList();
            if (avalonia.Count > 1)
                yield return new Finding("ver.companions", c, Severity.Warn,
                    $"{host.Folder} mixes Avalonia versions: {string.Join(", ", avalonia)}", "Avalonia packages must match", "align them (2dog update raises the template's set)");
        }

        if (custom.Count == 0 && tool != null && ctx.LatestTool is { } newest && Version.TryParse(newest, out var newestVersion) && newestVersion > tool)
            yield return new Finding("ver.tool-latest", c, Severity.Info, $"a newer 2dog tool exists: {newest}", null, $"dnx 2dog@{newest} doctor");
    }

    private static Version? NumericVersion(string text) =>
        Version.TryParse(text.Trim('[', ']').Split('-', '+')[0], out var version) ? version : null;

    /// <summary>The client csproj's references; an unparseable client is a load problem (ProjectModel parses it), not reported here.</summary>
    private static List<PackageRef> ClientReferences(string text)
    {
        try { return VersionRewriter.References(text); }
        catch (System.Xml.XmlException) { return []; }
    }
}
