namespace twodog.Hosting.Xunit;

/// <summary>
/// Per-instance scratch Godot projects: each engine instance gets its own copy
/// (no .godot/ contention) with a distinct application name (no user://
/// collision - Godot derives user:// from application/config/name).
/// </summary>
public static class ScratchProject
{
    /// <summary>
    /// Creates a scratch project for <paramref name="tag"/> and returns its path.
    /// With <paramref name="sourceProjectDir"/> the source files are copied
    /// (top-level only) and project.godot gets a custom user dir patched in;
    /// without, a minimal headless project (empty root scene) is generated.
    /// </summary>
    public static string Create(string tag, string? sourceProjectDir = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "2dog-hosting", Environment.ProcessId.ToString(), tag);
        Directory.CreateDirectory(dir);

        if (sourceProjectDir is null)
        {
            File.WriteAllText(Path.Combine(dir, "project.godot"),
                $"""
                 ; Generated scratch project for the 2dog hosting fixture.
                 config_version=5

                 [application]
                 config/name="2dog-{tag}"
                 run/main_scene="res://main.tscn"
                 """);
            File.WriteAllText(Path.Combine(dir, "main.tscn"),
                """
                [gd_scene format=3]

                [node name="Main" type="Node"]
                """);
        }
        else
        {
            foreach (var file in Directory.GetFiles(sourceProjectDir))
                File.Copy(file, Path.Combine(dir, Path.GetFileName(file)), overwrite: true);
            PatchUserDir(Path.Combine(dir, "project.godot"), tag);
        }

        // The empty import cache an editor would produce (avoids a boot error print).
        Directory.CreateDirectory(Path.Combine(dir, ".godot"));
        File.WriteAllText(Path.Combine(dir, ".godot", "global_script_class_cache.cfg"), "list=[]\n");
        return dir;
    }

    private static void PatchUserDir(string projectGodot, string tag)
    {
        var text = File.ReadAllText(projectGodot);
        if (text.Contains("use_custom_user_dir")) return;
        const string section = "[application]";
        var patch = $"""
                     config/use_custom_user_dir=true
                     config/custom_user_dir_name="2dog-hosting/{tag}"
                     """;
        text = text.Contains(section)
            ? text.Replace(section, $"{section}\n{patch}")
            : $"{text}\n{section}\n{patch}\n";
        File.WriteAllText(projectGodot, text);
    }
}
