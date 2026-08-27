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

        try
        {
            var gameAssemblyPaths = FindGameAssemblies(projectPath);
            if (gameAssemblyPaths.Any())
            {
                Console.WriteLine($"[AssemblyPreloader] Found {gameAssemblyPaths.Count()} game assembly(ies)");

                foreach (var gameAssemblyPath in gameAssemblyPaths)
                {
                    // Check if already loaded in Default context
                    var assemblyName = AssemblyName.GetAssemblyName(gameAssemblyPath);
                    var existingAssembly = AssemblyLoadContext.Default.Assemblies
                        .FirstOrDefault(a => string.Equals(a.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));

                    if (existingAssembly != null)
                    {
                        Console.WriteLine($"[AssemblyPreloader] Assembly '{assemblyName.Name}' already loaded in Default context");
                        continue;
                    }

                    // Load into Default context
                    var assembly = Assembly.LoadFrom(gameAssemblyPath);
                    Console.WriteLine($"[AssemblyPreloader] Pre-loaded assembly '{assembly.GetName().Name}' into Default context");
                }
            }
            else
            {
                Console.WriteLine("[AssemblyPreloader] No game assemblies found to pre-load");
            }
        }
        catch (Exception ex)
        {
            // Don't fail the test if pre-loading fails - just log and continue
            Console.WriteLine($"[AssemblyPreloader] Warning: Failed to pre-load game assemblies: {ex.Message}");
        }
    }

    /// <summary>
    /// Finds compiled game assemblies across the project's Debug/Release/Editor outputs, sorted for
    /// deterministic load order.
    /// </summary>
    private static IEnumerable<string> FindGameAssemblies(string projectPath)
    {
        // Godot builds assemblies to .godot/mono/temp/bin/{Configuration}/
        var monoTempBin = Path.Combine(projectPath, ".godot", "mono", "temp", "bin");
        if (!Directory.Exists(monoTempBin))
        {
            Console.WriteLine($"[AssemblyPreloader] Mono temp bin directory not found: {monoTempBin}");
            return Enumerable.Empty<string>();
        }

        // Try configurations in order: Debug, Release, Editor
        var configurations = new[] { "Debug", "Release", "Editor" };

        foreach (var config in configurations)
        {
            var configPath = Path.Combine(monoTempBin, config);
            if (!Directory.Exists(configPath))
                continue;

            var candidateDlls = Directory.GetFiles(configPath, "*.dll")
                .Where(f => !IsSystemOrGodotAssembly(f))
                .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
                .ToArray();

            if (candidateDlls.Length > 0)
            {
                if (candidateDlls.Length > 1)
                {
                    Console.WriteLine($"[AssemblyPreloader] Multiple game assemblies found in {config} (will pre-load all in alphabetical order):");
                    foreach (var dll in candidateDlls)
                        Console.WriteLine($"  - {Path.GetFileName(dll)}");
                }

                return candidateDlls;
            }
        }

        return Enumerable.Empty<string>();
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
