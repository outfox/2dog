using System.Runtime.InteropServices;

namespace twodog.cli;

/// <summary>A check's identity for --list-checks and the docs.</summary>
internal sealed record CheckInfo(string Id, Category Category, string Description);

/// <summary>The machine: SDK, workloads, platform, overrides, restored packages.</summary>
internal static class EnvironmentChecks
{
    public static readonly CheckInfo[] Checks =
    [
        new("env.dotnet-sdk", Category.Environment, "a .NET 10 SDK is installed"),
        new("env.global-json", Category.Environment, "the root global.json pin is satisfied by an installed SDK"),
        new("env.wasm-tools", Category.Environment, "the wasm-tools workload is installed when a browser host exists"),
        new("env.host-platform", Category.Environment, "this OS and architecture have 2dog native packages"),
        new("env.godot-editor", Category.Environment, "GODOT_EDITOR, when set, points at an existing file"),
        new("env.overrides", Category.Environment, "GODOTSHARP_DIR and the other layout overrides point at what they claim"),
        new("env.packages-restored", Category.Environment, "the engine, tools and native packages are in the NuGet cache"),
    ];

    private static readonly string[] SupportedRids = ["win-x64", "linux-x64", "osx-arm64"];

    public static IEnumerable<Finding> Run(DoctorContext ctx)
    {
        const Category c = Category.Environment;
        var project = ctx.Project;

        var sdks = ctx.Sdks;
        var usable = sdks.Where(s => s.Version.Major >= 10).ToList();
        if (sdks.Count == 0)
            yield return new Finding("env.dotnet-sdk", c, Severity.Fail, "dotnet SDK not found",
                "'dotnet --list-sdks' listed nothing", "install the .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0");
        else if (usable.Count == 0)
            yield return new Finding("env.dotnet-sdk", c, Severity.Fail, $".NET SDK 10 missing (newest installed: {sdks[0].Raw})",
                "2dog projects target net10.0", "install the .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0");
        else
            yield return Finding.Pass("env.dotnet-sdk", c, $".NET SDK {(usable.FirstOrDefault(s => !s.IsPreview) ?? usable[0]).Raw}");

        if (project.RootGlobalJsonText is { } json)
        {
            var (pin, roll) = DotnetInfo.ParseGlobalJson(json);
            if (pin != null && sdks.Count > 0 && !DotnetInfo.Satisfies(pin, roll, sdks))
                yield return new Finding("env.global-json", c, Severity.Fail,
                    $"global.json pins SDK {pin} ({roll}) but no installed SDK satisfies it",
                    $"installed: {string.Join(", ", sdks.Select(s => s.Raw))}",
                    "install a matching SDK, or adjust global.json (2dog never edits it)", "global.json");
            else if (pin != null)
                yield return Finding.Pass("env.global-json", c, $"global.json {pin} ({roll})");
        }

        if (project.HasWebLikeHost)
        {
            var folders = string.Join(", ", project.Hosts.Where(h => h.IsWebLike).Select(h => h.Folder));
            if (ctx.Workloads is not { } workloads)
                yield return new Finding("env.wasm-tools", c, Severity.Info, "could not list workloads",
                    "'dotnet workload list' failed", "run 'dotnet workload list' yourself; the browser hosts need wasm-tools");
            else if (!workloads.Contains("wasm-tools"))
                yield return new Finding("env.wasm-tools", c, Severity.Fail, $"wasm-tools workload missing (needed by {folders})",
                    "browser hosts publish through the .NET WebAssembly SDK", "dotnet workload install wasm-tools");
            else
                yield return Finding.Pass("env.wasm-tools", c, "wasm-tools");
        }

        var rid = Rid(ctx.Env);
        if (SupportedRids.Contains(rid))
            yield return Finding.Pass("env.host-platform", c, rid);
        else
            yield return new Finding("env.host-platform", c, Severity.Fail, $"no 2dog native packages for {rid}",
                $"supported: {string.Join(", ", SupportedRids)}", "build on a supported platform, or build the natives yourself");

        if (ctx.Env.Var("GODOT_EDITOR") is { Length: > 0 } editor)
            yield return ctx.Env.FileExists(editor)
                ? Finding.Pass("env.godot-editor", c, "GODOT_EDITOR set")
                : new Finding("env.godot-editor", c, Severity.Warn, $"GODOT_EDITOR points at a missing file: {editor}",
                    "imports and exports fall back to the 2dog editor packages", "fix or unset GODOT_EDITOR");

        foreach (var (name, check, what) in new (string, Func<string, bool>, string)[]
                 {
                     ("GODOTSHARP_DIR", dir => ctx.Env.FileExists(Path.Combine(dir, "GodotPlugins.dll")), "contains no GodotPlugins.dll"),
                     ("GODOT_TOOLS_DIR", ctx.Env.DirectoryExists, "is not a directory"),
                     ("GODOT_PROJECT_ASSEMBLY_DIR", ctx.Env.DirectoryExists, "is not a directory"),
                 })
        {
            if (ctx.Env.Var(name) is not { Length: > 0 } value) continue;
            yield return check(value)
                ? new Finding("env.overrides", c, Severity.Info, $"{name} overrides the package layout ({value})")
                : new Finding("env.overrides", c, Severity.Warn, $"{name} is set but {what}: {value}",
                    "the engine probes this location first", $"unset {name} unless you mean to override the package layout");
        }

        if (ctx.Versions.TryGetValue("TwoDogVersion", out var engine) && ctx.GlobalPackages is { } cache)
        {
            var expected = new List<(string Id, Version Version)> { ("2dog.engine", engine) };
            if (ctx.Versions.TryGetValue("TwoDogNativesVersion", out var natives))
            {
                expected.Add(("2dog.tools", natives));
                if (SupportedRids.Contains(rid)) expected.Add(($"2dog.{rid}.editor", natives));
                if (project.HasWebLikeHost) expected.Add(("2dog.browser-wasm.release", natives));
            }

            var missing = expected.Where(e => !Restored(ctx.Env, cache, e.Id, e.Version)).Select(e => $"{e.Id} {e.Version}").ToList();
            if (missing.Count == 0)
                yield return Finding.Pass("env.packages-restored", c, "packages restored");
            else
                yield return new Finding("env.packages-restored", c, Severity.Warn,
                    $"{missing.Count} package(s) not in the NuGet cache: {string.Join(", ", missing)}",
                    "the import step and the native copy need them at build time", "dotnet restore");
        }
    }

    internal static string Rid(IEnvironment env)
    {
        var os = env.IsWindows ? "win" : env.IsMacOS ? "osx" : "linux";
        var arch = env.Architecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            var other => other.ToString().ToLowerInvariant(),
        };
        return $"{os}-{arch}";
    }

    /// <summary>NuGet lowercases ids and normalizes versions (4.7.2.0 becomes 4.7.2) in the cache layout.</summary>
    private static bool Restored(IEnvironment env, string cache, string id, Version version)
    {
        var folder = Path.Combine(cache, id.ToLowerInvariant());
        string[] candidates = [version.ToString(), Normalize(version)];
        return candidates.Distinct().Any(v => env.DirectoryExists(Path.Combine(folder, v)));
    }

    internal static string Normalize(Version v) =>
        v.Revision == 0 ? $"{v.Major}.{v.Minor}.{v.Build}" : v.ToString();
}
