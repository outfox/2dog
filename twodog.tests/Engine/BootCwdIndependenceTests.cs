using twodog;
using twodog.fixture;
using twodog.Hosting.Xunit;
using Godot;
using Environment = System.Environment;

namespace twodog.tests.EngineTests;

// Boots must resolve the requested project even while the process CWD moves
// under them: another engine instance booting (or a DirAccess call in any
// instance) chdirs the whole process. Regression test for the CI flake where
// the classic engine booted against a hosting fixture's scratch project.
// No fixture on purpose: the collection manages its own engines.
[CollectionDefinition(nameof(BootCwdIndependenceCollection), DisableParallelization = true)]
public class BootCwdIndependenceCollection;

[Collection(nameof(BootCwdIndependenceCollection))]
public class BootCwdIndependenceTests
{
    [Fact]
    public void BootedProjectMatchesRequestedPath_WhileCwdChurns()
    {
        // Windows documents the process CWD as not thread-safe: concurrent
        // SetCurrentDirectory while native code runs can fault inside OS path
        // machinery (crashed win-x64 Release CI with an access violation).
        // The churn scenario runs on Unix, where chdir/getcwd are atomic
        // syscalls; the realistic Windows adversary - another engine's boot
        // moving the CWD - is serialized away by the boot lock (BootLockTests).
        Assert.SkipWhen(OperatingSystem.IsWindows(),
            "concurrent SetCurrentDirectory is not thread-safe on Windows");

        var projectDir = Engine.ResolveProjectDir();
        AssemblyPreloader.PreloadGameAssemblies(projectDir);

        // A bootable decoy project in temp: if a boot picks up the CWD instead
        // of the requested path, it finds this project's name, not "showcase".
        var decoy = ScratchProject.Create("cwd-decoy");
        var originalCwd = Environment.CurrentDirectory;
        var stop = false;
        var churn = new Thread(() =>
        {
            var temp = Path.GetTempPath();
            while (!Volatile.Read(ref stop))
            {
                Environment.CurrentDirectory = decoy;
                Environment.CurrentDirectory = temp;
                // Stay adversarial without starving the booting thread on
                // small CI runners.
                Thread.Yield();
            }
        }) { IsBackground = true };

        churn.Start();
        try
        {
            for (var i = 0; i < 3; i++)
            {
                using var engine = new Engine($"cwd-independence-{i}", projectDir, "--headless");
                using var godot = engine.Start();
                var name = (string)ProjectSettings.GetSetting("application/config/name");
                Assert.Equal("showcase", name);
            }
        }
        finally
        {
            Volatile.Write(ref stop, true);
            churn.Join();
            Environment.CurrentDirectory = originalCwd;
            ScratchProject.Delete(decoy);
        }
    }
}
