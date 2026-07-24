using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace twodog;

/// <summary>
/// Hosted-mode GodotPlugins wiring. When <see cref="Engine.NativePath"/> gives this load
/// context its own libgodot module, the engine must also use THIS context's GodotPlugins
/// (and therefore GodotSharp) instead of the hostfxr default-ALC load, so the managed
/// interop tables NativeFuncs.Initialize fills stay per-instance. The module's exported
/// set_load_from_executable_fn (GD_MONO_LIBGODOT_ENABLED) is the per-module seam:
/// GDMono::initialize consults the registered callback before falling back to hostfxr.
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
            // UnmanagedCallersOnly method: GetFunctionPointer returns the native-callable stub.
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
        // Same discovery order as Engine.ConfigureGodotSharpDir.
        var envDir = Environment.GetEnvironmentVariable("GODOTSHARP_DIR");
        if (!string.IsNullOrEmpty(envDir) && File.Exists(Path.Combine(envDir, "GodotPlugins.dll")))
            return Path.Combine(envDir, "GodotPlugins.dll");

        var assemblyDir = Path.GetDirectoryName(typeof(HostedGodotPlugins).Assembly.Location);
        if (!string.IsNullOrEmpty(assemblyDir))
        {
            var flat = Path.Combine(assemblyDir, "GodotPlugins.dll");
            if (File.Exists(flat)) return flat;
            var nested = Path.Combine(assemblyDir, "GodotSharp", "Api", "Debug", "GodotPlugins.dll");
            if (File.Exists(nested)) return nested;
        }

        throw new FileNotFoundException(
            "TwoDog: GodotPlugins.dll not found (probed GODOTSHARP_DIR, the twodog assembly directory, and " +
            "GodotSharp/Api/Debug). Hosted engine instances need the GodotPlugins layout the 2dog.engine " +
            "package copies to the output directory.");
    }
}
