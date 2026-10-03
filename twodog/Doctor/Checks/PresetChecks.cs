namespace twodog.cli;

/// <summary>export_presets.cfg: publishes export the pck through named presets.</summary>
internal static class PresetChecks
{
    public static readonly CheckInfo[] Checks =
    [
        new("preset.file", Category.Presets, "export_presets.cfg exists"),
        new("preset.web", Category.Presets, "the 'Web' preset exists when a browser host does"),
        new("preset.android", Category.Presets, "the Android hosts' export preset ('Android' by default) exists"),
        new("preset.desktop", Category.Presets, "the per-OS desktop presets exist"),
    ];

    public static IEnumerable<Finding> Run(DoctorContext ctx)
    {
        const Category c = Category.Presets;
        var p = ctx.Project;
        if (p.Hosts.Count == 0) yield break;
        var path = Path.Combine(p.Dir, ExportPresetOps.FileName);

        if (p.ExportPresetsText is not { } text)
        {
            yield return new Finding("preset.file", c, Severity.Fail, $"{ExportPresetOps.FileName} missing",
                "publishes export the game pck through it (the publish stops without it)", null, ExportPresetOps.FileName,
                new Fix("presets:create", FixClass.Safe, $"create {ExportPresetOps.FileName} (web, desktop and Android export presets)",
                    () => File.WriteAllText(path, TemplateAssets.ExportPresets())));
            yield break;
        }

        var wanted = new List<(string Name, string Id, string Why, Severity Missing)>();
        if (p.HasWebLikeHost)
            wanted.Add((ExportPresetOps.WebPresetName, "preset.web", "web publish exports the pck through it", Severity.Fail));
        // A host that ships a pre-exported pck (TwoDogAndroidPack) never exports; an unevaluable name is skipped.
        foreach (var name in p.Hosts.Where(h => h.Kind == HostKind.Android && !h.HasProperty("TwoDogAndroidPack"))
                     .Select(h => h.Property("TwoDogAndroidExportPreset") ?? ExportPresetOps.AndroidPresetName)
                     .Where(name => !name.Contains("$(")).Distinct())
            wanted.Add((name, "preset.android", "Android builds export the pck through it", Severity.Fail));
        var hostOs = ctx.Env.IsWindows ? "Windows Desktop" : ctx.Env.IsMacOS ? "macOS" : "Linux";
        foreach (var name in ExportPresetOps.DesktopPresetNames)
            wanted.Add((name, "preset.desktop", $"desktop publish for {name} exports the pck through it",
                name == hostOs ? Severity.Fail : Severity.Warn));

        var present = new List<string>();
        foreach (var (name, id, why, severity) in wanted)
        {
            if (ExportPresetOps.HasPreset(text, name))
            {
                present.Add(name);
                continue;
            }

            // Only the template's presets can be appended; a custom preset name has to be added in Godot.
            var fromTemplate = name == ExportPresetOps.WebPresetName || name == ExportPresetOps.AndroidPresetName
                               || ExportPresetOps.DesktopPresetNames.Contains(name);
            yield return new Finding(id, c, severity, $"'{name}' export preset missing", why,
                fromTemplate ? null : "add it in Godot (Project > Export)", ExportPresetOps.FileName,
                fromTemplate
                    ? new Fix($"preset:{name}", FixClass.Safe, $"append '{name}' export preset to {ExportPresetOps.FileName}",
                        () => File.AppendAllText(path, ExportPresetOps.AppendText(File.ReadAllText(path), name)))
                    : null);
        }

        if (present.Count > 0) yield return Finding.Pass("preset.file", c, string.Join(", ", present));
    }
}
