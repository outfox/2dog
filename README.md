<p align="center">
  <img src="docs/content/public/logo-animated.svg" alt="2dog logotype, a white stylized dog with the negative space around its leg forming the number 2, and a playful font spelling the word dog" width="70%">
</p>

[![Discord Invite](https://img.shields.io/badge/discord-_%E2%A4%9Coutfox%E2%A4%8F-blue?logo=discord&logoColor=f5f5f5)](https://discord.gg/GAXdbZCNGT)
[![NuGet](https://img.shields.io/nuget/v/2dog?color=blue)](https://www.nuget.org/packages/2dog/)
[![CI](https://github.com/outfox/2dog/actions/workflows/ci.yml/badge.svg)](https://github.com/outfox/2dog/actions/workflows/ci.yml)

# 🦴 2dog is Godot... just backward!

### Godot normally loads .NET, now .NET loads Godot.

**2dog** is available fully precompiled from [NuGet.org](https://www.nuget.org/packages/2dog/) and as [GitHub Releases](https://github.com/outfox/2dog/releases). *(no need to clone this repo)*

**2dog** packages Godot as a library (a slightly modified [`libgodot`](https://github.com/godotengine/godot/pull/110863))
that your .NET application hosts. Keep your Godot project exactly as it is, and run it on the desktop, in the
browser, inside a .NET UI, or under **xUnit and NUnit with a real Godot engine**, all with `dotnet` and your IDE.

📖 End-user documentation: **[2dog.dev](https://2dog.dev)**

## Build and develop locally

Poe is the build entry point; tasks live in [pyproject.toml](pyproject.toml).
`uv run --locked` installs the Python tools from `uv.lock`; [global.json](global.json)
pins the .NET SDK and optional workload set. Install **uv**, **Git LFS**, that .NET SDK,
and your platform's [Godot build dependencies](https://docs.godotengine.org/en/stable/engine_details/development/compiling/index.html).
Configuration checks and releases also need [Nushell](https://www.nushell.sh/).
Configure GitHub SSH access for the Godot submodule before cloning.

On Windows, enable [Developer Mode](https://learn.microsoft.com/windows/advanced-settings/developer-mode)
and run `git config --global core.symlinks true` before cloning.

```bash
git clone --recursive https://github.com/outfox/2dog
cd 2dog
git lfs pull
uv run --locked poe build-all
```

`build-all` builds the host's Godot editor, C# glue and native libraries, then packs
platform packages, bindings, the engine and integrations in dependency order. Packages
land in `packages/`, and the desktop/test solution filter is restored last.

| Command | When to use it |
| --- | --- |
| `uv run --locked poe build-local` | Incremental native work after a full build of the same precision; skips the editor and glue, then repacks. |
| `uv run --locked poe build` | Repack platform and managed packages from existing native outputs. Unbuilt platforms get empty stubs. |
| `uv run --locked poe build-managed` | Repack bindings, engine and integrations using existing platform packages; no C++ build. |

Add `--precision double` to these build tasks for matching double-precision natives
and managed packages. Direct `dotnet` commands then need `-p:TwoDogPrecision=double`.
Native build options go after `--`; for example, to keep an SCons cache and debug symbols:

```bash
uv run --locked poe build-all -- --cache-path temp/scons-cache --debug-symbols yes
```

For managed work without compiling Godot, use
`uv run --locked poe pull-packages --run RUN_ID` with an authenticated GitHub CLI (`gh`).
Replace `RUN_ID` with a successful CI run matching the checkout's Godot pin and package
versions; add `--precision double` if needed. This imports validated packages and matching
SDK/bindings, records CI package versions and restores the desktop/test solution.
After importing, use `build-managed` or direct `dotnet` commands to keep the downloaded
platform packages. Run CI again if its package artifacts have expired.

```bash
dotnet run --project demos/showcase/showcase.2dog
dotnet test 2dog.tests.slnf
uv run --locked poe test-build-configuration
```

Install optional workloads with `uv run --locked poe install-workloads wasm-tools`
(substitute `android` or `ios maui-ios` as needed), using the committed workload-set pin.
`uv run --locked poe --help` lists the mobile build tasks and other commands.

## Release

After changing the Godot submodule, run `uv run --locked poe sync-versions` and commit
its version/pin updates with the submodule bump. This sync does not create a release.
Dispatch the release from the terminal with an authenticated GitHub CLI, or run
**2dog Release** on `main` in GitHub Actions:

```bash
uv run --locked poe release --dry-run       # preview both release tags in Actions
uv run --locked poe release                 # release all configured lines
uv run --locked poe release --lines 4.7     # release only the 4.7 line
```

The workflow queues release requests and allocates one revision from the tags on
`origin` for each batch. It signs every release commit and tag, protects the Godot
fork pins, and atomically pushes all release tags. It advances `main` only when you
select the line checked out there. [godot-lines.json](godot-lines.json) defines the
other lines, and each line reuses its native revision while its inputs match.

For workflow setup, register a dedicated SSH signing public key on the signer
account and add the private key as the repository secret `RELEASE_SIGNING_KEY`.
Set `RELEASE_SIGNING_EMAIL` to that account's verified email. Register a separate
write deploy key on `outfox/godot` and store its private key in the 2dog repository
secret `GODOT_RELEASE_DEPLOY_KEY`. Run a dry run after setup to check that the
signing key loads and that the runner can connect to the fork for a push.

Each tag starts its own CI run. If a dispatch fails, run the retry command from
the release workflow log to resume CI for the existing tag.

CI calls the same Poe recipes, reuses verified native builds by input fingerprint,
and uses Blacksmith caches and sticky disks. As soon as packing finishes, it stages a complete
package set on a GitHub prerelease and uploads it to **int.nugettest.org**, so manual testing
can start while both precisions are tested. The independent iOS Release/AOT check runs on the
staged packages. Promotion requires the whole tag run to succeed, including its Release Gate
(the test verdicts) and that iOS check. Promote one or more tags with an authenticated GitHub CLI:

```bash
uv run poe promote v4.7.2.189 v4.8.0.188-beta3
```

For CI on another line without releasing, run
`uv run --locked poe release --ci --line 4.7`; delete its temporary `ci/4.7/<commit>`
branch after the run.

## Dogs and robots are nice

🦴♥️👾 **2dog** is proudly made by human maintainers and contributors. We permit extensive use of LLMs:

- Forked `libgodot` features/fixes consist of similar amounts of human-written and machine-written code
- Commits/PRs are reviewed & gated by both humans and machines (over 90%/90%) and signed by humans
- .NET Host applications are mostly human-written, while boilerplate assets (e.g. XAML) are mostly machine-written
- Configurations, tools, smoke tests, CSS, MSBuild XML, and CI workflows are overwhelmingly LLM-maintained
- Documentation aims to be human-authored, but new feature docs are generated and later gradually rewritten
- Releases on NuGet use Trusted Publishing and may only be invoked via direct human interaction

## Acknowledgements

Continuous Integration compute graciously supported by [Blacksmith](https://blacksmith.sh).
