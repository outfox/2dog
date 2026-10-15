# MAUI validation

Pack the normal managed/native packages, then `uv run poe build-maui` on
Windows with `maui-windows` and `maui-android` installed.

```text
uv run python tests/maui/test_windows.py --require-gpu
```

The Windows tests scaffold outside the repository with a private NuGet cache
and public SDK source. They create the view from XAML and exercise CPU presentation, automatic fallback for
the Compatibility renderer, window resize, owner-thread commands, and clean
shutdown. `--require-gpu` additionally requires working Vulkan/D3D11 sharing.

Build and test the Android showcase with the normal Android device runner:

```text
dotnet build demos/showcase/showcase.maui -f net10.0-android -r android-x64
uv run scripts/test_android_device.py demos/showcase/showcase.maui/bin/Debug/net10.0-android/android-x64/com.companyname.showcase.maui-Signed.apk --serial emulator-5554 --package com.companyname.showcase.maui --marker 2DOG_MAUI_SHOWCASE_SMOKE_PASSED
```

The marker is emitted only after the shared Godot API smoke runs through
`GodotView.Dispatch` on the render thread. Check pause/resume, rotation,
background/foreground, and the MAUI overlay on a device or emulator.

Local Windows validation observed one native DirectInput startup failure
(`0xc06d007f`). The crash dump identifies `dinput8.dll` failing to resolve
`CreateInputHostForProcess` through `ext-ms-win-mininput-inputhost-l1-1-1.dll`,
which resolved to the system `InputHost.dll` (version `10.0.26100.9278`). The
isolated CPU check passed afterward. Controller support remains enabled.
The dump and diagnosis are retained locally in
`artifacts/maui/windows-inputhost.dmp` and
`artifacts/maui/windows-inputhost-diagnosis.txt`.
