# NUnit rendering smoke test

Run with a display on Windows or Linux after packing `2dog.engine` and `2dog.nunit`:

```text
dotnet test tests/nunit-rendering -c Release
```

This uses the stock NUnit adapter and a library test project, without an executable
manifest. It verifies STA on Windows, actual rendered frames, and async owner-thread
continuations. Native startup warnings fail the test through `GodotTestFixture`.
Two fixture classes also check sequential rendering-engine restart.

Windows uses the Mobile renderer with Vulkan. CI provisions a checksum-verified Mesa
lavapipe CPU driver and portable Vulkan loader through
`.github/scripts/setup-windows-software-rendering.ps1 -RegisterDriver`. Elevated Windows
runners ignore Vulkan's driver-path environment overrides, so the script registers the
verified driver on the disposable CI VM. The loader is staged beside `testhost.exe`;
neither DLL is shipped in 2dog packages. This still exercises real windows, rendering, STA, and
engine restart. Without a graphics driver, Godot can block in an invisible error dialog;
the Windows job captures native logs and a full hang dump after 90 seconds.

Linux needs a display (or Xvfb) and a compatible renderer. macOS rendering is outside
the standard NUnit runner's scope; the headless NUnit suite runs there instead.

Kept out of the headless solution filter, like `tests/windowed-fixture`.
