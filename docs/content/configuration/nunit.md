---
title: NUnit Configuration
description: "Configure NUnit hosts, engine variants, Editor bindings and headless fixtures."
---

# NUnit Configuration

Set properties in `MyGame.nunit/MyGame.nunit.csproj`. Reference `2dog.nunit`,
which brings in `2dog.engine` and NUnit. The scaffold also references
`NUnit3TestAdapter`, `Microsoft.NET.Test.Sdk` and `coverlet.collector` for
`dotnet test` and coverage collection.

The [global settings](/configuration#properties) control the game directory,
resource import, analyzers and native variant.

| Configuration | Default native | Use |
| --- | --- | --- |
| Debug | `debug` | Development tests with assertions |
| Release | `release` | Tests against the production engine |
| Editor | `editor` | Tests using Editor APIs and `[Tool]` scripts |

```bash
dotnet test MyGame.nunit -c Editor
```

The generated Editor configuration defines `EDITOR` and references
`2dog.godotsharp.editor`. Selecting the editor native alone does not add
the managed reference needed to compile Editor API calls.

`GodotTestFixture` uses one headless engine per NUnit fixture with inherited
`SingleThreaded`, `NonParallelizable` and
`FixtureLifeCycle(LifeCycle.SingleInstance)` attributes.
Keep these settings so engine startup, tests and disposal share one thread.
Use derived `[OneTimeSetUp]`, `[SetUp]`, `[TearDown]` and `[OneTimeTearDown]`
methods for your own lifecycle work; they may return `Task`.

Override `CreateFixture()` to configure a custom engine fixture and
`FailOnGodotErrors` to change native error checking for a fixture.
See [NUnit](/hosts/nunit) for signal expectations, bounded waits and cleanup.
