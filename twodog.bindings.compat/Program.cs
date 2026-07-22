// Hash-compatibility check for the pinned GDExtension API: verifies that every
// class method, builtin method, and utility function hash recorded in
// gdextension/extension_api.json still resolves in a locally built libgodot.
// This is the 2dog equivalent of the upstream tests/compatibility_test
// hash-load check - run it after bumping the godot submodule to learn whether
// the committed bindings need regeneration.
//
// One flavor per process (Godot allows a single instance); `poe compat-test`
// runs both flavors sequentially. The expected binary is located strictly -
// no fallback: a missing binary is a hard failure.
//
// Usage: dotnet run --project twodog.bindings.compat -- [--flavor mono|gdext]
//        [--variant release|debug|editor]     (or TWODOG_VARIANT; default debug)

using System.Text.Json;
using Godot;
using Godot.NativeInterop;

namespace twodog.bindings.compat;

internal static class Program
{
    private static unsafe int Main(string[] args)
    {
        var variant = System.Environment.GetEnvironmentVariable("TWODOG_VARIANT") ?? "debug";
        var flavor = "gdext";
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--variant") variant = args[i + 1];
            if (args[i] == "--flavor") flavor = args[i + 1];
        }
        if (flavor is not ("mono" or "gdext"))
        {
            Console.Error.WriteLine($"[compat] unknown flavor '{flavor}' (expected 'mono' or 'gdext')");
            return 2;
        }

        var repoRoot = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(repoRoot, "2dog.sln")))
            repoRoot = Path.GetDirectoryName(repoRoot)
                       ?? throw new InvalidOperationException("Could not locate the 2dog repo root.");

        var apiPath = Path.Combine(repoRoot, "gdextension", "extension_api.json");
        using var api = JsonDocument.Parse(File.ReadAllText(apiPath));
        var root = api.RootElement;
        var header = root.GetProperty("header").GetProperty("version_full_name").GetString();
        Console.WriteLine($"[compat] pinned API: {header}");
        Console.WriteLine($"[compat] flavor: {flavor}, variant: {variant}");

        string nativePath;
        try
        {
            nativePath = FindNative(repoRoot, flavor, variant);
        }
        catch (FileNotFoundException e)
        {
            Console.Error.WriteLine($"[compat] FAIL - {e.Message}");
            return 2;
        }
        Console.WriteLine($"[compat] native: {nativePath}");
        if (flavor == "mono")
        {
            // The mono binary must bring up its .NET side for this run to count
            // as testing "the mono binaries": point it at the GodotSharp support
            // assemblies the way a 2dog.engine host would. A clean boot is
            // enforced by scripts/compat_test.py - any engine ERROR line fails.
            var apiConfig = variant == "release" ? "Release" : "Debug";
            var godotSharpDir = Path.Combine(repoRoot, "godot", "bin", "GodotSharp", "Api", apiConfig);
            if (!File.Exists(Path.Combine(godotSharpDir, "GodotPlugins.dll")))
            {
                Console.Error.WriteLine($"[compat] FAIL - GodotPlugins.dll not found in {godotSharpDir}; " +
                                        "the mono binary cannot initialize .NET without it. " +
                                        "Build the GodotSharp assemblies with: uv run poe build-godot");
                return 2;
            }
            SetEnvironmentVariableForNative("GODOTSHARP_DIR", godotSharpDir);
            Console.WriteLine($"[compat] GODOTSHARP_DIR: {godotSharpDir}");
        }

        var projectDir = Path.Combine(repoRoot, "twodog.bindings.tests", "project");
        EnsureGlobalScriptClassCache(projectDir);
        using var engine = new Engine("compat-test", projectDir, "--headless") { NativePath = nativePath };
        using var godot = engine.Start();

        var missing = new List<string>();
        int checkedMethods = 0, skippedClasses = 0, skippedModuleClasses = 0, skippedVirtuals = 0;
        var isEditorBinary = variant == "editor";

        // Classes the editor dump records as api_type=core but whose module only
        // exists in editor builds (modules/<name>/config.py: can_build =>
        // editor_build) - structurally absent from every template binary,
        // upstream and fork alike. Additions here need the same justification.
        var editorModuleCoreClasses = new HashSet<string> { "LightmapperRD" };

        // Class methods. Virtuals are dispatched, not bound - no method bind
        // exists for them even when the JSON records a hash (4.4+); upstream's
        // compatibility test skips them the same way. An absent class is only
        // legitimate for api_type=editor classes on a template build; any other
        // absence (core class, or anything on an editor binary) is a failure.
        foreach (var cls in root.GetProperty("classes").EnumerateArray())
        {
            var className = cls.GetProperty("name").GetString()!;
            var apiType = cls.TryGetProperty("api_type", out var at) ? at.GetString() : "core";
            if (!ClassDB.ClassExists(new StringName(className)))
            {
                if (apiType == "editor" && !isEditorBinary)
                {
                    skippedClasses++;
                    continue;
                }
                if (!isEditorBinary && editorModuleCoreClasses.Contains(className))
                {
                    skippedModuleClasses++;
                    continue;
                }
                missing.Add($"class {className} (api_type {apiType}) absent from ClassDB");
                continue;
            }
            if (!cls.TryGetProperty("methods", out var methods)) continue;
            foreach (var m in methods.EnumerateArray())
            {
                if (m.TryGetProperty("is_virtual", out var isVirtual) && isVirtual.GetBoolean())
                {
                    skippedVirtuals++;
                    continue;
                }
                if (!m.TryGetProperty("hash", out var hashProp)) continue;
                var name = m.GetProperty("name").GetString()!;
                checkedMethods++;
                if (MethodBinds.Resolve(className, name, hashProp.GetInt64()) == 0)
                    missing.Add($"class {className}.{name} (hash {hashProp.GetInt64()})");
            }
        }

        // Builtin (variant) methods: map JSON names to GDExtensionVariantType by
        // underscore-insensitive enum name. Vararg builtins have no ptr method.
        var typeByName = Enum.GetValues<GDExtensionVariantType>().ToDictionary(
            v => v.ToString().Replace("GDEXTENSION_VARIANT_TYPE_", "").Replace("_", ""),
            v => v);
        int checkedBuiltins = 0, skippedVarargBuiltins = 0;
        foreach (var cls in root.GetProperty("builtin_classes").EnumerateArray())
        {
            var clsName = cls.GetProperty("name").GetString()!;
            if (!typeByName.TryGetValue(clsName.ToUpperInvariant(), out var vtype)) continue;
            if (!cls.TryGetProperty("methods", out var methods)) continue;
            foreach (var m in methods.EnumerateArray())
            {
                if (m.TryGetProperty("is_vararg", out var vararg) && vararg.GetBoolean())
                {
                    skippedVarargBuiltins++;
                    continue;
                }
                if (!m.TryGetProperty("hash", out var hashProp)) continue;
                var name = m.GetProperty("name").GetString()!;
                checkedBuiltins++;
                var sn = StringNames.Get(name).Opaque;
                if (GdExtensionInterface.VariantGetPtrBuiltinMethod((int)vtype, (nint)(&sn), hashProp.GetInt64()) == 0)
                    missing.Add($"builtin {clsName}.{name} (hash {hashProp.GetInt64()})");
            }
        }

        var checkedUtilities = 0;
        foreach (var u in root.GetProperty("utility_functions").EnumerateArray())
        {
            var name = u.GetProperty("name").GetString()!;
            checkedUtilities++;
            var sn = StringNames.Get(name).Opaque;
            if (GdExtensionInterface.VariantGetPtrUtilityFunction((nint)(&sn), u.GetProperty("hash").GetInt64()) == 0)
                missing.Add($"utility {name} (hash {u.GetProperty("hash").GetInt64()})");
        }

        Console.WriteLine($"[compat] class methods checked: {checkedMethods} " +
                          $"(skipped: {skippedClasses} editor-only classes, " +
                          $"{skippedModuleClasses} editor-module core classes, {skippedVirtuals} virtuals)");
        Console.WriteLine($"[compat] builtin methods checked: {checkedBuiltins} " +
                          $"(skipped: {skippedVarargBuiltins} vararg)");
        Console.WriteLine($"[compat] utility functions checked: {checkedUtilities}");

        if (missing.Count == 0)
        {
            Console.WriteLine($"[compat] PASS ({flavor}) - every pinned hash resolves. 2DOG_COMPAT_TEST_PASSED");
            return 0;
        }

        foreach (var entry in missing) Console.WriteLine($"[compat] MISSING {entry}");
        Console.WriteLine($"[compat] FAIL ({flavor}) - {missing.Count} pinned hashes no longer resolve. " +
                          "Regenerate the bindings (poe dump-api + twodog.bindings.generator) or add compat shims.");
        return 1;
    }

    /// <summary>Native-visible env set: .NET's SetEnvironmentVariable does not
    /// propagate to native getenv() on Linux/macOS.</summary>
    private static void SetEnvironmentVariableForNative(string name, string value)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) setenv(name, value, 1);
        else System.Environment.SetEnvironmentVariable(name, value);
    }

    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern int setenv(string name, string value, int overwrite);

    /// <summary>
    /// The minimal test project ships without a .godot import cache, which
    /// costs an engine error print at boot. Write the empty cache an editor
    /// import would produce for a project with no class_name scripts.
    /// </summary>
    private static void EnsureGlobalScriptClassCache(string projectDir)
    {
        var cache = Path.Combine(projectDir, ".godot", "global_script_class_cache.cfg");
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        if (!File.Exists(cache)) File.WriteAllText(cache, "list=[]\n");
    }

    /// <summary>
    /// Locates the flavor's libgodot in godot/bin, strictly: mono means the
    /// plain shared_library build, gdext the gdext_shared_library build.
    /// Missing binary = FileNotFoundException (hard fail), never a fallback.
    /// </summary>
    private static string FindNative(string repoRoot, string flavor, string variant)
    {
        var binDir = Path.Combine(repoRoot, "godot", "bin");
        var buildVariant = variant switch
        {
            "release" => "template_release",
            "editor" => "editor",
            _ => "template_debug",
        };
        var ext = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        var suffix = flavor == "gdext" ? "gdext_shared_library" : "shared_library";
        var candidates = Directory.Exists(binDir)
            ? Directory.GetFiles(binDir, $"*godot.*.{buildVariant}.*{suffix}{ext}")
                .Where(f => !f.Contains(".console.") &&
                            (flavor == "gdext") == Path.GetFileName(f).Contains("gdext"))
                .ToArray()
            : [];
        if (candidates.Length == 1) return candidates[0];
        if (candidates.Length > 1)
            throw new FileNotFoundException(
                $"Ambiguous {flavor} {buildVariant} libgodot in godot/bin: {string.Join(", ", candidates.Select(Path.GetFileName))}");
        var monoFlag = flavor == "gdext" ? "--mono no " : "";
        throw new FileNotFoundException(
            $"No {flavor} {buildVariant} libgodot in godot/bin. " +
            $"Build it with: uv run build-godot.py {monoFlag}--no-glue --no-editor --target {buildVariant}");
    }
}
