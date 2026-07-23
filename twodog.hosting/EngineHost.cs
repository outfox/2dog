using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace twodog.Hosting;

/// <summary>
/// Starts N concurrent engine instances in one process: each gets its own
/// physical copy of the native libgodot (OS loaders dedupe by path - one module
/// per copy) and its own ALC for the managed bindings (statics are per-ALC).
/// The host itself never loads Godot types.
/// </summary>
public sealed class EngineHost : IDisposable
{
    // Process-wide: slots are never reused while the process lives, because a
    // disposed instance's module stays mapped until ProcessExit. Slot 0 uses
    // the original file, so single-instance keeps today's zero-copy behavior.
    private static int _slotCounter = -1;

    private readonly List<EngineInstance> _instances = [];
    private readonly Lock _gate = new();

    /// <summary>Starts an instance whose program type is referenced by the caller.
    /// Only the type's assembly path and name are used - the instance ALC loads
    /// its own copy (the default-ALC load of TProgram's assembly stays dormant).</summary>
    public EngineInstance Start<TProgram>(InstanceOptions options) where TProgram : IEngineProgram
    {
        var type = typeof(TProgram);
        return Start(options with
        {
            ProgramAssemblyPath = type.Assembly.Location,
            ProgramTypeName = $"{type.FullName}, {type.Assembly.GetName().Name}",
        });
    }

    public EngineInstance Start(InstanceOptions options)
    {
        var programAssemblyPath = options.ProgramAssemblyPath
            ?? throw new ArgumentException($"{nameof(InstanceOptions.ProgramAssemblyPath)} is required (or use Start<TProgram>).");
        var programTypeName = options.ProgramTypeName
            ?? throw new ArgumentException($"{nameof(InstanceOptions.ProgramTypeName)} is required (or use Start<TProgram>).");
        if (!File.Exists(programAssemblyPath))
            throw new FileNotFoundException("Program assembly not found.", programAssemblyPath);

        var source = options.NativeSourcePath ?? NativeResolver.Resolve(options.Variant);
        var nativePath = AcquireNativeCopy(source);
        var alc = new InstanceAlc(options.Tag, Path.GetFullPath(programAssemblyPath), options.SharedAssemblies);
        var instance = new EngineInstance(options, alc, Path.GetFullPath(programAssemblyPath), programTypeName, nativePath);
        lock (_gate) _instances.Add(instance);
        return instance;
    }

    /// <summary>Requests quit on all instances, then waits for each to finish.</summary>
    public void Dispose()
    {
        EngineInstance[] instances;
        lock (_gate)
        {
            instances = [.. _instances];
            _instances.Clear();
        }
        foreach (var instance in instances) instance.RequestQuit();
        foreach (var instance in instances) instance.Dispose();
    }

    /// <summary>
    /// Pool: %LOCALAPPDATA%/2dog/native-pool/&lt;key&gt;/slot-&lt;n&gt;/&lt;name&gt;. Copies
    /// persist across runs (keyed by source identity), so steady-state cost is
    /// zero. Cross-process slot collisions are fine - separate processes may
    /// map the same file.
    /// </summary>
    private static string AcquireNativeCopy(string sourcePath)
    {
        var slot = Interlocked.Increment(ref _slotCounter);
        if (slot == 0) return sourcePath;

        var source = new FileInfo(sourcePath);
        var key = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{source.FullName}|{source.Length}|{source.LastWriteTimeUtc.Ticks}")))[..16];
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "2dog", "native-pool", key, $"slot-{slot}");
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, source.Name);
        if (!File.Exists(dest))
        {
            var tmp = $"{dest}.{Environment.ProcessId}.tmp";
            File.Copy(sourcePath, tmp, overwrite: true);
            try
            {
                File.Move(tmp, dest);
            }
            catch (IOException)
            {
                File.Delete(tmp); // lost a cross-process race; dest exists now
            }
        }
        return dest;
    }

    /// <summary>Mirrors twodog.gdextension/NativeLoader.Resolve (which is internal
    /// to an assembly this Godot-free host must not reference).</summary>
    private static class NativeResolver
    {
        public static string Resolve(string variant)
        {
            var ext = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
            var fileName = $"libgodot-gdext-{variant}{ext}";

            var local = Path.Combine(AppContext.BaseDirectory, fileName);
            if (File.Exists(local)) return local;

            var rid = RuntimeInformation.RuntimeIdentifier;
            var ridPath = Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", fileName);
            if (File.Exists(ridPath)) return ridPath;

            var repoRoot = FindRepoRoot();
            var binDir = repoRoot is null ? null : Path.Combine(repoRoot, "godot", "bin");
            if (binDir is not null && Directory.Exists(binDir))
            {
                var buildVariant = variant switch
                {
                    "release" => "template_release",
                    "editor" => "editor",
                    _ => "template_debug",
                };
                foreach (var suffix in (string[])["gdext_shared_library", "shared_library"])
                {
                    var candidates = Directory.GetFiles(binDir, $"*godot.*.{buildVariant}.*{suffix}{ext}")
                        .Where(f => !f.Contains(".mono.") && !f.Contains(".console.")).ToArray();
                    if (candidates.Length > 0) return candidates[0];
                }
            }
            throw new DllNotFoundException(
                $"Could not locate libgodot-gdext-{variant}. Reference a 2dog.gdextension.[rid] natives package, " +
                "or build locally with: uv run python build-godot.py --mono no --no-glue");
        }

        private static string? FindRepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (dir is not null && !File.Exists(Path.Combine(dir, "2dog.sln")))
                dir = Path.GetDirectoryName(dir);
            return dir;
        }
    }
}
