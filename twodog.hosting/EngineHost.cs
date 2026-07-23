using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;

namespace twodog.Hosting;

/// <summary>
/// Starts N concurrent engine instances in one process: each gets its own
/// physical copy of the native libgodot (OS loaders dedupe by path - one module
/// per copy) and its own ALC for the managed bindings (statics are per-ALC).
/// The host itself never loads Godot types.
///
/// Process-global caveats (cannot be isolated in-process): CWD (the engine
/// chdirs during boot - pass absolute paths), environment variables, native
/// crash blast radius, signal/exception handlers, stdio. For test isolation
/// with none of these constraints, use one process per engine instead.
/// </summary>
public sealed class EngineHost : IDisposable
{
    // Process-wide: slots are never reused while the process lives, because a
    // disposed instance's module stays mapped until ProcessExit. Slot 0 uses
    // the original file, so single-instance keeps today's zero-copy behavior.
    private static int _slotCounter = -1;

    private readonly List<EngineInstance> _instances = [];
    private readonly Lock _gate = new();
    private bool _disposed;

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

        // Normalize every path NOW, on the caller's thread: CWD is process-global
        // and engine boots move it, so relative paths must not survive to any
        // later point. (Absolute inputs are still the only fully safe choice.)
        programAssemblyPath = Path.GetFullPath(programAssemblyPath);
        options = options with
        {
            ProjectDir = Path.GetFullPath(options.ProjectDir),
            ProgramAssemblyPath = programAssemblyPath,
            NativeSourcePath = options.NativeSourcePath is { } native ? Path.GetFullPath(native) : null,
        };
        if (!File.Exists(programAssemblyPath))
            throw new FileNotFoundException("Program assembly not found.", programAssemblyPath);

        var resolver = new AssemblyDependencyResolver(programAssemblyPath);
        ValidateSharedAssemblies(options.SharedAssemblies, resolver);

        var source = Path.GetFullPath(options.NativeSourcePath ?? NativeResolver.Resolve(options.Variant));
        var nativePath = AcquireNativeCopy(source);
        var alc = new InstanceAlc(options.Tag, programAssemblyPath, options.SharedAssemblies, resolver);
        var instance = new EngineInstance(options, alc, programAssemblyPath, programTypeName, nativePath);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _instances.Add(instance);
            // The thread only starts once the instance is registered, so
            // Dispose can never race past an unrecorded live engine.
            instance.Begin();
        }
        return instance;
    }

    /// <summary>Requests quit on all instances, then waits for each (bounded by
    /// their ShutdownTimeout). Instance failures/timeouts are aggregated.
    /// Native modules, ALCs, and pool slots stay resident until process exit -
    /// see <see cref="EngineInstance.Dispose"/>.</summary>
    public void Dispose()
    {
        EngineInstance[] instances;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            instances = [.. _instances];
            _instances.Clear();
        }
        foreach (var instance in instances) instance.RequestQuit();
        List<Exception>? errors = null;
        foreach (var instance in instances)
        {
            try
            {
                instance.Dispose();
            }
            catch (Exception e)
            {
                (errors ??= []).Add(e);
            }
        }
        if (errors is not null)
            throw new AggregateException("One or more engine instances failed to shut down cleanly.", errors);
    }

    /// <summary>
    /// Sharing an assembly that (transitively) references the bindings stack
    /// would reunify the per-ALC statics the whole isolation model rests on -
    /// reject it up front by walking assembly references.
    /// </summary>
    private static void ValidateSharedAssemblies(string[] shared, AssemblyDependencyResolver resolver)
    {
        if (shared.Length == 0) return;
        string[] forbidden = ["twodog.bindings", "twodog.gdextension", "twodog.hosting.runtime"];
        var pending = new Queue<string>(shared);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (pending.Count > 0)
        {
            var name = pending.Dequeue();
            if (!visited.Add(name)) continue;
            if (forbidden.Contains(name, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException(
                    $"SharedAssemblies must be Godot-free: '{string.Join("' / '", shared)}' leads to '{name}', " +
                    "whose statics must stay per-instance.");
            var path = resolver.ResolveAssemblyToPath(new AssemblyName(name));
            if (path is null) continue; // framework/default-ALC assembly - no bindings reachable this way
            using var pe = new PEReader(File.OpenRead(path));
            var metadata = pe.GetMetadataReader();
            foreach (var handle in metadata.AssemblyReferences)
                pending.Enqueue(metadata.GetString(metadata.GetAssemblyReference(handle).Name));
        }
    }

    /// <summary>
    /// Pool: %LOCALAPPDATA%/2dog/native-pool/&lt;key&gt;/slot-&lt;n&gt;/&lt;name&gt;. Copies
    /// persist across runs, keyed by source identity (path, size, mtime - NOT
    /// content; an in-place rebuild changes size/mtime and therefore the key),
    /// so steady-state cost is zero. Cross-process slot collisions are fine -
    /// separate processes may map the same file.
    /// </summary>
    private static string AcquireNativeCopy(string sourcePath)
    {
        var slot = Interlocked.Increment(ref _slotCounter);
        if (slot == 0) return sourcePath;

        var source = new FileInfo(sourcePath);
        var identityKey = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{source.FullName}|{source.Length}|{source.LastWriteTimeUtc.Ticks}")))[..16];
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "2dog", "native-pool", identityKey, $"slot-{slot}");
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, source.Name);
        // Length check evicts partial copies left by a crashed process.
        if (!File.Exists(dest) || new FileInfo(dest).Length != source.Length)
        {
            var tmp = $"{dest}.{Environment.ProcessId}.tmp";
            File.Copy(sourcePath, tmp, overwrite: true);
            try
            {
                File.Move(tmp, dest, overwrite: false);
            }
            catch (IOException) when (File.Exists(dest))
            {
                File.Delete(tmp); // lost a cross-process race to a completed copy
            }
        }
        if (new FileInfo(dest).Length != source.Length)
            throw new IOException($"Native pool copy at '{dest}' does not match its source '{sourcePath}'.");
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
