# NUnit rendering smoke test

Run with a display on Windows or Linux after packing `2dog.engine` and `2dog.nunit`:

```text
dotnet test tests/nunit-rendering -c Release
```

This uses the stock NUnit adapter and a library test project, without an executable
manifest. It verifies STA on Windows, actual rendered frames, and async owner-thread
continuations. Native startup warnings fail the test through `GodotTestFixture`.
Two fixture classes also check sequential rendering-engine restart.
Linux needs a display (or Xvfb) and a compatible renderer. macOS rendering is outside
the standard NUnit runner's scope; the headless NUnit suite runs there instead.

Kept out of the headless solution filter, like `tests/windowed-fixture`.
