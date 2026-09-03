---
title: 2dog doctor
description: "Reference for 2dog doctor: check a 2dog project and the machine, apply fixes, explain build failures - options, exit codes, every check id."
---

# `2dog doctor`

Checks the project and this machine without building, and applies the fixes
it can. Static by default; works offline. Fix classes, the interactive
checklist and a sample run: [Doctor and Update](/doctor).

```bash
2dog doctor [path] [options]
```

| Argument | Meaning |
| --- | --- |
| `path` | Directory containing `project.godot`; defaults to the current directory |

## Options

| Option | Effect |
| --- | --- |
| `--fix` | Apply the safe fixes, then check again |
| `--fix-all` | Also apply the announced fixes: solution migration, target framework, bootstrap refresh |
| `--build [target]` | Run `dotnet build` of the solution, or a host folder or project, and explain known failures |
| `-c, --configuration <Cfg>` | Configuration for `--build` (default `Debug`) |
| `--log <file>` | Only explain an existing build, restore or runtime log; `-` reads stdin |
| `--ignore <id>` | Drop a finding by [check id](#checks); repeatable |
| `--strict` | Warnings count as findings for the exit code |
| `--offline` | Skip the nuget.org check for a newer tool |
| `--list-checks` | Print every check and build-log signature |

Plus the [global and output options](/dnx-2dog#global-options); `-v` lists
the passed checks too.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Only informational items remain |
| `2` | Doctor could not run: no `project.godot`, unreadable files |
| `3` | Errors remain (warnings too under `--strict`), or the `--build` failed |

## Examples

```bash
2dog doctor                                         # check, then ask which fixes to apply
2dog doctor --fix                                   # apply the safe fixes unattended
2dog doctor --build MyGame.web -c Release           # build one host and explain failures
2dog doctor --log build.log                         # explain an existing log
dotnet build 2>&1 | 2dog doctor --log -             # or a piped one
2dog doctor --json --strict | jq '.doctor.summary'  # CI: exit 3 on any warning
```

## Checks

Ids are stable; `--ignore <id>` drops one.

### Environment

| Id | Passes when |
| --- | --- |
| `env.dotnet-sdk` | a .NET 10 SDK is installed |
| `env.global-json` | the root global.json pin is satisfied by an installed SDK |
| `env.wasm-tools` | the wasm-tools workload is installed when a browser host exists |
| `env.host-platform` | this OS and architecture have 2dog native packages |
| `env.godot-editor` | GODOT_EDITOR, when set, points at an existing file |
| `env.overrides` | GODOTSHARP_DIR and the other layout overrides point at what they claim |
| `env.packages-restored` | the engine, tools and native packages are in the NuGet cache |

### Layout

| Id | Passes when |
| --- | --- |
| `layout.load-problems` | every project file parses |
| `layout.game-csproj` | the game csproj exists at the project root |
| `layout.assembly-name` | project.godot names the assembly and the csproj matches it |
| `layout.multiple-root-csproj` | a single csproj at the root, or assembly_name picks one |
| `layout.spaced-name` | the .NET project name contains no whitespace |
| `layout.legacy-root-webboot` | TwoDogWebBoot.cs lives in a web host folder, not at the root |
| `layout.root-build-targets` | Directory.Build.targets provides the deep-clean target |
| `layout.root-build-props` | Directory.Build.props holds the shared package versions |
| `layout.root-global-json` | global.json pins a wasm-capable SDK when a browser host exists |

### Game csproj

| Id | Passes when |
| --- | --- |
| `game.sdk` | the game csproj uses Godot.NET.Sdk |
| `game.sdk-mismatch` | Godot.NET.Sdk matches the Godot line 2dog.engine was built for |
| `game.target-framework` | the game csproj targets net10.0 |
| `game.properties` | EnableDynamicLoading, AllowUnsafeBlocks and LIBGODOT_ENABLED are set |
| `game.default-item-excludes` | every host folder is excluded from the game's default globs |
| `game.webboot-include` | the web bootstrap is compiled into the game assembly |
| `game.webboot-duplicate` | exactly one TwoDogWebBoot.cs compiles into the game assembly |

### Hosts

| Id | Passes when |
| --- | --- |
| `host.gdignore` | every host folder carries a .gdignore |
| `host.project-reference` | every ProjectReference points at an existing project |
| `host.godot-project-dir` | GodotProjectDir points at the Godot project |
| `host.variant` | TwoDogVariant is release, debug or editor |
| `host.buildtype-deprecated` | the deprecated TwoDogBuildType property is gone |
| `host.publish-aot` | no desktop host enables PublishAot or PublishSingleFile |
| `host.duplicate-analyzers` | hosts referencing the game strip the duplicate Godot analyzers |
| `host.app-manifest` | the app.manifest a host declares exists |
| `host.web-props-shim` | browser hosts chain to the root Directory.Build.props |
| `host.web-global-json` | browser hosts carry their own global.json |
| `host.webboot-drift` | the tool-owned TwoDogWebBoot.cs matches this tool's copy |
| `host.trimmer-root` | browser hosts root the game assembly for the trimmer |
| `host.blazor-client` | the Blazor client project exists and publishes 2dog.engine |
| `host.windows-only` | Windows-only hosts are noted on other platforms |

### Solution

| Id | Passes when |
| --- | --- |
| `sln.exists` | a solution exists at the project root |
| `sln.multiple` | exactly one solution contains the game project |
| `sln.legacy-format` | the solution uses the .slnx format |
| `sln.contains-game` | the solution lists the game project |
| `sln.contains-hosts` | the solution lists every host project |
| `sln.build-exclusions` | browser and WinUI hosts are excluded from plain solution builds |

### Versions

| Id | Passes when |
| --- | --- |
| `ver.managed-elsewhere` | versions expressed through your own properties are noted, not judged |
| `ver.props-invalid` | the TwoDog* versions in Directory.Build.props are readable versions |
| `ver.literal-versions` | host csprojs use the shared version properties, not literals |
| `ver.twodog-consistent` | every 2dog package uses the same version |
| `ver.twodog-outdated` | the 2dog packages are at this tool's version |
| `ver.twodog-newer` | the project is not ahead of this tool |
| `ver.natives` | the native packages are exact-pinned on the engine's Godot line |
| `ver.godot-line-consistent` | TwoDogGodotVersion matches the game project's Godot.NET.Sdk |
| `ver.godotsharp-editor` | GodotSharpEditor in the test host matches the Godot.NET.Sdk |
| `ver.companions` | Avalonia and the other companion packages are not mixed |
| `ver.tool-latest` | a newer 2dog tool on nuget.org is mentioned |

### Presets

| Id | Passes when |
| --- | --- |
| `preset.file` | export_presets.cfg exists |
| `preset.web` | the 'Web' preset exists when a browser host does |
| `preset.desktop` | the per-OS desktop presets exist |

### Godot settings

| Id | Passes when |
| --- | --- |
| `godot.features-line` | config/features names the Godot line the SDK targets |
| `godot.xr-shaders` | a WebXR host has xr/shaders/enabled.web set |
| `godot.import-stamp` | the project has been imported at least once |

## Build-log signatures

Failures `--build` and `--log` recognize. A match gets a title, the offending
line and the fixing command; other errors follow.

| Id | Failure |
| --- | --- |
| `build.variant-invalid` | TwoDogVariant is not release, debug or editor |
| `build.buildtype-deprecated` | the deprecated TwoDogBuildType property is set |
| `build.publish-aot` | PublishAot is not supported for desktop hosts |
| `build.publish-singlefile` | PublishSingleFile is not supported for desktop hosts |
| `build.godot-sdk-mismatch` | Godot.NET.Sdk and 2dog.engine are on different Godot lines |
| `build.no-import-capability` | no import capability (`2dog.<rid>.editor` and 2dog.tools packages missing) |
| `build.import-required` | the build requires an import (TwoDogRequireImport=true) and has no import capability |
| `build.desktop-preset-missing` | the desktop export preset is missing from export_presets.cfg |
| `build.web-preset-missing` | the 'Web' export preset is missing from export_presets.cfg |
| `build.no-export-capability` | no export capability (`2dog.<rid>.editor` and 2dog.tools packages missing) |
| `build.web-payload-missing` | the browser natives (2dog.browser-wasm) are not restored |
| `build.native-missing` | the native libgodot for this platform was not found |
| `build.nu1213` | the '2dog' tool package is referenced as a library |
| `build.package-not-found` | a package version is missing from the feeds |
| `build.version-conflict` | conflicting package versions across the solution |
| `build.msb3277-godotsharp` | two GodotSharp versions meet in one build |
| `build.wasm-tools-missing` | the wasm-tools workload is not installed |
| `build.sdk-too-old` | the installed .NET SDK is too old for net10.0 |
| `build.global-json-unresolved` | no installed SDK satisfies global.json |
| `build.godot-sdk-not-found` | the Godot.NET.Sdk version could not be downloaded |
| `build.webboot-duplicate` | TwoDogWebBoot.cs compiles twice into the game assembly |
| `build.il1035` | the trimmer cannot see a root assembly |
| `build.export-failed` | the Godot pck export failed |
| `build.godotplugins-missing` | GodotPlugins.dll not found at runtime |
| `build.native-variant-missing` | the native library for this TwoDogVariant is missing at runtime |
| `build.variant-fallback` | the engine fell back to another native variant |
