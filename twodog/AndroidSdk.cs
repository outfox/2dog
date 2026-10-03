namespace twodog.cli;

/// <summary>A read-only SDK probe, separate from the .NET android workload.</summary>
internal static class AndroidSdk
{
    public const string SetupUrl = "https://learn.microsoft.com/en-us/dotnet/android/getting-started/installation/dependencies";

    internal sealed record Inspection(string? Directory, IReadOnlyList<string> Warnings);

    public static Inspection Inspect(IEnvironment env)
    {
        var warnings = new List<string>();
        var configured = new[] { "AndroidSdkDirectory", "ANDROID_HOME", "ANDROID_SDK_ROOT" }
            .Select(name => (Name: name, Path: env.Var(name)))
            .Where(v => !string.IsNullOrWhiteSpace(v.Path))
            .Select(v => (v.Name, Path: v.Path!)).ToList();
        string? sdk = null;

        foreach (var (name, path) in configured)
        {
            if (Problem(env, path) is { } problem)
                warnings.Add($"{name} points at an Android SDK that {problem}: {path}. " +
                             "Please check this setting before publishing the Android host.");
            else
                sdk ??= path;
        }

        var home = configured.FirstOrDefault(v => v.Name == "ANDROID_HOME").Path;
        var root = configured.FirstOrDefault(v => v.Name == "ANDROID_SDK_ROOT").Path;
        if (home != null && root != null && !SamePath(env, home, root))
            warnings.Add("ANDROID_HOME and ANDROID_SDK_ROOT point at different SDK directories. " +
                         "Please make them agree, or unset the deprecated ANDROID_SDK_ROOT before publishing.");

        if (configured.Count > 0) return new Inspection(sdk, warnings);

        string? incomplete = null;
        foreach (var path in DefaultLocations(env))
        {
            if (!env.DirectoryExists(path)) continue;
            if (Problem(env, path) is { } problem)
                incomplete ??= $"The Android SDK at {path} {problem}. " +
                                "Please install the missing components with Android Studio's SDK Manager before publishing.";
            else
                return new Inspection(path, []);
        }

        warnings.Add(incomplete ?? "Could not find an Android SDK: AndroidSdkDirectory, ANDROID_HOME and " +
            "ANDROID_SDK_ROOT are unset, and no SDK was found in the usual install locations. " +
            "Please install the Android SDK and set ANDROID_HOME to its SDK root, or supply AndroidSdkDirectory " +
            "when building. The .NET android workload alone does not provide the Android SDK.");
        return new Inspection(null, warnings);
    }

    private static string? Problem(IEnvironment env, string path)
    {
        if (!Path.IsPathFullyQualified(path)) return "needs an absolute SDK root path";
        if (!env.DirectoryExists(path)) return "does not exist or cannot be read";

        var missing = new List<string>();
        var adb = Path.Combine("platform-tools", env.IsWindows ? "adb.exe" : "adb");
        if (!env.FileExists(Path.Combine(path, adb))) missing.Add(adb);
        foreach (var component in new[] { "platforms", "build-tools" })
            if (!env.DirectoryExists(Path.Combine(path, component))) missing.Add(component);
        return missing.Count > 0 ? $"is missing {string.Join(", ", missing)}" : null;
    }

    private static bool SamePath(IEnvironment env, string a, string b)
    {
        try
        {
            return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
                env.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static IEnumerable<string> DefaultLocations(IEnvironment env)
    {
        if (env.IsWindows)
        {
            foreach (var name in new[] { "LOCALAPPDATA", "ProgramFiles(x86)", "ProgramFiles" })
                if (env.Var(name) is { Length: > 0 } dir)
                    yield return Path.Combine(dir, "Android", name == "LOCALAPPDATA" ? "Sdk" : "android-sdk");
        }
        else if (env.Var("HOME") is { Length: > 0 } home)
        {
            if (env.IsMacOS)
                yield return Path.Combine(home, "Library", "Android", "sdk");
            else
            {
                yield return Path.Combine(home, "Android", "Sdk");
                yield return Path.Combine(home, "android-sdk");
            }
        }
        if (!env.IsWindows && !env.IsMacOS) yield return "/usr/lib/android-sdk";
    }
}
