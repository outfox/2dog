using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace twodog;

/// <summary>
/// Resolves the logical <c>libgodot</c> P/Invoke name to the variant-specific
/// native library (<c>libgodot-release</c>, <c>libgodot-debug</c>, or
/// <c>libgodot-editor</c>). The variant is declared via
/// <c>[AssemblyMetadata("TwoDogVariant", ...)]</c>, which the 2dog package
/// targets emit into consuming assemblies from the <c>&lt;TwoDogVariant&gt;</c>
/// MSBuild property. Distinct per-variant file names make a variant mismatch a
/// loud, actionable load error instead of a silent wrong-library load (or an
/// opaque hostfxr failure from a stale copy).
/// </summary>
internal static class LibGodotLoader
{
    private static readonly string[] KnownVariants = ["release", "debug", "editor"];

    /// <summary>
    /// File name of the libgodot actually loaded (e.g. "libgodot-editor.dll").
    /// Used to unload the module by name at process exit on Windows.
    /// </summary>
    internal static string? LoadedLibraryFileName { get; private set; }

    /// <summary>Called from LibGodot's static constructor, so registration is
    /// guaranteed to precede the first libgodot P/Invoke.</summary>
    internal static void Register()
    {
        // Browser-wasm links libgodot statically; there is nothing to resolve.
        if (OperatingSystem.IsBrowser()) return;
        // Register for the twodog assembly ONLY. GodotSharp's own libgodot
        // bridge imports are covered by the resolver GodotPlugins registers
        // for it during InitializeFromEngine (with the engine's real module
        // handle) - claiming that slot here breaks GodotPlugins init.
        NativeLibrary.SetDllImportResolver(typeof(LibGodotLoader).Assembly, Resolve);
    }

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != LibGodot.LIBGODOT_LIBRARY_NAME) return nint.Zero;

        var variant = ResolveVariant();
        var probeDirs = ProbeDirs();

        // Declared variant first; without a declaration, any variant-named library.
        List<string> candidates = variant != null
            ? [VariantFileName(variant)]
            : [.. Array.ConvertAll(KnownVariants, VariantFileName)];

        foreach (var candidate in candidates)
        {
            foreach (var dir in probeDirs)
            {
                var path = Path.Combine(dir, candidate);
                if (!File.Exists(path)) continue;
                var handle = NativeLibrary.Load(path);
                LoadedLibraryFileName = candidate;
                return handle;
            }
        }

        // Legacy flat name: pre-rename natives packages and in-repo local-dev copies.
        var plainName = VariantFileName(null);
        foreach (var dir in probeDirs)
        {
            var path = Path.Combine(dir, plainName);
            if (!File.Exists(path)) continue;
            if (variant != null)
                Console.Error.WriteLine(
                    $"TwoDog: TwoDogVariant is '{variant}' but {VariantFileName(variant)} was not found; " +
                    $"falling back to {plainName}, which may be a different variant.");
            var handle = NativeLibrary.Load(path);
            LoadedLibraryFileName = plainName;
            return handle;
        }

        // Default runtime probing (deps.json runtime assets, NATIVE_DLL_SEARCH_DIRECTORIES).
        if (NativeLibrary.TryLoad(libraryName, assembly, searchPath, out var fallback))
        {
            LoadedLibraryFileName = plainName;
            return fallback;
        }

        throw new DllNotFoundException(
            "TwoDog: could not locate the native libgodot library " +
            (variant != null
                ? $"for TwoDogVariant '{variant}' (expected {VariantFileName(variant)})"
                : $"(expected one of: {string.Join(", ", candidates)}, or {plainName})") +
            $". Probed: {string.Join("; ", probeDirs)}. " +
            "Ensure the 2dog platform package for this OS is restored and rebuild " +
            "after changing <TwoDogVariant>.");
    }

    private static string? ResolveVariant()
    {
        // The entry assembly's declaration wins; under non-standard hosts
        // (dotnet test) the entry assembly has no metadata, so fall back to
        // scanning loaded assemblies (same discovery as Engine.ResolveProjectDir).
        if (Assembly.GetEntryAssembly() is { } entry && MetadataVariant(entry) is { } declared)
            return declared;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (MetadataVariant(asm) is { } found)
                return found;
        }

        return null;
    }

    private static string? MetadataVariant(Assembly assembly)
    {
        try
        {
            foreach (var attr in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
            {
                if (attr.Key != "TwoDogVariant" || string.IsNullOrEmpty(attr.Value)) continue;
                var variant = attr.Value.ToLowerInvariant();
                if (Array.IndexOf(KnownVariants, variant) >= 0) return variant;
            }
        }
        catch
        {
            // Some dynamic/reflection-emit assemblies may throw
        }

        return null;
    }

    private static List<string> ProbeDirs()
    {
        List<string> dirs = [];
        var assemblyDir = Path.GetDirectoryName(typeof(LibGodotLoader).Assembly.Location);
        if (!string.IsNullOrEmpty(assemblyDir)) dirs.Add(assemblyDir);
        if (!string.IsNullOrEmpty(AppContext.BaseDirectory) && !dirs.Contains(AppContext.BaseDirectory))
            dirs.Add(AppContext.BaseDirectory);
        return dirs;
    }

    private static string VariantFileName(string? variant)
    {
        var suffix = variant == null ? "" : "-" + variant;
        if (OperatingSystem.IsWindows()) return $"libgodot{suffix}.dll";
        if (OperatingSystem.IsMacOS()) return $"libgodot{suffix}.dylib";
        return $"libgodot{suffix}.so";
    }
}
