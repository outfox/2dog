using System.Text.RegularExpressions;

namespace twodog.cli;

/// <summary>
/// export_presets.cfg handling: publishes export the pck via presets ('Web', per-OS desktop names) and the engine
/// refuses to export without the file. Append-only: a missing preset is appended under the next free index.
/// </summary>
internal static class ExportPresetOps
{
    public const string FileName = "export_presets.cfg";
    public const string WebPresetName = "Web";

    /// <summary>Godot's standard desktop preset names, as the desktop publish's RID mapping expects them.</summary>
    public static readonly string[] DesktopPresetNames = ["Windows Desktop", "Linux", "macOS"];

    /// <summary>Whether the cfg text defines a preset with the given name.</summary>
    public static bool HasPreset(string cfgText, string presetName) =>
        Regex.IsMatch(cfgText, $@"^name=""{Regex.Escape(presetName)}""\s*$", RegexOptions.Multiline);

    /// <summary>
    /// The template's section for the named preset, renumbered past every [preset.N] already in the file, ready to
    /// append (leading separator included).
    /// </summary>
    public static string AppendText(string existingCfgText, string presetName)
    {
        var next = Regex.Matches(existingCfgText, @"^\[preset\.(\d+)\]\s*$", RegexOptions.Multiline)
            .Select(m => int.Parse(m.Groups[1].Value))
            .DefaultIfEmpty(-1).Max() + 1;
        var (section, index) = TemplateSection(presetName);
        section = section.TrimEnd() + Environment.NewLine;
        return Environment.NewLine + section
            .Replace($"[preset.{index}.options]", $"[preset.{next}.options]")
            .Replace($"[preset.{index}]", $"[preset.{next}]");
    }

    /// <summary>
    /// Extracts the named preset's [preset.N] and [preset.N.options] blocks from the template's export_presets.cfg.
    /// </summary>
    private static (string Section, int Index) TemplateSection(string presetName)
    {
        var template = TemplateAssets.ExportPresets();
        var headers = Regex.Matches(template, @"^\[preset\.(\d+)\]\s*$", RegexOptions.Multiline);
        foreach (Match header in headers)
        {
            var end = headers.Where(m => m.Index > header.Index)
                .Select(m => m.Index).DefaultIfEmpty(template.Length).Min();
            var section = template.Substring(header.Index, end - header.Index);
            if (HasPreset(section, presetName))
                return (section, int.Parse(header.Groups[1].Value));
        }

        throw new InvalidOperationException(
            $"The template's {FileName} defines no '{presetName}' preset.");
    }
}
