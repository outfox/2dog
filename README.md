<p align="center">
  <img src="docs/content/public/logo-animated.svg" alt="2dog logotype, a white stylized dog with the negative space around its leg forming the number 2, and a playful font spelling the word dog" width="70%">
</p>

[![Discord Invite](https://img.shields.io/badge/discord-_%E2%A4%9Coutfox%E2%A4%8F-blue?logo=discord&logoColor=f5f5f5)](https://discord.gg/GAXdbZCNGT)
[![NuGet](https://img.shields.io/nuget/v/2dog?color=blue)](https://www.nuget.org/packages/2dog/)
[![CI](https://github.com/outfox/2dog/actions/workflows/ci.yml/badge.svg)](https://github.com/outfox/2dog/actions/workflows/ci.yml)

# 🦴 2dog is Godot... just backward!

Godot normally loads .NET, now .NET loads Godot.

2dog packages Godot as a library (a slightly modified [`libgodot`](https://github.com/godotengine/godot/pull/110863)) that .NET applications can host. Your game will run on the desktop, in a browser, inside a .NET UI, or under **xUnit and NUnit with a real Godot engine**. All through the convenient `dotnet` tooling or your IDE of choice.

Oh btw., this means you can [export Godot C# to the web](https://2dog.dev/web.html) using 2dog.

2dog ships with pre-built native libraries, so you do not have to compile Godot yourself.

## One Godot project, plenty of ways to run

Keep your scenes, scripts and assets as you had them, in your normal Godot project. No change needed.

### Easy to use dotnet tool: just run `dnx 2dog add`

The tool adds hosts you want as small 'sidecar' projects, each with its own .NET entry point:

| Host | Purpose | Argument |
| --- | --- | --- |
| [Desktop](https://2dog.dev/hosts/generic) | Run Godot from your own `Main` on Windows, Linux and macOS. | `--generic` |
| [Web](https://2dog.dev/hosts/web) | Publish your Godot C# game to the browser with WebAssembly. | `--web` |
| [WebXR](https://2dog.dev/hosts/webxr) | Take your game into browser VR and AR. | `--webxr` |
| [Avalonia](https://2dog.dev/hosts/avalonia) | Embed Godot in a cross-platform desktop UI, with controls over the game. | `--avalonia` |
| [Blazor](https://2dog.dev/hosts/blazor) | Put Godot in a Blazor page; access game objects from Razor components. | `--blazor` |
| [WinForms](https://2dog.dev/hosts/winforms) | Embed Godot in a Windows Forms application. | `--winforms` |
| [WinUI 3](https://2dog.dev/hosts/winui) | Embed Godot in a Windows App SDK application. | `--winui` |
| [Android](https://2dog.dev/hosts/android) · experimental | Build an APK with `dotnet publish`. | `--android` |
| [xUnit](https://2dog.dev/hosts/xunit) | Test your game with Godot fixtures and xUnit collections. | `--xunit` |
| [NUnit](https://2dog.dev/hosts/nunit) | Test your game with Godot fixtures and NUnit's familiar test runner. | `--nunit` |
| [C# REPL](https://2dog.dev/hosts/repl) | Explore and change your running game from an interactive prompt. | `--repl` |

Use these flags with `dnx 2dog add`, or run it without flags to pick hosts interactively.
See the [showcase](demos/showcase/README.md) for the same Godot project running across desktop, web, UI and Android hosts.


## 🦴 Your game deserves ~~treats~~ tests, too

(ok, it also deserves treats!)

**Unit testing with xUnit and NUnit is a first-class feature of 2dog.** Run tests against a real Godot engine, load your scenes, exercise your game code, and check what happens as frames advance.

- **Headless tests for CI.** Both hosts run with `dotnet test`, without opening a game window or needing the Godot editor.
- **Godot-aware assertions.** Check signal counts and arguments, wait for conditions as frames advance, and verify that `QueueFree()` actually frees a node.
- **Async tests on Godot's thread.** Fixtures keep setup, tests, `await` continuations and teardown on the engine's owner thread.
- **Engine errors fail tests.** Godot warnings, errors and exceptions caught in C# callbacks fail tests by default. Assert expected reports explicitly.

Add either framework, or both, from your Godot project directory:

```bash
dnx 2dog add --xunit --nunit
dotnet test MyGame.xunit
dotnet test MyGame.nunit
```

Each test host comes with eight examples covering scenes, async work, signals, timers and deferred deletion.
Rendering fixtures are available too; see the [testing guide](https://2dog.dev/testing), [xUnit package](twodog.xunit/README.md) and [NUnit package](twodog.nunit/README.md) for examples and platform details.


## Getting started

Full documentation at **[2dog.dev](https://2dog.dev)**.


### Existing project (recommended)

2dog adds nested .NET hosts without moving your existing Godot project.

```bash
cd path/to/MyGame
dnx 2dog add                  # pick the hosts you want, then confirm
dotnet run --project MyGame.2dog
```

Run `dnx 2dog add` again to add another host.


### New project

```bash
dnx 2dog new MyGame
cd MyGame
dotnet run --project MyGame.2dog
```

The package also provides a `dotnet new` template:
`dotnet new install 2dog && dotnet new 2dog -n MyGame`.

In either case, the familiar Godot workflow still works:

```bash
godot-mono --editor . # or Godot_v4.7.2-stable_mono_win64.exe, etc.
```


## Exporting for the web

You can also publish the generated .NET app to `browser-wasm` for the browser.

When adding a Web host, 2dog offers to install missing `wasm-tools` on terminals.
If you skip the offer or run unattended, use
`dnx 2dog doctor --install-wasm-tools --yes` before publishing.

```bash
dotnet publish MyGame.web
dnx dotnet-serve -d MyGame.web/AppBundle
```

See the [Web host](https://2dog.dev/hosts/web) for building and deployment.

## Project structure

2dog adds subdirectories with "hosts" that run your Godot project. For example, a project with desktop, web, Avalonia and both test hosts looks like this:

```text
MyGame/                       Godot project and solution root
├── project.godot             Scenes, scripts, assets, project settings
├── MyGame.csproj             Godot C# game assembly
├── MyGame.2dog/              Desktop .NET host
├── MyGame.web/               Browser WebAssembly host
├── MyGame.avalonia/           Avalonia UI host
├── MyGame.xunit/             Headless xUnit host
└── MyGame.nunit/             Headless NUnit host
```

Each nested host carries a `.gdignore`, so Godot ignores it. Your game project stays as it was, and each host
has its own entry point and dependencies.


## Requirements and status

- .NET SDK 10.0 or later
- Web/WebXR/Blazor builds: `wasm-tools`; Android builds: `android`, Android SDK and JDK 17. Desktop and tests need neither workload, even when those optional hosts exist in the solution.
- Godot 4.7.x official .NET editor (only when you want to edit scenes visually)
- Supported build platforms: `win-x64`, `linux-x64`, and `osx-arm64`
- Supported RIDs for published builds: `win-x64`, `linux-x64`, `osx-arm64`, `browser-wasm`
- Packages available on [NuGet](https://www.nuget.org/packages/2dog) and [GitHub](https://github.com/outfox/2dog/releases)

Experimental Android arm64/x64 support: `dnx 2dog add --android` scaffolds a host
whose `dotnet publish` makes an APK.
See the [Android host](https://2dog.dev/hosts/android) and [2dog.android](platforms/twodog.android/README.md).
Device validation is still required before Android joins the supported RID list.


## Dogs and robots are nice

🦴♥️👾 **2dog** is proudly made by human maintainers and contributors. We permit extensive use of LLMs:
- Forked `libgodot` features/fixes consist of similar amounts of human-written and machine-written code
- Commits/PRs are reviewed & gated by both humans and machines (over 90%/90%) and signed by humans
- .NET Host applications are mostly human-written, while boilerplate assets (e.g. XAML) are mostly machine-written
- Configurations, tools, smoke tests, CSS, MSBuild XML, and CI workflows are overwhelmingly LLM-maintained
- Documentation aims to be human-authored, but new feature docs are generated and later gradually rewritten
- Releases on NuGet use Trusted Publishing and may only be invoked via direct human interaction


## Teach 2dog new tricks

Want to work on 2dog itself? Clone with submodules, then build the native and .NET packages:

```bash
git clone --recursive https://github.com/outfox/2dog
cd 2dog
uv run poe build-all
```

Run the showcase with `dotnet run --project demos/showcase/showcase.2dog` and the tests with
`dotnet test twodog.tests`.


## Join us at the dog park

We have a dedicated channel for 2dog. Come say hello!

[![Discord Invite](https://img.shields.io/badge/discord-_%E2%A4%9Coutfox%E2%A4%8F-blue?logo=discord&logoColor=f5f5f5)](https://discord.gg/GAXdbZCNGT)

---

### *No squirrels were harmed in the making of this README.*
