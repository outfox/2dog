# 2dog.android

Initial, experimental Android host support. Godot's Java Activity owns the surface,
render thread, input, pause/resume and shutdown; the .NET app registers its runtime
before launching it. Do not use the desktop `Engine.Start()` loop on Android.

Reference `2dog.engine`, this package, and the `2dog.android-arm64` and/or
`2dog.android-x64` meta packages. Choose Debug or Release with `TwoDogVariant` and
an explicit Android RID. Both native variants retain the JNI name
`libgodot_android.so`; exactly one belongs in each ABI directory of the APK.

Set `GodotProjectDir` to the Godot project, and `dotnet publish -r android-arm64`
produces a signed APK:

- the build imports the project and exports its pck with the project's `Android`
  export preset (`TwoDogAndroidExportPreset`), using the same capability as desktop
  publish: the packaged editor libgodot of the build machine, or `GodotEditor`.
  `TwoDogAndroidPack` supplies a pre-exported pck instead;
- every Android library and dependency that the project's `.gdextension` files list
  for the APK's ABIs and variant goes into `lib/<abi>/`, where the engine loads it
  (`TwoDogAndroidGdExtensions=false` opts out);
- the pck becomes the APK asset `game.pck`, which `TwoDogActivity` loads.

Mark the Android preset `runnable=false`: a runnable Android preset starts the
editor's ADB device poll during the headless export, which logs an `EditorSettings`
error when it exits. Release publishes produce an app bundle unless the host sets
`<AndroidPackageFormats>apk</AndroidPackageFormats>`.

Signing follows established conventions only:

- .NET Android's `AndroidKeyStore=true` with `AndroidSigningKeyStore`,
  `AndroidSigningKeyAlias`, `AndroidSigningStorePass` and `AndroidSigningKeyPass`
  always wins (`env:` and `file:` password prefixes keep secrets out of logs);
- otherwise Godot's export variables `GODOT_ANDROID_KEYSTORE_RELEASE_PATH`, `_USER`
  (the key alias) and `_PASSWORD` (`GODOT_ANDROID_KEYSTORE_DEBUG_*` for debug builds).
  2dog maps them onto the .NET properties and hands the password over as an `env:`
  reference, so it never reads the secret;
- otherwise .NET signs with its per-user debug key (`debug.keystore` in its
  `Xamarin/Mono for Android` settings directory), and release builds say so.

`TwoDogAndroidSigning=false` turns the variable mapping off.

`demos/showcase/showcase.android` is the complete example; `tests/android/host` is
the minimal one. Both include the Java
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
CI builds the Android natives (arm64/x64, debug/release) and the Java AARs into the
`godot-<hash>` natives release; its pack job packs the `2dog.android*` packages
from them. Neither build needs Mono glue. The pinned Godot submodule includes the
Android native-symbol resolver changes; rebuild the managed bindings from that
source before testing. To build everything locally:

```sh
uv run build-godot.py # desktop editor and managed bindings
uv run build-godot.py --platform android --arch arm64
uv run build-godot.py --platform android --arch x86_64
uv run scripts/build_android_java.py
uv run poe pack-android
uv run poe build # pack desktop dependencies and the managed engine with AndroidHost
```

Then publish a host and run it:

```sh
dotnet publish tests/android/host/android-smoke.csproj -c Debug -r android-x64 -o artifacts/android
uv run scripts/test_android_device.py artifacts/android/dev.twodog.smoke-Signed.apk --serial emulator-5554
```

`scripts/publish_android.py` wraps the same publish: `--format aab` builds an app
bundle, `--pack` supplies a pre-exported pck, and signing properties can be passed
using repeated `--property NAME=VALUE` arguments; use normal .NET Android keystore
configuration for distributable builds. No store upload or NuGet push is performed.

Run `uv run poe test-android-build` for Android build and package tests.
The APK smoke test validates C# callbacks, signals, node disposal and a rendered
pixel on an Android 36 x64 emulator. The showcase APK adds the main-loop hook, a
3D scene and a plain C GDExtension loaded from the APK's native library directory.
Input, pause/resume, restart, broader GDExtension coverage and physical-device
validation remain gates before production support.
The Android workload and emulator are not required for the build and package tests.
The separate `tests/android/android.slnx` keeps Android workload requirements out
of ordinary desktop solution builds. CI uploads the Android packages as their own
`android-packages` artifact, which deploy does not publish yet; `ForcePackAllPlatforms`
cannot pack empty Android packages.

## APK implementation milestones

1. Build both native variants for one ABI, build both Java AARs, pack a private
   local NuGet feed, and publish a signed smoke APK (the publish exports the C# scene).
   Inspect the final APK for the exact selected native libraries, one ABI, the
   PCK and the extracted game-assembly asset. The private feed also contains
   desktop dependency stubs and must never be published.
2. Run that APK on an x64 emulator. The scene verifies signals, native node
   lookup, deferred disposal, 32 C# frame callbacks and a green rendered pixel before
   reporting success. CI's Android Smoke job builds this APK and the showcase's
   against the packed packages and runs both; the showcase must report
   `2DOG_ANDROID_SHOWCASE_SMOKE_PASSED`. It uploads them as `android-apks`, with an
   arm64 showcase APK for devices.
3. Add Android engine tests for touch input, background/resume, Activity
   recreation, process death and repeated launches, with per-test results and
   crash diagnostics. Use Android-owned lifecycle fixtures and the existing
   `*Tests` naming convention.
4. Validate Debug/Release on physical arm64 hardware, AAB-derived APKs and a
   16 KB page-size environment before expanding the supported RID list.

With source-matched GodotSharp/editor release outputs staged, an installed
Android workload, and `ANDROID_HOME`/`JAVA_HOME` pointing to the SDK/JDK,
`build-android-apk` packs a private feed from the staged payloads, publishes a host
against it exactly like `dotnet publish` above, and inspects the APK:

```sh
uv run poe build-android-apk
```

The pck export uses the editor libgodot packed from `godot/bin`, or `--editor <godot
mono editor>`. Use `--rid android-arm64` or `--configuration Release` for the other
target. `--skip-native --skip-java` reuses already staged payloads. `--feed <dir>`
publishes against already packed packages instead of packing them (CI passes its
`nuget-packages` and `android-packages`, with the natives staged in
`godot/bin/android/` for the APK inspection). The resulting APK and inspection report
are written to `artifacts/android-apk/`. `--app showcase` publishes the showcase host
instead (into `artifacts/android-showcase-apk/`); its GDExtension needs an Android NDK
(`ANDROID_NDK_ROOT`, or one under the Android SDK).

References: [Godot Android library](https://docs.godotengine.org/en/4.7/tutorials/platform/android/android_library.html),
[.NET Android build items](https://learn.microsoft.com/en-us/dotnet/android/building-apps/build-items).
