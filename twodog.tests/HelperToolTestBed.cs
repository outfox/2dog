using System.Diagnostics;
using System.Runtime.InteropServices;

namespace twodog.tests;

/// <summary>
/// Shared plumbing for tests that exercise the twodog.import helper as a
/// subprocess (import and export-pack modes): locates the helper, the
/// editor-variant libgodot and the GodotSharp directories, and stages
/// scratch copies of the game project.
/// </summary>
internal static class HelperToolTestBed
{
    public static string RepoRoot
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "2dog.sln")))
                dir = Path.GetDirectoryName(dir);
            Assert.SkipWhen(dir == null, "Repo root not found (packaged test run)");
            return dir!;
        }
    }

    public static string HelperPath
    {
        get
        {
            var candidates = new[] { "Release", "Debug", "Editor" }
                .Select(c => Path.Combine(RepoRoot, "twodog.import", "bin", c, "net10.0", "2dog.import.dll"))
                .ToList();

            // Fall back to the helper packaged inside the restored 2dog.engine package
            // (CI test jobs restore from the local feed without building twodog.import).
            var packageDir = Path.Combine(RepoRoot, ".packages", "2dog.engine");
            if (Directory.Exists(packageDir))
            {
                candidates.AddRange(Directory.EnumerateDirectories(packageDir)
                    .OrderDescending()
                    .Select(v => Path.Combine(v, "tools", "net10.0", "2dog.import.dll")));
            }

            var helper = candidates.FirstOrDefault(File.Exists);
            Assert.SkipWhen(helper == null, "twodog.import helper not found (build output or packaged)");
            return helper!;
        }
    }

    public static string EditorLibGodot
    {
        get
        {
            var bin = Path.Combine(RepoRoot, "godot", "bin");
            var name =
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "godot.windows.editor.x86_64.shared_library.dll" :
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "libgodot.linuxbsd.editor.x86_64.shared_library.so" :
                "libgodot.macos.editor.arm64.shared_library.dylib";
            var path = Path.Combine(bin, name);
            Assert.SkipWhen(!File.Exists(path), $"Editor libgodot not found: {path}");
            return path;
        }
    }

    public static (string apiDir, string toolsDir) GodotSharpDirs()
    {
        var apiDir = Path.Combine(RepoRoot, "godot", "bin", "GodotSharp", "Api", "Debug");
        var toolsDir = Path.Combine(RepoRoot, "godot", "bin", "GodotSharp", "Tools");
        Assert.SkipWhen(!File.Exists(Path.Combine(apiDir, "GodotPlugins.dll")), "GodotPlugins.dll not found");
        Assert.SkipWhen(!File.Exists(Path.Combine(toolsDir, "GodotTools.dll")), "GodotTools.dll not found");
        return (apiDir, toolsDir);
    }

    public static int RunHelper(params string[] arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("exec");
        psi.ArgumentList.Add(HelperPath);
        foreach (var arg in arguments) psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;
        // Drain both streams concurrently; reading them sequentially can
        // deadlock once the other pipe's buffer fills.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(3)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("Import helper timed out");
        }

        Task.WaitAll(stdout, stderr);
        return process.ExitCode;
    }

    public static string CreateScratchProject()
    {
        var source = Path.Combine(RepoRoot, "demo");
        var scratch = Path.Combine(Path.GetTempPath(), "2dog-import-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            // Project sources only: no .godot cache, no bin/obj, and no .uid
            // files, so the test proves they are regenerated.
            if (file.EndsWith(".uid")) continue;
            File.Copy(file, Path.Combine(scratch, Path.GetFileName(file)));
        }

        return scratch;
    }
}
