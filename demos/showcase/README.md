# Showcase

The repository's showcase: a Godot project that is also the solution root, with
the 2dog host projects nested inside it (each hidden from the Godot editor by
a `.gdignore`):

- `showcase.csproj` / `project.godot` - the Godot project (scenes, resources, C# scripts)
- `gdextension/` - a GDExtension in plain C (`TwoDogProbe`); the `GDExtensionProbe` label in `main.tscn` calls it
  on every host. Building the game project compiles it for the build machine (MSVC on Windows, `cc` elsewhere); the
  browser hosts compile a WebAssembly side module with the wasm-tools workload's emscripten and embed it in `godot.pck`;
  the Android host builds the arm64/x86_64 Android libraries with the NDK's clang, and 2dog.android packages them
- `signals/` - the signal table in the top-left corner: four sources tick once per second (a C# `[Signal]`, a GDScript
  `signal`, an engine `Timer` and the GDExtension's `TwoDogTicker`), and each row counts what a C# (`SignalCounter.cs`)
  and a GDScript (`signal_counter.gd`) listener received. Both columns should advance in lockstep on every host
- `showcase.2dog/` - desktop host: `dotnet run --project demos/showcase/showcase.2dog`
- `showcase.web/` - browser (wasm) host: `dotnet publish` from that folder (defaults to Release; needs the wasm-tools workload)
- `showcase.webxr/` - WebXR browser host (its page ships the WebXR Layers polyfill): same publish flow; VR needs a secure context (localhost or HTTPS)
- `showcase.blazor/` - Blazor Web App host (ASP.NET Core server + `Client/` WebAssembly page embedding the game via `2dog.blazor`): `dotnet run --project demos/showcase/showcase.blazor` (needs the wasm-tools workload)
- `showcase.winforms/` - Windows-only GUI embedding demo (`--wid`): `dotnet run --project demos/showcase/showcase.winforms`
- `showcase.winui/` - Windows-only WinUI 3 embedding demo (`--wid`; builds only on Windows): `dotnet run --project demos/showcase/showcase.winui`
- `showcase.avalonia/` - cross-platform Avalonia embedding demo (controls composite over the game): `dotnet run --project demos/showcase/showcase.avalonia`
- `showcase.android/` - experimental Android host: `dotnet publish demos/showcase/showcase.android` (arm64; add
  `-r android-x64` for the emulator) builds the game, exports its pck and packages a signed APK with the GDExtension
  probe (built with the Android NDK). Godot's Activity owns the loop, so the host drives the white cubes and runs the
  API smoke from `TwoDogActivity.addMainLoopStartedListener`. Needs the android workload and, in `packages/`, the
  `2dog.android*` packages (`uv run poe build-android`) next to the rest of 2dog's packages (`uv run poe build`). Those
  include the build machine's editor libgodot (`2dog.<rid>.editor`) and `2dog.tools`, which export the pck; pass
  `-p:GodotEditor=<godot mono editor>` to export with an editor binary instead. CI's `nuget-packages` artifact is
  the same feed (see [2dog.android](../../platforms/twodog.android/README.md)).
  It is in no solution because restore needs the android workload. CI's Android Smoke job publishes it the same way
  and uploads the APKs as `android-apks`. To run an APK:

      uv run scripts/test_android_device.py <dir>/dev.twodog.showcase-Signed.apk --serial emulator-5554 \
          --package dev.twodog.showcase --marker 2DOG_ANDROID_SHOWCASE_SMOKE_PASSED

  Start emulators with `-gpu swangle` (or `host`): the legacy SwiftShader GLES translator cannot link Godot's GLES3
  shaders, so the screen stays gray even though the smoke passes

The test suite (`twodog.tests/`, at the repository root) runs against this
project. Assets are imported automatically during build.

The web hosts reference the engine from the source checkout (ProjectReference
plus in-repo targets imports). Pass `-p:Official=true` to build against the
published NuGet packages instead (`2dog.engine` + `2dog.browser-wasm`, exactly
like a project scaffolded from the 2dog template), e.g.:

    dotnet publish demos/showcase/showcase.web -p:Official=true
