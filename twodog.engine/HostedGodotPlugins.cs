using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace twodog;

/// <summary>
/// Registers this load context's GodotPlugins with a libgodot module through its exported
/// set_load_from_executable_fn, so GDMono initializes on the host's own runtime and never
/// falls back to hostfxr (machine-wide; boots a second runtime under self-contained hosts).
/// </summary>
internal static unsafe class HostedGodotPlugins
{
    private static nint _initializeFromEngine;

    /// <summary>Registers this context's GodotPlugins.Main.InitializeFromEngine with the
    /// given libgodot module. Must run before the engine instance is created.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Hosted multi-instance loads GodotPlugins from the untrimmed output layout; " +
                        "hosted deployments cannot be trimmed (AssemblyDependencyResolver needs deps.json).")]
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "GodotPlugins.Main.InitializeFromEngine is preserved by the untrimmed GodotPlugins.dll " +
                        "shipped in the 2dog.engine package; hosted deployments cannot be trimmed.")]
    internal static void Register(nint moduleHandle)
    {
        if (_initializeFromEngine == 0)
        {
            var alc = AssemblyLoadContext.GetLoadContext(typeof(HostedGodotPlugins).Assembly)
                      ?? AssemblyLoadContext.Default;
            var assembly = alc.LoadFromAssemblyPath(FindGodotPluginsPath());
            var method = assembly.GetType("GodotPlugins.Main", throwOnError: true)!
                             .GetMethod("InitializeFromEngine", BindingFlags.NonPublic | BindingFlags.Static)
                         ?? throw new MissingMethodException("GodotPlugins.Main", "InitializeFromEngine");
            // The pointer is only native-callable for [UnmanagedCallersOnly] methods; a contract
            // drift in GodotPlugins must fail managed here, not as a native ABI crash later.
            if (method.GetCustomAttribute<UnmanagedCallersOnlyAttribute>() is null)
                throw new InvalidOperationException(
                    "TwoDog: GodotPlugins.Main.InitializeFromEngine is not [UnmanagedCallersOnly] - the " +
                    "GodotPlugins.dll found does not match this engine's hosted-mode contract.");
            _initializeFromEngine = method.MethodHandle.GetFunctionPointer();
        }

        var setLoadFromExecutable = (delegate* unmanaged<nint, void>)NativeLibrary.GetExport(
            moduleHandle, "set_load_from_executable_fn");
        setLoadFromExecutable((nint)(delegate* unmanaged<nint>)&LoadFromExecutable);
    }

    [UnmanagedCallersOnly]
    private static nint LoadFromExecutable() => _initializeFromEngine;

    private static string FindGodotPluginsPath()
    {
        // Same discovery order as Engine.ConfigureGodotSharpDir: release/debug variants ship the
        // API flat next to twodog.dll; the editor variant nests it under GodotSharp/Api/Debug
        // (upstream editor convention - the Debug folder name is fixed regardless of build config).
        var envDir = Environment.GetEnvironmentVariable("GODOTSHARP_DIR");
        if (!string.IsNullOrEmpty(envDir) && File.Exists(Path.Combine(envDir, "GodotPlugins.dll")))
            return Path.Combine(envDir, "GodotPlugins.dll");

        string?[] baseDirs = [Path.GetDirectoryName(typeof(HostedGodotPlugins).Assembly.Location), AppContext.BaseDirectory];
        foreach (var baseDir in baseDirs)
        {
            if (string.IsNullOrEmpty(baseDir)) continue;
            var flat = Path.Combine(baseDir, "GodotPlugins.dll");
            if (File.Exists(flat)) return flat;
            var nested = Path.Combine(baseDir, "GodotSharp", "Api", "Debug", "GodotPlugins.dll");
            if (File.Exists(nested)) return nested;
        }

        throw new FileNotFoundException(
            "TwoDog: GodotPlugins.dll not found (probed GODOTSHARP_DIR, the twodog assembly directory, and " +
            "GodotSharp/Api/Debug). Hosted engine instances need the GodotPlugins layout the 2dog.engine " +
            "package copies to the output directory.");
    }
}
