// Dual-instance spike host: proves two libgodot instances can run in one
// process by loading a renamed COPY of the gdext native twice (the "one
// instance per process" limit is really per-module) and isolating the managed
// bindings per instance via AssemblyLoadContexts (statics are per-ALC).
//
// Stages (--stage a|b|c|d):
//   a  dual native load only - two modules, distinct bases/exports, no boot
//   b  sequential dual boot - instance A full lifecycle, then B, then exit
//      (exit code 0 also validates the two per-ALC ProcessExit FreeLibrary sweeps)
//   c  concurrent - both instances pump simultaneously on two threads
//   d  stress - concurrent + per-frame node/RefCounted churn

using System.Diagnostics;
using System.Runtime.InteropServices;
using DualHost;

internal static class Program
{
    private static int _checks;
    private static int _failures;

    private static int Main(string[] rawArgs)
    {
        // Line-accurate interleaving with the engine's unbuffered stderr.
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });

        var stage = "a";
        var sameNames = false;
        for (var i = 0; i < rawArgs.Length; i++)
        {
            if (rawArgs[i] == "--stage" && i + 1 < rawArgs.Length) stage = rawArgs[i + 1];
            if (rawArgs[i] == "--same-names") sameNames = true;
        }

        var repoRoot = FindRepoRoot();
        var sourceDll = FindNonMonoLibgodot(repoRoot);
        var scratch = Path.Combine(Path.GetTempPath(), "dual-instance-spike", Environment.ProcessId.ToString());
        Directory.CreateDirectory(scratch);
        Console.WriteLine($"[host] stage:    {stage}");
        Console.WriteLine($"[host] libgodot: {sourceDll}");
        Console.WriteLine($"[host] scratch:  {scratch}");

        // Distinct file names were mandatory while the ProcessExit sweep
        // resolved modules via GetModuleHandleW(bare filename). --same-names
        // exercises the handle-based sweep: same file name, distinct dirs.
        var dllA = sameNames
            ? Path.Combine(Directory.CreateDirectory(Path.Combine(scratch, "A")).FullName, "libgodot-dual.dll")
            : Path.Combine(scratch, "libgodot-dual-A.dll");
        var dllB = sameNames
            ? Path.Combine(Directory.CreateDirectory(Path.Combine(scratch, "B")).FullName, "libgodot-dual.dll")
            : Path.Combine(scratch, "libgodot-dual-B.dll");
        File.Copy(sourceDll, dllA, overwrite: true);
        File.Copy(sourceDll, dllB, overwrite: true);

        LogWorkingSet("before stage");
        var rc = stage switch
        {
            "a" => StageA(dllA, dllB),
            "b" => StageBcd(repoRoot, scratch, dllA, dllB, concurrent: false, frames: 120, churn: 0),
            "c" => StageBcd(repoRoot, scratch, dllA, dllB, concurrent: true, frames: 300, churn: 0),
            "d" => StageBcd(repoRoot, scratch, dllA, dllB, concurrent: true, frames: 600, churn: 100),
            _ => UnknownStage(stage),
        };
        LogWorkingSet("after stage");

        Console.WriteLine();
        Console.WriteLine(rc == 0
            ? $"[host] stage {stage} PASS - all {_checks} checks succeeded (exit code 0 pending clean process teardown)"
            : $"[host] stage {stage} FAIL - {_failures}/{_checks} checks failed");
        return rc;
    }

    // ------------------------------------------------------------- stage a --

    private static int StageA(string dllA, string dllB)
    {
        var libA = NativeLibrary.Load(dllA);
        var libB = NativeLibrary.Load(dllB);
        Check(libA != 0 && libB != 0, $"both copies loaded (A=0x{libA:x} B=0x{libB:x})");
        Check(libA != libB, "distinct module handles");

        var createA = NativeLibrary.GetExport(libA, "libgodot_create_godot_instance");
        var createB = NativeLibrary.GetExport(libB, "libgodot_create_godot_instance");
        var destroyA = NativeLibrary.GetExport(libA, "libgodot_destroy_godot_instance");
        var destroyB = NativeLibrary.GetExport(libB, "libgodot_destroy_godot_instance");
        Check(createA != 0 && createB != 0 && destroyA != 0 && destroyB != 0, "entry points resolved from both modules");
        Check(createA != createB, $"distinct create exports (A=0x{createA:x} B=0x{createB:x})");

        if (OperatingSystem.IsWindows())
        {
            var baseA = GetModuleHandleW(Path.GetFileName(dllA));
            var baseB = GetModuleHandleW(Path.GetFileName(dllB));
            Check(baseA != 0 && baseB != 0 && baseA != baseB,
                $"two distinct module bases mapped (A=0x{baseA:x} B=0x{baseB:x})");
        }

        NativeLibrary.Free(libB);
        NativeLibrary.Free(libA);
        Check(true, "both modules freed without boot (no engine init happened)");
        return _failures == 0 ? 0 : 1;
    }

    // --------------------------------------------------------- stages b/c/d --

    private static int StageBcd(string repoRoot, string scratch, string dllA, string dllB,
                                bool concurrent, int frames, int churn)
    {
        var driverDll = FindDriverDll(repoRoot);
        Console.WriteLine($"[host] driver:   {driverDll}");

        var projA = PrepareProject(repoRoot, scratch, "proj-A");
        var projB = PrepareProject(repoRoot, scratch, "proj-B");

        var bootBarrier = concurrent ? new Barrier(2) : null;
        var exitBarrier = concurrent ? new Barrier(2) : null;
        Action? BarrierAction(Barrier? b, string what) => b is null
            ? null
            : () =>
            {
                if (!b.SignalAndWait(TimeSpan.FromSeconds(120)))
                    throw new TimeoutException($"{what} barrier timed out - sibling instance never arrived");
            };

        var runA = () => RunInstance("dual-A", driverDll, dllA, projA, frames, churn,
            BarrierAction(bootBarrier, "boot"), BarrierAction(exitBarrier, "exit"));
        var runB = () => RunInstance("dual-B", driverDll, dllB, projB, frames, churn,
            BarrierAction(bootBarrier, "boot"), BarrierAction(exitBarrier, "exit"));

        string reportA, reportB;
        var sw = Stopwatch.StartNew();
        if (concurrent)
        {
            var taskA = Task.Factory.StartNew(runA, TaskCreationOptions.LongRunning);
            var taskB = Task.Factory.StartNew(runB, TaskCreationOptions.LongRunning);
            reportA = taskA.GetAwaiter().GetResult();
            reportB = taskB.GetAwaiter().GetResult();
        }
        else
        {
            reportA = Task.Factory.StartNew(runA, TaskCreationOptions.LongRunning).GetAwaiter().GetResult();
            LogWorkingSet("after instance A (module A still mapped)");
            reportB = Task.Factory.StartNew(runB, TaskCreationOptions.LongRunning).GetAwaiter().GetResult();
        }
        sw.Stop();

        Console.WriteLine();
        Console.WriteLine($"[host] both drivers returned in {sw.ElapsedMilliseconds}ms; reports:");
        Console.WriteLine("---- dual-A ----");
        Console.Write(reportA);
        Console.WriteLine("---- dual-B ----");
        Console.Write(reportB);
        Console.WriteLine("----------------");

        Check(reportA.Contains("RESULT dual-A: PASS"), "instance A report is PASS");
        Check(reportB.Contains("RESULT dual-B: PASS"), "instance B report is PASS");
        return _failures == 0 ? 0 : 1;
    }

    /// <summary>Loads dual.driver into a fresh ALC and invokes Driver.Run via reflection
    /// (only CoreLib types cross; Godot types never reach the default ALC).</summary>
    private static string RunInstance(string tag, string driverDll, string nativeDll, string projectDir,
                                      int frames, int churn, Action? bootBarrier, Action? exitBarrier)
    {
        try
        {
            var alc = new InstanceAlc(tag, driverDll);
            var asm = alc.LoadFromAssemblyPath(driverDll);
            var run = asm.GetType("DualSpike.Driver")?.GetMethod("Run")
                      ?? throw new MissingMethodException("DualSpike.Driver.Run not found in " + driverDll);
            return (string)run.Invoke(null, [tag, nativeDll, projectDir, frames, churn, bootBarrier, exitBarrier])!;
        }
        catch (Exception e)
        {
            return $"RESULT {tag}: FAIL (host-side exception)\n{e}\n";
        }
    }

    private static string PrepareProject(string repoRoot, string scratch, string name)
    {
        var template = Path.Combine(repoRoot, "spikes", "dual-instance", "project-template");
        var dir = Path.Combine(scratch, name);
        Directory.CreateDirectory(dir);
        foreach (var file in Directory.GetFiles(template))
            File.Copy(file, Path.Combine(dir, Path.GetFileName(file)), overwrite: true);
        // Pre-write the empty import cache an editor would produce (avoids a boot error print).
        Directory.CreateDirectory(Path.Combine(dir, ".godot"));
        File.WriteAllText(Path.Combine(dir, ".godot", "global_script_class_cache.cfg"), "list=[]\n");
        return dir;
    }

    // ------------------------------------------------------------ utilities --

    private static int UnknownStage(string stage)
    {
        Console.WriteLine($"[host] unknown stage '{stage}' - use --stage a|b|c|d");
        return 2;
    }

    private static void Check(bool ok, string what)
    {
        _checks++;
        if (!ok) _failures++;
        Console.WriteLine($"[host] {(ok ? "ok " : "FAIL")} {what}");
    }

    private static void LogWorkingSet(string when)
    {
        using var p = Process.GetCurrentProcess();
        p.Refresh();
        Console.WriteLine($"[host] working set {when}: {p.WorkingSet64 / (1024 * 1024)} MB");
    }

    private static string FindDriverDll(string repoRoot)
    {
        var candidates = new[] { "Debug", "Release" }
            .Select(c => Path.Combine(repoRoot, "spikes", "dual-instance", "dual.driver", "bin", c, "net10.0", "dual.driver.dll"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
        return candidates.FirstOrDefault() ?? throw new FileNotFoundException(
            "dual.driver.dll not found - build spikes/dual-instance/dual.host first (it builds the driver too).");
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "2dog.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Could not locate 2dog repo root above " + AppContext.BaseDirectory);
    }

    private static string FindNonMonoLibgodot(string repoRoot)
    {
        var binDir = Path.Combine(repoRoot, "godot", "bin");
        var ext = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        foreach (var suffix in (string[])["gdext_shared_library", "shared_library"])
        {
            var candidates = Directory.Exists(binDir)
                ? Directory.GetFiles(binDir, $"*godot.*.template_debug.*{suffix}{ext}")
                    .Where(f => !f.Contains(".mono.") && !f.Contains(".console.")).ToArray()
                : [];
            if (candidates.Length > 0) return candidates[0];
        }
        throw new FileNotFoundException(
            "No non-mono template_debug libgodot found in godot/bin. " +
            "Build it with: uv run python build-godot.py --mono no --no-editor --no-glue --target template_debug");
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string moduleName);
}
