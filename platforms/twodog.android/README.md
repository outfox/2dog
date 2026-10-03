# 2dog.android

Initial, experimental Android host support. Godot's Java Activity owns the surface,
render thread, input, pause/resume and shutdown; the .NET app registers its runtime
before launching it. Do not use the desktop `Engine.Start()` loop on Android.

Reference `2dog.engine`, this package, and the `2dog.android-arm64` and/or
`2dog.android-x64` meta packages. Choose Debug or Release with `TwoDogVariant` and
an explicit Android RID. Both native variants retain the JNI name
`libgodot_android.so`; exactly one belongs in each ABI directory of the APK.

Use `tests/android/host` as the initial host example. It includes the Java
dependencies Godot needs (AndroidX Fragment, DocumentFile, Kotlin standard library)
and extracts the game DLL into private app storage before `AndroidHost.Register`.
Registration happens in `Application.OnCreate`, including when Android recreates
the Godot Activity directly after process death.
GodotPlugins and the game must remain untrimmed; AOT is not enabled in this first pass.

Host code reaches the scene through `TwoDogActivity.addMainLoopStartedListener(Runnable)`:
Godot runs each listener on its render thread (the engine's main thread) once the main
loop has started and the main scene is in the tree. Register from `Application.OnCreate`
via JNI and catch every exception, because listeners run inside Godot's JNI call.
`demos/showcase/showcase.android` uses it to drive scene nodes once per frame
(`SceneTree.ProcessFrame`) and to run the showcase's API smoke.

Build prerequisites: .NET 10 with the `android` workload, JDK 17, Android SDK 36,
build tools 36.1.0, and NDK 29.0.14206865 (see the pinned Godot submodule config).
Android native and Java builds consume desktop-generated Mono glue.
The pinned Godot submodule includes the Android native-symbol resolver changes;
rebuild the managed bindings from that source before testing.

```sh
uv run build-godot.py # desktop editor, Mono glue and managed bindings
uv run build-godot.py --platform android --arch arm64
uv run build-godot.py --platform android --arch x86_64
uv run scripts/build_android_java.py
uv run poe pack-android
uv run poe build # pack desktop dependencies and the managed engine with AndroidHost
```

Export the game to a PCK using an Android export preset, then publish:

```sh
dotnet build tests/android/game/android-smoke-game.csproj -c Release
godot-mono --headless --path tests/android/game --export-pack Android /path/to/game.pck
uv run scripts/publish_android.py tests/android/host/android-smoke.csproj --pack /path/to/game.pck --rid android-x64
uv run scripts/test_android_device.py artifacts/android/dev.twodog.smoke-Signed.apk --serial emulator-5554
```

The publishing script produces an APK by default; `--format aab` builds an app
bundle. Signing properties can be passed using repeated `--property NAME=VALUE`
arguments; use normal .NET Android keystore configuration for distributable builds.
No store upload or NuGet push is performed.

Run `uv run poe test-android-build` for Android build and package tests.
The APK smoke test validates C# callbacks, signals, node disposal and a rendered
pixel on an Android 36 x64 emulator. The showcase APK adds the main-loop hook, a
3D scene and a plain C GDExtension loaded from the APK's native library directory.
Input, pause/resume, restart, broader GDExtension coverage and physical-device
validation remain gates before production support.
The Android workload and emulator are not required for the build and package tests.
The separate `tests/android/android.slnx` keeps Android workload requirements out
of ordinary desktop solution builds. The main release workflow skips these
packages until complete Android payloads are staged; `ForcePackAllPlatforms`
cannot publish empty Android packages.

## APK implementation milestones

1. Build both native variants for one ABI, build both Java AARs, pack a private
   local NuGet feed, build/export the C# scene, and publish a signed smoke APK.
   Inspect the final APK for the exact selected native libraries, one ABI, the
   PCK and the extracted game-assembly asset. The private feed also contains
   desktop dependency stubs and must never be published.
2. Run that APK on an x64 emulator. The scene verifies signals, native node
   lookup, deferred disposal, 32 C# frame callbacks and a green rendered pixel before
   reporting success. The `2dog Android APK` workflow builds and runs this path, then
   the showcase APK, which must report `2DOG_ANDROID_SHOWCASE_SMOKE_PASSED`.
3. Add Android engine tests for touch input, background/resume, Activity
   recreation, process death and repeated launches, with per-test results and
   crash diagnostics. Use Android-owned lifecycle fixtures and the existing
   `*Tests` naming convention.
4. Validate Debug/Release on physical arm64 hardware, AAB-derived APKs and a
   16 KB page-size environment before expanding the supported RID list.

With source-matched GodotSharp/editor release outputs staged, an installed
Android workload, and `ANDROID_HOME`/`JAVA_HOME` pointing to the SDK/JDK:

```sh
uv run poe build-android-apk --editor godot/bin/godot.linuxbsd.editor.x86_64.executable.mono
```

Use `--rid android-arm64` or `--configuration Release` for the other target.
`--skip-native --skip-java` reuses already staged payloads. The resulting APK
and inspection report are written to `artifacts/android-apk/`. `--app showcase`
builds the showcase host instead (into `artifacts/android-showcase-apk/`); it also
needs an Android NDK for the showcase's GDExtension (`ANDROID_NDK_ROOT`, or one under
`ANDROID_HOME/ndk`).

References: [Godot Android library](https://docs.godotengine.org/en/4.7/tutorials/platform/android/android_library.html),
[.NET Android build items](https://learn.microsoft.com/en-us/dotnet/android/building-apps/build-items).
