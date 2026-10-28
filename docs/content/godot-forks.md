---
title: Godot Forks
description: "Merge your Godot fork into 2dog, build matching engine and C# packages, and configure NuGet to use them locally."
tagline: "Bring your Godot fork to 2dog and make sure your game runs it."
keywords: "Godot fork, custom Godot engine, libgodot, local NuGet packages"
---

# Godot Forks

If your game uses a custom Godot fork, 2dog needs those engine changes too.
Merge your fork into the `godot/` submodule of a 2dog checkout, build the native
engine and matching C# bindings, then point your game's NuGet configuration at
the resulting packages.

2dog loads its engine from the native NuGet packages. Installing your fork's
editor or export templates does not change the engine a 2dog host runs.

## Prepare the checkout

Install [uv](https://docs.astral.sh/uv/getting-started/installation/), the .NET SDK
selected by the repository's
[`global.json`](https://github.com/outfox/2dog/tree/main/global.json), and the
[Godot build prerequisites](https://docs.godotengine.org/en/stable/contributing/development/compiling/index.html)
for your operating system. `uv` manages Python, SCons and Poe for this checkout.
The desktop packages target Windows x64, Linux x64 and macOS arm64.

Clone 2dog with its submodule:

```bash
git clone --recursive https://github.com/outfox/2dog
cd 2dog
```

The submodule URL uses GitHub SSH. If you use HTTPS instead, clone without
`--recursive`, then run these commands inside `2dog/`:

```bash
git submodule init
git config submodule.godot.url https://github.com/thygrrr/godot.git
git submodule update --recursive
```

On Windows, enable Developer Mode and `core.symlinks` before cloning; see the
[contributor instructions](https://github.com/outfox/2dog/tree/main/README.md#compile-your-own-teach-2dog-new-tricks-contributors).

## Merge your engine changes

Start from the submodule commit pinned by your 2dog checkout. It already contains
the libgodot changes that let .NET host the engine. Replacing it with an ordinary
Godot checkout loses those changes.

From `2dog/`, create a branch inside the submodule and merge your fork into it.
Replace the URL and `my-fork-branch` with your own:

```bash
cd godot
git switch -c my-2dog-fork
git remote add my-fork https://github.com/YOUR-ACCOUNT/godot.git
git fetch my-fork
git merge my-fork/my-fork-branch
cd ..
```

Resolve conflicts so both your fork's changes and 2dog's libgodot integration
survive. Use a compatible Godot base; merging a different Godot release can
require porting changes. Keep `GodotVersion` in the outer
[`Directory.Build.props`](https://github.com/outfox/2dog/tree/main/Directory.Build.props)
consistent with `godot/version.py` and the generated Godot SDK package version.

When saving or sharing the result, commit and push the merged engine branch to
your own Godot fork, then record its submodule pointer in your 2dog fork. Ensure
that fork's `.gitmodules` URL lets collaborators fetch the merged commit.

## Build your packages

Give your builds distinct package versions so NuGet can distinguish them from
public releases and earlier fork builds. In the outer `Directory.Build.props`,
replace the existing `TwoDogVersion` and `NativesVersion` values, for example:

```xml
<TwoDogVersion>:2dog-version:-myfork.1</TwoDogVersion>
<NativesVersion>:natives-version:-myfork.1</NativesVersion>
```

Keep `GodotVersion` at the engine's SDK version. The fork suffix above versions
the 2dog packages, not `Godot.NET.Sdk`.

Run the first full build from `2dog/`:

```bash
uv run poe build-all
```

This builds the editor executable, generates Mono glue and C# assemblies, builds
libgodot's editor/debug/release variants for your desktop platform, and packs
the .NET libraries and tools. The local feeds are:

| Directory | Packages |
| --- | --- |
| `2dog/packages/` | `2dog`, `2dog.engine`, bindings, integrations and native packages |
| `2dog/godot/bin/GodotSharp/Tools/nupkgs/` | `Godot.NET.Sdk`, `Godot.SourceGenerators` and GodotSharp packages |

The checkout's `nuget.config` already lists both feeds and nuget.org.

::: warning Build each target you intend to ship
`build-all` builds native code for the current platform. Packaging also creates
empty stubs for unbuilt desktop platforms so the engine's dependencies restore
locally. Those stubs cannot run your game on another operating system. Build
matching natives there before distributing packages for it.
:::

## Make your game use the local feeds

Adding a local feed alone is insufficient: NuGet can select a public package
with the same ID/version, or reuse a cached copy. Feed order does not give local
packages priority. Use
[package source mapping](https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping)
and a fresh package cache.

Create or update `nuget.config` in your **game's root**, above every host project.
Replace `/absolute/path/to/2dog` with your checkout's absolute path (forward
slashes also work on Windows). Merge any additional feeds your game needs into
this configuration:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <config>
    <add key="globalPackagesFolder" value=".packages-local-fork/" />
  </config>
  <packageSources>
    <clear />
    <add key="2dog Packages (local)" value="/absolute/path/to/2dog/packages/" />
    <add key="Godot Packages (local)" value="/absolute/path/to/2dog/godot/bin/GodotSharp/Tools/nupkgs/" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="2dog Packages (local)">
      <package pattern="2dog" />
      <package pattern="2dog.*" />
    </packageSource>
    <packageSource key="Godot Packages (local)">
      <package pattern="Godot.*" />
      <package pattern="GodotSharp*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

The more specific patterns beat `*`. A missing local 2dog or Godot package now
fails restore instead of silently falling back to nuget.org. Other dependencies
still come from nuget.org. Source mapping covers transitive dependencies too.

Add `.packages-local-fork/` to the game's `.gitignore`. Start with an unused
cache directory: packages already in the global packages folder bypass source
mapping. Check for `NUGET_PACKAGES` or `RestorePackagesPath` overrides if NuGet
still uses a different cache.

## Set up the hosts and pin versions

To add a host, run the CLI from your source checkout while your working directory
is the **game's root**:

```bash
dotnet run --project /absolute/path/to/2dog/twodog -- add --generic
```

Use `--gdunit`, `--xunit` or another [host flag](/dnx-2dog#host-flags) as needed.
This CLI carries your checkout's package versions. A plain `dnx 2dog` selects a
published tool independently of the versions in your project; see
[version pinning](/cli/pinning).

For existing hosts, set these values in the **game's** `Directory.Build.props`
to match the packages you built:

```xml
<PropertyGroup Label="2dog">
  <TwoDogVersion>:2dog-version:-myfork.1</TwoDogVersion>
  <TwoDogNativesVersion>:natives-version:-myfork.1</TwoDogNativesVersion>
  <TwoDogGodotVersion>:godot-version:</TwoDogGodotVersion>
</PropertyGroup>
```

Keep the other properties in that file. The producer calls its native version
`NativesVersion`; the consumer calls it `TwoDogNativesVersion`. The engine
package already pins its native dependencies to the version it was packed
against, so changing only the consumer property cannot retarget an existing
engine package.

Make **every existing 2dog package reference** exact, in the game, hosts and any
Godot-dependent libraries. For example, the game references the bindings and
the generic host references the engine:

```xml
<PackageReference Include="2dog.godotsharp" Version="[$(TwoDogVersion)]" />
<PackageReference Include="2dog.engine" Version="[$(TwoDogVersion)]" />
```

Apply the same brackets to references such as `2dog.gdunit`, `2dog.xunit` and
`2dog.godotsharp.editor` wherever you use them; do not add integrations you do
not need. Explicit native package references use `[$(TwoDogNativesVersion)]`.
[Square brackets pin an exact version](https://learn.microsoft.com/en-us/nuget/concepts/package-versioning#version-ranges);
a bare version specifies a minimum.

In `MyGame.csproj`, keep `Sdk="Godot.NET.Sdk/:godot-version:"` aligned with the
SDK package in the Godot local feed. Rebuild all projects that use Godot APIs
against your fork's bindings.

## Restore and verify the running engine

From the game's root:

```bash
dotnet restore --force-evaluate
dotnet list MyGame.2dog package --include-transitive
dotnet run --project MyGame.2dog --no-restore -- --verbose
```

Check the resolved `2dog.engine`, bindings and platform package versions. For
each, `.nupkg.metadata` in `.packages-local-fork/<package-id>/<version>/` records
the source. Confirm it points to your local feed. The host's startup banner
also prints the Godot commit; compare it with:

```bash
git -C /absolute/path/to/2dog/godot rev-parse --short HEAD
```

Then exercise an API or behavior specific to your fork.
The CLI's `version` command describes the tool and project references; it does
not prove which native binary the host loaded.

## Rebuild while developing

After a successful full build, use this loop from `2dog/` for native changes
that do not change the exposed API:

```bash
uv run poe build-local
```

`build-local` incrementally rebuilds host libgodot variants and repacks the
packages. It skips rebuilding the editor executable and generating Mono glue.
If you add or change exposed classes, methods, variants or C# bindings, run
`uv run poe build-all` again. `build-all` already packages everything; it does
not require a follow-up `build-local`. To only repack existing native outputs,
use `uv run poe build`.

Bump both fork package versions before a new engine build, then update the
consumer's versions. If you repack under the same ID/version, remove only the
affected package/version directories from both the checkout's `.packages/` and
the game's `.packages-local-fork/` before restoring. Include cached Godot SDK
and source-generator packages if they changed. `--force-evaluate` and
`--no-http-cache` do not evict already extracted packages.

## When the fork seems to be missing

- **Missing custom GDScript methods or GDExtension interface functions:** first
  verify the resolved packages and running engine commit. An error such as
  `Attempt to get non-existent interface function` during extension
  initialization means the library was opened but the engine lacks that API.
  Check the merge and rebuild extensions against the same fork and precision.
- **Missing native method binds, including editor methods:** check that native
  variants and managed bindings came from the same full build. Editor APIs
  additionally require the [Editor variant](/build-configurations#editor-variants):
  `dotnet run --project MyGame.2dog -c Editor -- --editor`. This does not repair
  mismatched bindings or a missing method in the fork.
- **Restore hangs or keeps old behavior:** check the effective NuGet config,
  source paths and extracted package cache. Complete restore separately before
  using `--no-restore`; skipping restore alone does not fix wrong packages.
- **A GDExtension library cannot be opened:** check its `.gdextension` paths,
  architecture and packaged native dependencies. Symlinked files and directories
  need separate investigation. Do not infer a macOS `.framework` loading problem
  from an error that already reached the extension's initialization function.
