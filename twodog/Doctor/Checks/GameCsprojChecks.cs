namespace twodog.cli;

/// <summary>The game csproj: SDK line, target framework, the properties and includes the hosts rely on.</summary>
internal static class GameCsprojChecks
{
    public static readonly CheckInfo[] Checks =
    [
        new("game.sdk", Category.GameProject, "the game csproj uses Godot.NET.Sdk"),
        new("game.sdk-mismatch", Category.GameProject, "Godot.NET.Sdk matches the Godot line 2dog.engine was built for"),
        new("game.target-framework", Category.GameProject, "the game csproj targets net10.0"),
        new("game.properties", Category.GameProject, "EnableDynamicLoading, AllowUnsafeBlocks and LIBGODOT_ENABLED are set"),
        new("game.default-item-excludes", Category.GameProject, "every host folder is excluded from the game's default globs"),
        new("game.webboot-include", Category.GameProject, "the web bootstrap is compiled into the game assembly"),
        new("game.webboot-duplicate", Category.GameProject, "exactly one TwoDogWebBoot.cs compiles into the game assembly"),
    ];

    public static IEnumerable<Finding> Run(DoctorContext ctx)
    {
        const Category c = Category.GameProject;
        var p = ctx.Project;
        if (p.GameCsprojPath is not { } csproj || p.GameCsprojText is not { } text || p.GameCsproj == null) yield break;
        var name = p.GameCsprojName!;

        var sdk = VersionRewriter.GodotSdkVersion(text);
        var sdkAttribute = (string?)p.GameCsproj.Root?.Attribute("Sdk") ?? "";
        if (!sdkAttribute.StartsWith("Godot.NET.Sdk", StringComparison.OrdinalIgnoreCase))
            yield return new Finding("game.sdk", c, Severity.Fail, $"{name} does not use Godot.NET.Sdk (Sdk=\"{sdkAttribute}\")",
                null, $"set <Project Sdk=\"Godot.NET.Sdk/{ToolVersions.GodotSdkVersion}\">", name);
        else
            yield return Finding.Pass("game.sdk", c, $"Godot.NET.Sdk/{sdk}");

        if (sdk != null && Version.TryParse(sdk, out var sdkVersion) && ctx.Versions.TryGetValue("TwoDogVersion", out var engine))
        {
            var line = new Version(engine.Major, engine.Minor, Math.Max(engine.Build, 0));
            var sdkLine = new Version(sdkVersion.Major, sdkVersion.Minor, Math.Max(sdkVersion.Build, 0));
            if (sdkLine != line)
                yield return new Finding("game.sdk-mismatch", c, Severity.Fail,
                    $"{name} uses Godot.NET.Sdk/{sdk} but 2dog.engine {engine} is built for Godot {line}",
                    "mixed versions crash at runtime (the build stops with the same message)",
                    sdkLine < line ? "2dog update" : $"run the tool that ships Godot {sdkLine}: dnx 2dog@<version> update", name);
            else
                yield return Finding.Pass("game.sdk-mismatch", c, "matches 2dog.engine");
        }

        var hostFolders = p.Hosts.Select(h => h.Folder).ToList();
        var bootFolder = p.LegacyRootWebBoot
            ? null
            : p.Hosts.Where(h => h.IsWebLike).Select(h => h.Folder)
                .FirstOrDefault(f => File.Exists(Path.Combine(p.Dir, f, "TwoDogWebBoot.cs")));
        var bootPath = bootFolder is null ? null : $"{bootFolder}/TwoDogWebBoot.cs";

        CsprojPatcher.Result safe, full;
        try
        {
            safe = CsprojPatcher.Patch(csproj, hostFolders, bootPath, upgradeTargetFramework: false);
            full = CsprojPatcher.Patch(csproj, hostFolders, bootPath);
        }
        catch (System.Xml.XmlException)
        {
            yield break;
        }

        var safeFix = safe.NewContent is { } safeText
            ? new Fix($"patch:{name}", FixClass.Safe, $"patch {name} ({string.Join("; ", safe.Added)})",
                () => File.WriteAllText(csproj, safeText))
            : null;

        var tfm = full.Added.FirstOrDefault(a => a.StartsWith("TargetFramework", StringComparison.Ordinal));
        if (tfm != null)
            yield return new Finding("game.target-framework", c, Severity.Warn, $"{name} does not target net10.0",
                "2dog packages are net10.0", null, name,
                new Fix($"patch:{name}:tfm", FixClass.Announced, $"upgrade {name} to net10.0 (patches the csproj: {string.Join("; ", full.Added)})",
                    () => File.WriteAllText(csproj, full.NewContent!)));
        else
            yield return Finding.Pass("game.target-framework", c, "net10.0");

        var properties = safe.Added.Where(a => a is "EnableDynamicLoading" or "AllowUnsafeBlocks" || a.StartsWith("DefineConstants", StringComparison.Ordinal)).ToList();
        if (properties.Count > 0)
            yield return new Finding("game.properties", c, Severity.Warn, $"{name} lacks {string.Join(", ", properties)}",
                "hosts load the game assembly dynamically and the web bootstrap needs unsafe code", null, name, safeFix);
        else
            yield return Finding.Pass("game.properties", c, "properties");

        if (safe.Added.FirstOrDefault(a => a.StartsWith("DefaultItemExcludes", StringComparison.Ordinal)) is { } excludes)
            yield return new Finding("game.default-item-excludes", c, Severity.Warn,
                $"{name} {excludes} (host sources would compile into the game)", null, null, name, safeFix);
        else if (hostFolders.Count > 0)
            yield return Finding.Pass("game.default-item-excludes", c, "host excludes");

        if (bootPath != null && safe.Added.Any(a => a.StartsWith("Compile Include", StringComparison.Ordinal)))
            yield return new Finding("game.webboot-include", c, Severity.Warn, $"{name} does not compile {bootPath}",
                "the web bootstrap must live in the game assembly", null, name, safeFix);
        else if (bootPath != null)
            yield return Finding.Pass("game.webboot-include", c, "web boot include");

        var copies = p.Hosts.Where(h => h.IsWebLike && File.Exists(Path.Combine(p.Dir, h.Folder, "TwoDogWebBoot.cs"))).Select(h => h.Folder).ToList();
        var unguarded = p.GameCsproj.Descendants()
            .Where(e => e.Name.LocalName == "Compile")
            .Count(e => ((string?)e.Attribute("Include") ?? "").EndsWith("TwoDogWebBoot.cs", StringComparison.OrdinalIgnoreCase)
                        && e.Attribute("Condition") == null);
        if ((p.LegacyRootWebBoot && copies.Count > 0) || unguarded > 1)
            yield return new Finding("game.webboot-duplicate", c, Severity.Fail, "more than one TwoDogWebBoot.cs would compile into the game (CS0101)",
                p.LegacyRootWebBoot ? $"root copy plus {string.Join(", ", copies)}" : $"{unguarded} unconditional Compile includes",
                "keep one copy: delete the extras yourself, or guard the includes with Exists() conditions", name);
    }
}
