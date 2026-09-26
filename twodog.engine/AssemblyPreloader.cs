using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace twodog.fixture;

/// <summary>
/// Pre-loads game assemblies into the Default AssemblyLoadContext before Godot starts, avoiding type-identity
/// splits across load contexts. Public so hosts constructing <see cref="Engine"/> directly can apply the same guard.
/// </summary>
public static class AssemblyPreloader
{
    /// <summary>
    /// Discovers and pre-loads the project's game assemblies. Must be called before Engine.Start() so they
    /// land in the Default context rather than Godot's PluginLoadContext.
    /// </summary>
    /// <param name="projectPath">Path to the Godot project directory</param>
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Game assemblies are discovered and loaded dynamically before Godot starts.")]
    public static void PreloadGameAssemblies(string projectPath)
    {
        // On browser (wasm) the bundle already lives in the single default ALC; nothing on disk to probe.
        if (OperatingSystem.IsBrowser()) return;

        IReadOnlyList<(string Name, string? Path)> plan;
        try
        {
            plan = Plan(FindGameAssemblies(projectPath), HostAssemblyNames());
        }
        catch (Exception ex)
        {
            // Don't fail the test if pre-loading fails - just log and continue
            Console.WriteLine($"[AssemblyPreloader] Warning: Failed to find game assemblies: {ex.Message}");
            return;
        }

        if (plan.Count == 0)
        {
            Console.WriteLine("[AssemblyPreloader] No game assemblies found to pre-load");
            return;
        }

        Console.WriteLine($"[AssemblyPreloader] Found {plan.Count} game assembly(ies)");
        foreach (var (name, path) in plan)
        {
            try
            {
                var existingAssembly = AssemblyLoadContext.Default.Assemblies
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase));
                if (existingAssembly != null)
                {
                    Console.WriteLine($"[AssemblyPreloader] Assembly '{name}' already loaded in Default context");
                    continue;
                }

                var assembly = path is null
                    ? AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(name))
                    : Assembly.LoadFrom(path);
                Console.WriteLine($"[AssemblyPreloader] Pre-loaded assembly '{assembly.GetName().Name}' into Default context");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AssemblyPreloader] Warning: Failed to pre-load '{name}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Chooses what to pre-load. A host that references the game ships it on its trusted platform list, built in the
    /// host's configuration: those names load by name (null path), and other configurations' outputs or a renamed
    /// project's leftovers are skipped. Otherwise the first configuration folder with assemblies is loaded from disk.
    /// </summary>
    internal static IReadOnlyList<(string Name, string? Path)> Plan(IReadOnlyList<string[]> configurationFolders,
        IReadOnlySet<string> hostAssemblies)
    {
        var fromHost = configurationFolders.SelectMany(folder => folder)
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Where(hostAssemblies.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => (name, (string?)null))
            .ToArray();
        if (fromHost.Length > 0) return fromHost;

        var firstFolder = configurationFolders.FirstOrDefault(folder => folder.Length > 0) ?? [];
        return [.. firstFolder.Select(path => (Path.GetFileNameWithoutExtension(path), (string?)path))];
    }

    private static HashSet<string> HostAssemblyNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trusted)
            foreach (var path in trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                names.Add(Path.GetFileNameWithoutExtension(path));
        return names;
    }

    /// <summary>
    /// Lists compiled game assemblies in the project's Debug, Release and Editor outputs, in that order, each sorted
    /// for deterministic load order.
    /// </summary>
    private static IReadOnlyList<string[]> FindGameAssemblies(string projectPath)
    {
        // Godot builds assemblies to .godot/mono/temp/bin/{Configuration}/
        var monoTempBin = Path.Combine(projectPath, ".godot", "mono", "temp", "bin");
        if (!Directory.Exists(monoTempBin))
        {
            Console.WriteLine($"[AssemblyPreloader] Mono temp bin directory not found: {monoTempBin}");
            return [];
        }

        return
        [
            .. new[] { "Debug", "Release", "Editor" }
                .Select(config => Path.Combine(monoTempBin, config))
                .Where(Directory.Exists)
                .Select(configPath => Directory.GetFiles(configPath, "*.dll")
                    .Where(f => !IsSystemOrGodotAssembly(f))
                    .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
                    .ToArray()),
        ];
    }

    /// <summary>
    /// Filters out system and Godot framework assemblies to find user game assemblies.
    /// </summary>
    private static bool IsSystemOrGodotAssembly(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.StartsWith("GodotSharp", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("GodotPlugins.dll", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("netstandard.dll", StringComparison.OrdinalIgnoreCase);
    }
}
