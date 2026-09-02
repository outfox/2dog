using System.Text.RegularExpressions;

namespace twodog.cli;

/// <summary>A known failure as it appears in a build, restore or runtime log, with the explanation and the way out.</summary>
internal sealed record BuildSignature(
    string Id,
    Regex Pattern,
    string Title,
    string Remedy,
    Severity Severity = Severity.Fail,
    int ContextBefore = 0);

/// <summary>
/// The failure signatures doctor recognizes. The patterns quote the 2dog MSBuild targets and runtime loaders
/// (2dog.engine.targets, 2dog.browser-wasm.targets, 2dog.native-resolver.targets, LibGodotLoader, HostedGodotPlugins)
/// plus the SDK and NuGet errors 2dog projects run into.
/// </summary>
internal static class BuildSignatures
{
    private static Regex Rx(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static readonly BuildSignature[] All =
    [
        new("build.variant-invalid", Rx(@"TwoDog: invalid TwoDogVariant '(?<v>[^']*)'"),
            "TwoDogVariant is not release, debug or editor", "set <TwoDogVariant> to release, debug or editor"),
        new("build.buildtype-deprecated", Rx(@"TwoDogBuildType is deprecated"),
            "the deprecated TwoDogBuildType property is set", "2dog doctor --fix (removes it)", Severity.Warn),
        new("build.publish-aot", Rx(@"PublishAot \(NativeAOT\) is not supported"),
            "PublishAot is not supported for desktop hosts", "remove PublishAot; publish as a folder (dotnet publish -c Release -r <rid>)"),
        new("build.publish-singlefile", Rx(@"PublishSingleFile is not supported"),
            "PublishSingleFile is not supported for desktop hosts", "remove PublishSingleFile; publish as a folder"),
        new("build.godot-sdk-mismatch",
            Rx(@"references GodotSharp (?<stock>\S+) \(via Godot\.NET\.Sdk\) but 2dog\.engine (?<engine>\S+) is built for Godot (?<godot>\S+)"),
            "Godot.NET.Sdk and 2dog.engine are on different Godot lines",
            "2dog update (sets Godot.NET.Sdk and GodotSharpEditor to the engine's line)"),
        new("build.no-import-capability", Rx(@"needs a resource import, but no import capability was found|import required \(TwoDogRequireImport=true\)"),
            "no import capability (2dog.<rid>.editor and 2dog.tools packages missing)",
            "dotnet restore --force (restores the editor and tools packages), or set GODOT_EDITOR / <GodotEditor>", Severity.Warn),
        new("build.desktop-preset-missing", Rx(@"desktop publish exports the game pck via the '(?<preset>[^']+)' export preset"),
            "the desktop export preset is missing from export_presets.cfg", "2dog doctor --fix (appends the preset)"),
        new("build.web-preset-missing", Rx(@"web publish exports the game pck via the '(?<preset>[^']+)' export preset"),
            "the 'Web' export preset is missing from export_presets.cfg", "2dog doctor --fix (appends the preset)"),
        new("build.no-export-capability", Rx(@"publish needs to export '.+' as a \.pck, but no export capability"),
            "no export capability (2dog.<rid>.editor and 2dog.tools packages missing)",
            "dotnet restore --force, or set GODOT_EDITOR / <GodotEditor>"),
        new("build.web-payload-missing", Rx(@"web payload \(libgodot\.a\) not found.*?v(?<v>\S+)"),
            "the browser natives (2dog.browser-wasm) are not restored", "dotnet restore; check that the natives version exists on the feed"),
        new("build.native-missing", Rx(@"2dog: could not locate (?<file>libgodot-\S+) for (?<rid>\S+)"),
            "the native libgodot for this platform was not found", "dotnet restore; rebuild after changing TwoDogVariant", Severity.Warn),
        new("build.nu1213", Rx(@"\bNU1213\b"),
            "the '2dog' tool package is referenced as a library", "reference 2dog.engine (the tool package cannot be a PackageReference)"),
        new("build.package-not-found", Rx(@"NU110[12]: Unable to find package (?<pkg>\S+)(?: with version \(>= (?<v>[^)]+)\))?"),
            "a package version is missing from the feeds", "check nuget.config sources and whether that version was published; 2dog update pins known-good versions"),
        new("build.version-conflict", Rx(@"NU1605|NU1107.*?(GodotSharp|2dog\.)"),
            "conflicting package versions across the solution", "2dog update (aligns every host)"),
        new("build.msb3277-godotsharp", Rx(@"MSB3277.*?GodotSharp"),
            "two GodotSharp versions meet in one build", "2dog update (one Godot line for the SDK and the engine)", Severity.Warn),
        new("build.wasm-tools-missing", Rx(@"NETSDK1147.*?wasm-tools"),
            "the wasm-tools workload is not installed", "dotnet workload install wasm-tools"),
        new("build.sdk-too-old", Rx(@"NETSDK1045"),
            "the installed .NET SDK is too old for net10.0", "install the .NET 10 SDK"),
        new("build.global-json-unresolved", Rx(@"Unable to resolve the \.NET SDK version as specified in the global\.json|A compatible \.NET SDK was not found"),
            "no installed SDK satisfies global.json", "install the pinned SDK band (2dog never edits global.json)"),
        new("build.godot-sdk-not-found", Rx(@"MSB4236: The SDK 'Godot\.NET\.Sdk/(?<v>\S+)'"),
            "the Godot.NET.Sdk version could not be downloaded", "check the feed or the version; 2dog update sets the one this tool ships"),
        new("build.webboot-duplicate", Rx(@"CS0101.*?TwoDogWebBoot|CS0111.*?TwoDogWebBoot"),
            "TwoDogWebBoot.cs compiles twice into the game assembly", "keep one copy (2dog doctor reports which)"),
        new("build.il1035", Rx(@"\bIL1035\b"),
            "the trimmer cannot see a root assembly", "Blazor client: 2dog.engine needs PrivateAssets=\"all\" Publish=\"true\"; check TrimmerRootAssembly"),
        new("build.export-failed", Rx(@"MSB3073: The command "".*?--export-pack.*?"" exited"),
            "the Godot pck export failed", "read the Godot lines above the error; open the project in the editor once", Severity.Fail, 15),
        new("build.godotplugins-missing", Rx(@"GodotPlugins\.dll not found"),
            "GodotPlugins.dll not found at runtime", "rebuild; unset GODOTSHARP_DIR unless the override is intended"),
        new("build.native-variant-missing", Rx(@"could not locate the native libgodot library for TwoDogVariant '(?<v>\w+)'"),
            "the native library for this TwoDogVariant is missing at runtime", "dotnet restore; rebuild after changing TwoDogVariant"),
        new("build.variant-fallback", Rx(@"TwoDogVariant is '(?<v>\w+)' but (?<f>\S+) was not found; falling back"),
            "the engine fell back to another native variant", "rebuild after changing TwoDogVariant so the right native is copied", Severity.Warn),
    ];
}
