using System.Text.RegularExpressions;

namespace twodog.Hosting.Xunit;

/// <summary>
/// Per-instance scratch Godot projects: each engine instance gets its own copy
/// (no .godot/ contention) with a forced per-instance user:// dir (Godot
/// derives user:// from application/config/name or custom_user_dir_name -
/// shared values would collide across instances).
/// </summary>
public static partial class ScratchProject
{
    /// <summary>
    /// Creates a scratch project for <paramref name="tag"/> and returns its path.
    /// With <paramref name="sourceProjectDir"/> the whole project tree is copied
    /// (including .godot import artifacts; import lock files are skipped) and
    /// project.godot gets the custom user dir FORCED - existing
    /// use_custom_user_dir/custom_user_dir_name values are overwritten.
    /// Without, a minimal headless project (empty root scene) is generated.
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
            CopyTree(Path.GetFullPath(sourceProjectDir), dir);
            ForceUserDir(Path.Combine(dir, "project.godot"), tag);
        }

        // The empty import cache an editor would produce (avoids a boot error print).
        Directory.CreateDirectory(Path.Combine(dir, ".godot"));
        var cache = Path.Combine(dir, ".godot", "global_script_class_cache.cfg");
        if (!File.Exists(cache)) File.WriteAllText(cache, "list=[]\n");
        return dir;
    }

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
        {
            if (Path.GetFileName(file) == "2dog.import.lock") continue;
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        }
        foreach (var sub in Directory.GetDirectories(from))
            CopyTree(sub, Path.Combine(to, Path.GetFileName(sub)));
    }

    private static void ForceUserDir(string projectGodot, string tag)
    {
        if (!File.Exists(projectGodot))
            throw new FileNotFoundException("Source project has no project.godot.", projectGodot);
        var text = File.ReadAllText(projectGodot);
        text = UseCustomUserDirLine().Replace(text, "");
        text = CustomUserDirNameLine().Replace(text, "");
        const string section = "[application]";
        var patch = $"""
                     {section}
                     config/use_custom_user_dir=true
                     config/custom_user_dir_name="2dog-hosting/{tag}"
                     """;
        text = text.Contains(section) ? text.Replace(section, patch) : $"{text}\n{patch}\n";
        File.WriteAllText(projectGodot, text);
    }

    [GeneratedRegex(@"^config/use_custom_user_dir=.*\r?\n?", RegexOptions.Multiline)]
    private static partial Regex UseCustomUserDirLine();

    [GeneratedRegex(@"^config/custom_user_dir_name=.*\r?\n?", RegexOptions.Multiline)]
    private static partial Regex CustomUserDirNameLine();
}
