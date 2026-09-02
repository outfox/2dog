using System.Text.RegularExpressions;

namespace twodog.cli;

/// <summary>project.godot settings the hosts depend on.</summary>
internal static class GodotSettingsChecks
{
    public static readonly CheckInfo[] Checks =
    [
        new("godot.features-line", Category.GodotSettings, "config/features names the Godot line the SDK targets"),
        new("godot.xr-shaders", Category.GodotSettings, "a WebXR host has xr/shaders/enabled.web set"),
        new("godot.import-stamp", Category.GodotSettings, "the project has been imported at least once"),
    ];

    public static IEnumerable<Finding> Run(DoctorContext ctx)
    {
        const Category c = Category.GodotSettings;
        var p = ctx.Project;
        if (p.Godot is not { } godot) yield break;

        var features = godot.Get("application", "config/features");
        var sdk = p.GameCsprojText is { } text ? VersionRewriter.GodotSdkVersion(text) : null;
        if (features != null && sdk != null && Regex.Match(features, @"""(?<line>\d+\.\d+)""") is { Success: true } m
            && Version.TryParse(sdk, out var sdkVersion))
        {
            var line = m.Groups["line"].Value;
            if (line != $"{sdkVersion.Major}.{sdkVersion.Minor}")
                yield return new Finding("godot.features-line", c, Severity.Warn,
                    $"project.godot config/features says Godot {line} but the game uses Godot.NET.Sdk/{sdk}",
                    "the editor rewrites it on open", $"open the project once in the Godot {sdkVersion.Major}.{sdkVersion.Minor} .NET editor", "project.godot");
            else
                yield return Finding.Pass("godot.features-line", c, $"Godot {line}");
        }

        if (p.Hosts.Any(h => h.Kind == HostKind.WebXr))
        {
            if (godot.Get("xr", "shaders/enabled.web") is "true")
                yield return Finding.Pass("godot.xr-shaders", c, "xr shaders");
            else
                yield return new Finding("godot.xr-shaders", c, Severity.Warn, "project.godot lacks [xr] shaders/enabled.web=true",
                    "the WebXR host renders nothing without it", null, "project.godot",
                    new Fix("godot:xr-shaders", FixClass.Safe, "set [xr] shaders/enabled.web=true in project.godot",
                        () => godot.Set("xr", "shaders/enabled.web", "true", raw: true)));
        }

        if (p.Hosts.Count > 0 && !File.Exists(Path.Combine(p.Dir, ".godot", "2dog.import.stamp")))
            yield return new Finding("godot.import-stamp", c, Severity.Info, "not imported yet (the next build imports the project)");
    }
}
