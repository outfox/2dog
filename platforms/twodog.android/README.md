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

Run `uv run python -m unittest discover -s tests/android -p 'test_*.py'` for portable
build/packaging tests. APK compilation, C# callbacks, input, pause/resume, restart,
GDExtensions, and device rendering remain validation gates before production support.
The Android workload and emulator are not required for the portable tests.
The separate `tests/android/android.slnx` keeps Android workload requirements out
of ordinary desktop solution builds. The main release workflow skips these
packages until complete Android payloads are staged; `ForcePackAllPlatforms`
cannot publish empty Android packages.

References: [Godot Android library](https://docs.godotengine.org/en/4.7/tutorials/platform/android/android_library.html),
[.NET Android build items](https://learn.microsoft.com/en-us/dotnet/android/building-apps/build-items).
