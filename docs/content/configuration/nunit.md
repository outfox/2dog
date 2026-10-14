---
title: NUnit Configuration
description: "Configure NUnit hosts, engine variants, Editor bindings and headless fixtures."
---

# NUnit Configuration

Set properties in `MyGame.nunit/MyGame.nunit.csproj`. Reference `2dog.nunit`
and explicitly select `2dog.engine` or `2dog.engine.double`.
The scaffold also references
`NUnit3TestAdapter`, `Microsoft.NET.Test.Sdk` and `coverlet.collector` for
`dotnet test` and coverage collection.

The [global settings](/configuration#properties) control the game directory,
resource import, analyzers and native variant.

Debug, Release, and Editor select the matching [engine variant](/build-configurations).
For tests that use Editor APIs:

```bash
dotnet test MyGame.nunit -c Editor
```

The generated Editor configuration defines `EDITOR` and references
`2dog.godotsharp.editor`. Selecting the editor native alone does not add
the managed reference needed to compile Editor API calls.

See [Testing with NUnit](/testing/nunit) for fixture setup and runner rules,
and [Writing engine tests](/testing/writing-tests) for waits and assertions.

## Microsoft.Testing.Platform

The host uses the official NUnit adapter's MTP bridge. New projects enable
it with these properties; add them to an existing project to opt in:

```xml
<PropertyGroup>
    <OutputType>Exe</OutputType>
    <EnableNUnitRunner>true</EnableNUnitRunner>
</PropertyGroup>
```

Use `NUnit3TestAdapter` 6.x for MTP v2. The same project retains VSTest
support. To use native MTP integration with `dotnet test` on .NET 10, add
this alongside your SDK settings in `global.json`:

```json
"test": {
  "runner": "Microsoft.Testing.Platform"
}
```

Then run `dotnet test --project MyGame.nunit/MyGame.nunit.csproj`.
2dog leaves discovery, filters and results to NUnit's bridge and keeps
Godot setup, tests and teardown on NUnit's fixture thread. See
[NUnit's official MTP documentation](https://docs.nunit.org/articles/vs-test-adapter/NUnit-And-Microsoft-Test-Platform.html)
for runner options.

### Cancellation

The verified NUnit adapter 6.3.1 does not signal
`TestContext.CurrentContext.CancellationToken` when MTP cancels the
session. Its forced stop skips fixture teardown, even if the test later
observes its own cancellation token. This can leave Godot active during
process exit.

Use NUnit's
[cooperative cancellation](https://docs.nunit.org/articles/nunit/writing-tests/attributes/cancelafter.html)
with `[CancelAfter(milliseconds)]`, and observe the NUnit token in async
work and frame waits. Set that test timeout below any outer MTP or CI
timeout. A forced process exit cannot guarantee engine cleanup.
