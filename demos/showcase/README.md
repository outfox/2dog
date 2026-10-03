# Showcase

The repository's showcase: a Godot project that is also the solution root, with
the 2dog host projects nested inside it (each hidden from the Godot editor by
a `.gdignore`):

- `showcase.csproj` / `project.godot` - the Godot project (scenes, resources, C# scripts)
- `gdextension/` - a GDExtension in plain C (`TwoDogProbe`); the `GDExtensionProbe` label in `main.tscn` calls it
  on every host. Building the game project compiles it for the build machine (MSVC on Windows, `cc` elsewhere); the
  browser hosts compile a WebAssembly side module with the wasm-tools workload's emscripten and embed it in `godot.pck`;
  `-p:TwoDogProbeAndroid=true` adds the arm64/x86_64 Android libraries (NDK clang), which the Android host packages
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
- `showcase.android/` - experimental Android host (APK): Godot's Activity owns the loop, so the host drives the white
  cubes and runs the API smoke from `TwoDogActivity.addMainLoopStartedListener`. Needs the Android natives, Java
  payloads and NDK (see [2dog.android](../../platforms/twodog.android/README.md)); it is in no solution because restore
  needs the android workload. Build and inspect the APK, then run it on a device or emulator:

      uv run poe build-android-apk --app showcase --editor <godot mono editor> [--rid android-arm64]
      uv run scripts/test_android_device.py artifacts/android-showcase-apk/apk/dev.twodog.showcase-Signed.apk \
          --serial emulator-5554 --package dev.twodog.showcase --marker 2DOG_ANDROID_SHOWCASE_SMOKE_PASSED

  Start emulators with `-gpu swangle` (or `host`): the legacy SwiftShader GLES translator cannot link Godot's GLES3
  shaders, so the screen stays gray even though the smoke passes

The test suite (`twodog.tests/`, at the repository root) runs against this
project. Assets are imported automatically during build.

The web hosts reference the engine from the source checkout (ProjectReference
plus in-repo targets imports). Pass `-p:Official=true` to build against the
published NuGet packages instead (`2dog.engine` + `2dog.browser-wasm`, exactly
like a project scaffolded from the 2dog template), e.g.:

    dotnet publish demos/showcase/showcase.web -p:Official=true
