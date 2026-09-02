namespace twodog.cli;

/// <summary>export_presets.cfg: publishes export the pck through named presets.</summary>
internal static class PresetChecks
{
    public static readonly CheckInfo[] Checks =
    [
        new("preset.file", Category.Presets, "export_presets.cfg exists"),
        new("preset.web", Category.Presets, "the 'Web' preset exists when a browser host does"),
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
                new Fix("presets:create", FixClass.Safe, $"create {ExportPresetOps.FileName} (web + desktop export presets)",
                    () => File.WriteAllText(path, TemplateAssets.ExportPresets())));
            yield break;
        }

        var wanted = new List<(string Name, Severity Missing)>();
        if (p.HasWebLikeHost) wanted.Add((ExportPresetOps.WebPresetName, Severity.Fail));
        var hostOs = ctx.Env.IsWindows ? "Windows Desktop" : ctx.Env.IsMacOS ? "macOS" : "Linux";
        foreach (var name in ExportPresetOps.DesktopPresetNames)
            wanted.Add((name, name == hostOs ? Severity.Fail : Severity.Warn));

        var present = new List<string>();
        foreach (var (name, severity) in wanted)
        {
            if (ExportPresetOps.HasPreset(text, name))
            {
                present.Add(name);
                continue;
            }

            var id = name == ExportPresetOps.WebPresetName ? "preset.web" : "preset.desktop";
            yield return new Finding(id, c, severity, $"'{name}' export preset missing",
                name == ExportPresetOps.WebPresetName ? "web publish exports the pck through it" : $"desktop publish for {name} exports the pck through it",
                null, ExportPresetOps.FileName,
                new Fix($"preset:{name}", FixClass.Safe, $"append '{name}' export preset to {ExportPresetOps.FileName}",
                    () => File.AppendAllText(path, ExportPresetOps.AppendText(File.ReadAllText(path), name))));
        }

        if (present.Count > 0) yield return Finding.Pass("preset.file", c, string.Join(", ", present));
    }
}
