using System.Diagnostics;
using System.Runtime.InteropServices;

namespace twodog.tests;

/// <summary>
/// Exercises the twodog.import helper's in-process import mode
/// (libgodot_import_project) against a scratch copy of the game project.
/// Spawns the helper as a subprocess, so it does not conflict with the
/// single-Godot-instance fixtures and needs no collection.
/// </summary>
public class ImportToolTests
{
    private static string RepoRoot
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

    private static string HelperPath
    {
        get
        {
            var candidates = new[] { "Release", "Debug", "Editor" }
                .Select(c => Path.Combine(RepoRoot, "twodog.import", "bin", c, "net8.0", "2dog.import.dll"))
                .ToList();

            // Fall back to the helper packaged inside the restored 2dog package
            // (CI test jobs restore from the local feed without building twodog.import).
            var packageDir = Path.Combine(RepoRoot, ".packages", "2dog");
            if (Directory.Exists(packageDir))
            {
                candidates.AddRange(Directory.EnumerateDirectories(packageDir)
                    .OrderDescending()
                    .Select(v => Path.Combine(v, "tools", "net8.0", "2dog.import.dll")));
            }

            var helper = candidates.FirstOrDefault(File.Exists);
            Assert.SkipWhen(helper == null, "twodog.import helper not found (build output or packaged)");
            return helper!;
        }
    }

    private static string EditorLibGodot
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

    private static (string apiDir, string toolsDir) GodotSharpDirs()
    {
        var apiDir = Path.Combine(RepoRoot, "godot", "bin", "GodotSharp", "Api", "Debug");
        var toolsDir = Path.Combine(RepoRoot, "godot", "bin", "GodotSharp", "Tools");
        Assert.SkipWhen(!File.Exists(Path.Combine(apiDir, "GodotPlugins.dll")), "GodotPlugins.dll not found");
        Assert.SkipWhen(!File.Exists(Path.Combine(toolsDir, "GodotTools.dll")), "GodotTools.dll not found");
        return (apiDir, toolsDir);
    }

    private static int RunHelper(params string[] arguments)
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

    private static string CreateScratchProject()
    {
        var source = Path.Combine(RepoRoot, "game");
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

    [Fact]
    public void InProcessImport_GeneratesUidsAndCache()
    {
        var (apiDir, toolsDir) = GodotSharpDirs();
        var scratch = CreateScratchProject();
        try
        {
            var exitCode = RunHelper("--libgodot", EditorLibGodot, "--api-dir", apiDir, "--tools-dir", toolsDir, scratch);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(Path.Combine(scratch, "Ticker.cs.uid")), "Ticker.cs.uid not generated");
            Assert.True(File.Exists(Path.Combine(scratch, "ToolNode.cs.uid")), "ToolNode.cs.uid not generated");
            Assert.True(File.Exists(Path.Combine(scratch, ".godot", "uid_cache.bin")), "uid_cache.bin not generated");

            // Second run must be idempotent.
            Assert.Equal(0, RunHelper("--libgodot", EditorLibGodot, "--api-dir", apiDir, "--tools-dir", toolsDir, scratch));
        }
        finally
        {
            try { Directory.Delete(scratch, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Import_MissingProject_Fails()
    {
        var missing = Path.Combine(Path.GetTempPath(), "2dog-import-missing-" + Guid.NewGuid().ToString("N"));
        Assert.NotEqual(0, RunHelper("--libgodot", EditorLibGodot, missing));
    }
}
