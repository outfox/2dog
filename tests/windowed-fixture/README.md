Starts the stock windowed `Fixture` through `RenderingCollection`, as consumers' rendering suites do, and renders a
few frames. It guards the macOS case where AppKit aborts the test process (exit code 134) unless the engine and its
window start on the process main thread, which 2dog.xunit's entry point gives to the 2dog test thread.

It needs a display, so it is not part of `2dog.tests.slnf`. CI runs it on macOS against the packed packages:

```bash
dotnet test tests/windowed-fixture -c Release
```

On macOS the fixture uses the Mobile renderer with Metal. Native OpenGL is unavailable on the CI virtual machine;
the test still creates a real window and renders frames through the stock fixture.
