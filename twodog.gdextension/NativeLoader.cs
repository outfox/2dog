using System.Runtime.InteropServices;

namespace twodog;

/// <summary>
/// Locates and loads the non-mono libgodot native (libgodot-gdext-*), mirroring
/// 2dog.engine's variant model: the 2dog.gdextension.[rid] package targets copy
/// the $(TwoDogVariant)-selected native next to the app; for in-repo
/// development it falls back to godot/bin (extra_suffix=gdext_shared_library,
/// with a legacy pre-suffix fallback).
/// </summary>
internal static class NativeLoader
{
    internal static string? LoadedLibraryPath { get; private set; }

    /// <summary>Module handle of the loaded libgodot; the ProcessExit sweep frees by
    /// handle so same-named copies loaded by other ALCs (multi-instance) stay disjoint.</summary>
    internal static nint LoadedLibraryHandle { get; private set; }

    public static nint Load(string variant)
    {
        var path = Resolve(variant) ?? throw new DllNotFoundException(
            $"Could not locate libgodot-gdext-{variant}. Reference a 2dog.gdextension.[rid] natives package, " +
            "or build locally with: uv run python build-godot.py --mono no --no-glue");
        LoadedLibraryPath = path;
        return LoadedLibraryHandle = NativeLibrary.Load(path);
    }

    /// <summary>Loads exactly the given library - no resolution, no fallback.</summary>
    public static nint LoadExact(string path)
    {
        if (!File.Exists(path))
            throw new DllNotFoundException($"libgodot not found at '{path}'.");
        LoadedLibraryPath = path;
        return LoadedLibraryHandle = NativeLibrary.Load(path);
    }

    private static string? Resolve(string variant)
    {
        var (prefix, ext) = OperatingSystem.IsWindows() ? ("libgodot-gdext-", ".dll")
            : OperatingSystem.IsMacOS() ? ("libgodot-gdext-", ".dylib")
            : ("libgodot-gdext-", ".so");
        var fileName = prefix + variant + ext;

        // 1. Next to the application (natives package copy targets).
        var local = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(local)) return local;

        // 2. runtimes/<rid>/native (RID-specific publish layouts).
        var rid = RuntimeInformation.RuntimeIdentifier;
        var ridPath = Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", fileName);
        if (File.Exists(ridPath)) return ridPath;

        // 3. In-repo development fallback: godot/bin.
        var repoRoot = FindRepoRoot();
        if (repoRoot is null) return null;
        var binDir = Path.Combine(repoRoot, "godot", "bin");
        if (!Directory.Exists(binDir)) return null;

        var buildVariant = variant switch
        {
            "release" => "template_release",
            "editor" => "editor",
            _ => "template_debug",
        };
        foreach (var suffix in (string[])["gdext_shared_library", "shared_library"])
        {
            var candidates = Directory.GetFiles(binDir, $"*.{buildVariant}.*{suffix}{ext}")
                .Where(f => !f.Contains(".mono.") && !f.Contains(".console.")).ToArray();
            if (candidates.Length > 0) return candidates[0];
        }
        return null;
    }

    private static string? FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "2dog.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir;
    }
}
