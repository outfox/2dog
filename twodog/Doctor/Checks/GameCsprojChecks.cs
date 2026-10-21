namespace twodog.cli;

/// <summary>The game csproj: SDK line, target framework, the properties and includes the hosts rely on.</summary>
internal static class GameCsprojChecks
{
    public static readonly CheckInfo[] Checks =
    [
        new("game.sdk", Category.GameProject, "the game csproj uses Godot.NET.Sdk"),
        new("game.sdk-mismatch", Category.GameProject, "Godot.NET.Sdk matches the Godot line 2dog.engine was built for"),
        new("game.target-framework", Category.GameProject, "the game csproj targets net10.0"),
        new("game.properties", Category.GameProject, "EnableDynamicLoading and AllowUnsafeBlocks are set"),
        new("game.bindings", Category.GameProject, "the game csproj references 2dog.godotsharp"),
        new("game.default-item-excludes", Category.GameProject, "every host folder is excluded from the game's default globs"),
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

        CsprojPatcher.Result safe, full;
        try
        {
            safe = CsprojPatcher.Patch(csproj, hostFolders, upgradeTargetFramework: false);
            full = CsprojPatcher.Patch(csproj, hostFolders);
        }
        catch (System.Xml.XmlException)
        {
            yield break;
        }

        // Patches are recomputed when applied, so the safe and the announced one compose in either order.
        void ApplyPatch(bool upgradeTargetFramework)
        {
            if (CsprojPatcher.Patch(csproj, hostFolders, upgradeTargetFramework: upgradeTargetFramework).NewContent is { } content)
                MsBuildXml.Write(csproj, content);
        }

        var safeFix = safe.NewContent != null
            ? new Fix($"patch:{name}", FixClass.Safe, $"patch {name} ({string.Join("; ", safe.Added)})", () => ApplyPatch(false))
            : null;

        var tfm = full.Added.FirstOrDefault(a => a.StartsWith("TargetFramework", StringComparison.Ordinal));
        if (tfm != null)
            yield return new Finding("game.target-framework", c, Severity.Warn, $"{name} does not target net10.0",
                "2dog packages are net10.0", null, name,
                new Fix($"patch:{name}:tfm", FixClass.Announced, $"upgrade {name} to net10.0 (patches the csproj: {string.Join("; ", full.Added)})",
                    () => ApplyPatch(true)));
        else
            yield return Finding.Pass("game.target-framework", c, "net10.0");

        var properties = safe.Added.Where(a => a is "EnableDynamicLoading" or "AllowUnsafeBlocks").ToList();
        if (properties.Count > 0)
            yield return new Finding("game.properties", c, Severity.Warn, $"{name} lacks {string.Join(", ", properties)}",
                "hosts load the game assembly dynamically; web hosts need the generated TwoDogWebBoot, which requires unsafe code", null, name, safeFix);

        // The patcher only adds what is absent; a property set to anything but true is the author's edit to undo.
        var disabled = new[] { "EnableDynamicLoading", "AllowUnsafeBlocks" }
            .Where(property => p.GameCsproj.Descendants().Where(e => e.Name.LocalName == property).Select(e => e.Value.Trim())
                .Any(v => !v.Contains("$(") && !v.Equals("true", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (disabled.Count > 0)
            yield return new Finding("game.properties", c, Severity.Warn, $"{name} sets {string.Join(", ", disabled)} to something other than true",
                "hosts load the game assembly dynamically; web hosts need the generated TwoDogWebBoot, which requires unsafe code", $"set {string.Join(" and ", disabled)} to true", name);

        if (properties.Count == 0 && disabled.Count == 0)
            yield return Finding.Pass("game.properties", c, "properties");

        if (safe.Added.FirstOrDefault(a => a.StartsWith("DefaultItemExcludes", StringComparison.Ordinal)) is { } excludes)
            yield return new Finding("game.default-item-excludes", c, Severity.Warn,
                $"{name} {excludes} (host sources would compile into the game)", null, null, name, safeFix);
        else if (hostFolders.Count > 0)
            yield return Finding.Pass("game.default-item-excludes", c, "host excludes");

        // Web hosts call TwoDogWebBoot, which only the fork's source generator emits.
        if (safe.Added.Contains(CsprojPatcher.BindingsAdded))
            yield return new Finding("game.bindings", c, p.Hosts.Any(h => h.IsWebLike) ? Severity.Fail : Severity.Warn,
                $"{name} does not reference 2dog.godotsharp",
                "hosts expect the fork's bindings, and web hosts the TwoDogWebBoot its source generator emits", "2dog update", name, safeFix);
        else
            yield return Finding.Pass("game.bindings", c, "2dog.godotsharp");
    }
}
