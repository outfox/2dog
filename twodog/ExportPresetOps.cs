using System.Text.RegularExpressions;

namespace twodog.cli;

/// <summary>
/// export_presets.cfg handling: the web host's publish exports the game pck
/// via an export preset (default 'Web'), and the engine refuses to export a
/// project without an export_presets.cfg at its root. Fresh templates ship
/// the file; conversions must produce it too. Append-only, like
/// project.godot: an existing file is never rewritten - a missing Web preset
/// is appended under the next free preset index.
/// </summary>
internal static class ExportPresetOps
{
    public const string FileName = "export_presets.cfg";
    public const string WebPresetName = "Web";

    /// <summary>Whether the cfg text defines a preset with the given name.</summary>
    public static bool HasPreset(string cfgText, string presetName) =>
        Regex.IsMatch(cfgText, $@"^name=""{Regex.Escape(presetName)}""\s*$", RegexOptions.Multiline);

    /// <summary>
    /// The template's Web preset renumbered past every [preset.N] already in
    /// the file, ready to append (leading separator included).
    /// </summary>
    public static string AppendText(string existingCfgText)
    {
        var next = Regex.Matches(existingCfgText, @"^\[preset\.(\d+)\]\s*$", RegexOptions.Multiline)
            .Select(m => int.Parse(m.Groups[1].Value))
            .DefaultIfEmpty(-1).Max() + 1;
        var section = TemplateAssets.ExportPresets()
            .Replace("[preset.0.options]", $"[preset.{next}.options]")
            .Replace("[preset.0]", $"[preset.{next}]");
        return Environment.NewLine + section;
    }
}
