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
        new("env.wasm-tools", Category.Environment, "the wasm-tools workload is available for selected browser builds"),
        new("env.android-workload", Category.Environment, "the android workload is available for selected Android builds"),
        new("env.maui-workload", Category.Environment, "MAUI workloads are available for selected MAUI builds"),
        new("env.android-sdk", Category.Environment, "Android SDK paths and components are available for selected Android builds"),
        new("env.qt-toolchain", Category.Environment, "CMake, Ninja and a C++ compiler are available for Qt hosts"),
        new("env.ios-workload", Category.Environment, "the ios workload is available for selected iOS builds"),
        new("env.xcode", Category.Environment, "Xcode is installed on this Mac for iOS builds; other OSes are told iOS builds need a Mac"),
        new("env.host-platform", Category.Environment, "this OS and architecture have 2dog native packages"),
        new("env.godot4", Category.Environment, "GODOT4 points at an existing Godot executable for IDE debugging"),
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

        foreach (var finding in WorkloadFindings(ctx, Workload.WasmTools, "env.wasm-tools",
                     project.Hosts.Where(h => h.IsWebLike), "browser hosts publish through the .NET WebAssembly SDK",
                     ctx.Options.InstallWasmTools))
            yield return finding;
        foreach (var finding in WorkloadFindings(ctx, Workload.Android, "env.android-workload",
                     project.Hosts.Where(h => h.Kind == HostKind.Android), "Android hosts build with .NET for Android",
                     ctx.Options.InstallAndroidWorkload))
            yield return finding;

        foreach (var workload in Workload.For([HostKind.Maui]))
        foreach (var finding in WorkloadFindings(ctx, workload, "env.maui-workload",
                     project.Hosts.Where(h => h.Kind == HostKind.Maui), "MAUI hosts need platform MAUI workloads", false))
            yield return finding;

        foreach (var finding in WorkloadFindings(ctx, Workload.Ios, "env.ios-workload",
                     project.Hosts.Where(h => h.Kind == HostKind.Ios), "iOS hosts build with .NET for iOS", false))
            yield return finding;

        if (project.Hosts.Any(h => h.Kind == HostKind.Ios) || (ctx.Env.IsMacOS && project.Hosts.Any(h => h.Kind == HostKind.Maui)))
        {
            var iosHosts = string.Join(", ", project.Hosts.Where(h => h.Kind is HostKind.Ios or HostKind.Maui).Select(h => h.Folder));
            if (!ctx.Env.IsMacOS)
                yield return new Finding("env.xcode", c, Severity.Info, $"iOS builds of {iosHosts} need a Mac with Xcode",
                    "the iOS SDK, clang, codesign and Mono's AOT compiler ship with Xcode; Linux cannot build iOS apps",
                    "build on a Mac, use Pair to Mac from Windows, or a macOS CI runner");
            else if (Xcode(ctx.Env) is { } developerDir)
                yield return Finding.Pass("env.xcode", c, $"Xcode ({developerDir})");
            else
                yield return new Finding("env.xcode", c, ctx.BuildHosts.Any(h => h.Kind is HostKind.Ios or HostKind.Maui) ? Severity.Fail : Severity.Warn,
                    "Xcode not found", $"iOS builds of {iosHosts} link with its iOS SDK and sign with its codesign",
                    "install Xcode with the iOS platform from the App Store, or point DEVELOPER_DIR at an Xcode.app/Contents/Developer");
        }

        if (project.Hosts.Any(h => h.Kind is HostKind.Android or HostKind.Maui))
        {
            var sdk = AndroidSdk.Inspect(ctx.Env);
            var required = ctx.BuildHosts.Any(h => h.Kind is HostKind.Android or HostKind.Maui);
            foreach (var warning in sdk.Warnings)
                yield return new Finding("env.android-sdk", c, required ? sdk.Directory is null ? Severity.Fail : Severity.Warn : Severity.Info, warning,
                    Remedy: $"Android SDK setup: {AndroidSdk.SetupUrl}");
            if (sdk.Warnings.Count == 0)
                yield return Finding.Pass("env.android-sdk", c, $"Android SDK ({sdk.Directory})");
        }

        if (project.Hosts.Where(h => h.Kind == HostKind.Qt).ToList() is { Count: > 0 } qtHosts)
        {
            var missing = MissingQtToolchain(ctx.Env, ctx.Runner, project.Dir);
            var folders = string.Join(", ", qtHosts.Select(h => h.Folder));
            var required = qtHosts.Any(ctx.BuildHosts.Contains);
            yield return missing.Count == 0
                ? Finding.Pass("env.qt-toolchain", c, "CMake, Ninja and a C++ compiler")
                : new Finding("env.qt-toolchain", c, required ? Severity.Fail : Severity.Info,
                    $"{string.Join(", ", missing)} missing (needed by {folders})",
                    required ? "Qt Bridge for C# and the TwoDog QML module compile native code with CMake" : $"optional until building {folders}",
                    ctx.Env.IsWindows
                        ? "install Visual Studio or its Build Tools with the 'Desktop development with C++' workload (it includes CMake and Ninja)"
                        : "install cmake, ninja and a C++ compiler (e.g. apt install cmake ninja-build g++, or brew install cmake ninja with the Xcode command line tools)");
        }

        var rid = Rid(ctx.Env);
        if (SupportedRids.Contains(rid))
            yield return Finding.Pass("env.host-platform", c, rid);
        else
            yield return new Finding("env.host-platform", c, Severity.Fail, $"no 2dog native packages for {rid}",
                $"supported: {string.Join(", ", SupportedRids)}", "build on a supported platform, or build the natives yourself");

        yield return Godot4Finding(ctx.Env);

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
                if (ctx.BuildHosts.Any(h => h.IsWebLike)) expected.Add(("2dog.browser-wasm.release", natives));
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

    /// <summary>A workload the project's hosts need: missing, unknown (listing failed) or present.</summary>
    private static IEnumerable<Finding> WorkloadFindings(DoctorContext ctx, Workload workload, string id,
        IEnumerable<HostModel> hosts, string why, bool install)
    {
        const Category c = Category.Environment;
        var hostList = hosts.ToList();
        var folders = string.Join(", ", hostList.Select(h => h.Folder));
        if (folders.Length == 0) yield break;
        var required = install || hostList.Any(ctx.BuildHosts.Contains);
        if (ctx.Workloads is not { } workloads)
            yield return new Finding(id, c, install ? Severity.Fail : Severity.Info,
                "could not list workloads", "'dotnet workload list' failed",
                install ? workload.InstallCommand : $"run 'dotnet workload list' yourself; the {workload.NeededBy} need {workload.Id}",
                Fix: install ? InstallWorkload(ctx, workload) : null);
        else if (!workloads.Contains(workload.Id))
            yield return new Finding(id, c, required ? Severity.Fail : Severity.Info, $"{workload.Id} workload missing (needed by {folders})",
                required ? why : $"optional until building or publishing {folders}", workload.InstallCommand,
                Fix: required ? InstallWorkload(ctx, workload) : null);
        else
            yield return Finding.Pass(id, c, workload.Id);
    }

    internal static Finding Godot4Finding(IEnvironment env)
    {
        const string id = "env.godot4";
        const string remedy = "set GODOT4 to the full path of the main .NET-enabled Godot executable (not the console wrapper), then restart your IDE";
        var path = env.Var("GODOT4");
        if (string.IsNullOrWhiteSpace(path))
            return new Finding(id, Category.Environment, Severity.Warn, "GODOT4 is not set",
                "the template's VS Code launch configuration uses GODOT4 to launch Godot", remedy);
        if (!env.FileExists(path))
            return new Finding(id, Category.Environment, Severity.Warn, $"GODOT4 points at a missing file: {path}",
                "IDE debugging cannot launch this path", remedy);
        return Finding.Pass(id, Category.Environment, $"GODOT4 executable found ({path})");
    }

    private static Fix InstallWorkload(DoctorContext ctx, Workload workload) =>
        new($"env:{workload.Id}", FixClass.Announced, $"install {workload.Id} ({workload.InstallCommand})", () =>
        {
            Workloads.Install(workload, ctx.Project.Dir, ctx.Runner);
            ctx.InvalidateWorkloads();
        });

    /// <summary>
    /// What a Qt host's native build cannot find. Qt Bridge runs CMake with Ninja: on Windows inside Visual Studio's
    /// vcvars (MSVC, whose C++ CMake tools also bring CMake and Ninja), elsewhere from PATH.
    /// </summary>
    internal static List<string> MissingQtToolchain(IEnvironment env, IProcessRunner runner, string workingDir)
    {
        var missing = new List<string>();
        string? cmakeTools = null;
        if (env.IsWindows)
        {
            if (VisualStudioWithCpp(env, runner, workingDir) is { } vs)
                cmakeTools = Path.Combine(vs, "Common7", "IDE", "CommonExtensions", "Microsoft", "CMake");
            else
                missing.Add("Visual Studio C++ tools");
        }
        else if (!OnPath(env, "c++") && !OnPath(env, "g++") && !OnPath(env, "clang++"))
            missing.Add("a C++ compiler");

        if (!OnPath(env, "cmake") && !(cmakeTools != null && env.FileExists(Path.Combine(cmakeTools, "CMake", "bin", "cmake.exe"))))
            missing.Add("CMake");
        if (!OnPath(env, "ninja") && !(cmakeTools != null && env.FileExists(Path.Combine(cmakeTools, "Ninja", "ninja.exe"))))
            missing.Add("Ninja");
        return missing;
    }

    private static bool OnPath(IEnvironment env, string tool) =>
        (env.Var("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Any(dir => env.FileExists(Path.Combine(dir, env.IsWindows ? tool + ".exe" : tool)));

    /// <summary>The newest Visual Studio installation with the C++ desktop workload, via vswhere; null when none.</summary>
    private static string? VisualStudioWithCpp(IEnvironment env, IProcessRunner runner, string workingDir)
    {
        if (env.Var("ProgramFiles(x86)") is not { Length: > 0 } programFiles) return null;
        var vswhere = Path.Combine(programFiles, "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!env.FileExists(vswhere)) return null;
        try
        {
            var result = runner.Run(new ProcessRequest(vswhere,
                ["-latest", "-products", "*", "-requires", "Microsoft.VisualStudio.ComponentGroup.NativeDesktop.Core",
                 "-property", "installationPath"], workingDir, TimeSpan.FromSeconds(30)), Cancellation.Token);
            return result.Ok ? result.Output.Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0 && env.DirectoryExists(l)) : null;
        }
        catch (ToolException)
        {
            return null;
        }
    }

    /// <summary>The Xcode developer directory on a Mac: DEVELOPER_DIR, or the App Store install location.</summary>
    internal static string? Xcode(IEnvironment env)
    {
        if (env.Var("DEVELOPER_DIR") is { Length: > 0 } developerDir)
            return env.DirectoryExists(Path.Combine(developerDir, "Platforms", "iPhoneOS.platform")) ? developerDir : null;
        const string appStore = "/Applications/Xcode.app/Contents/Developer";
        return env.DirectoryExists(Path.Combine(appStore, "Platforms", "iPhoneOS.platform")) ? appStore : null;
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
