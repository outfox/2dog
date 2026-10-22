---
title: NUnit Configuration
description: "Configure NUnit hosts, engine variants, Editor bindings and headless fixtures."
tagline: "Learn how to configure NUnit hosts, engine variants and headless fixtures."
---

# NUnit Configuration

Set properties in `MyGame.nunit/MyGame.nunit.csproj`. Reference `2dog.nunit`
and explicitly select `2dog.engine` or `2dog.engine.double`.
The setup also references `NUnit`, `NUnit3TestAdapter` and
`Microsoft.Testing.Extensions.CodeCoverage` for `dotnet test` and coverage
collection.

The [global settings](/configuration#properties) control the game directory,
resource import, analyzers and native variant.

Debug, Release, and Editor select the matching [native variant](/build-configurations).
For tests that use Editor APIs:

```bash
dotnet test MyGame.nunit -c Editor
```

The generated Editor configuration defines `EDITOR` and references
`2dog.godotsharp.editor`. A test project that you create manually needs
this managed reference to compile Editor API calls. The editor native variant
alone does not add it.

See [Testing with NUnit](/testing/nunit) for fixture setup and runner rules,
and [Writing engine tests](/testing/writing-tests) for waits and assertions.

## Microsoft.Testing.Platform

The host runs on the official NUnit adapter's MTP bridge. Projects that 2dog
sets up reference `NUnit3TestAdapter` 6.x for MTP v2 and set these properties:

```xml
<PropertyGroup>
    <OutputType>Exe</OutputType>
    <EnableNUnitRunner>true</EnableNUnitRunner>
</PropertyGroup>
```

Run it from the project root; see [Test runners](/testing/#test-runners) for
the `global.json` that selects MTP.

```bash
dotnet test MyGame.nunit
dotnet test --project MyGame.nunit --coverage
```

Coverage reports go to `TestResults/`. To move an older VSTest host to MTP, add the
properties above and update `NUnit3TestAdapter` to 6.x. For the other steps,
see Microsoft's
[migration guide](https://learn.microsoft.com/dotnet/core/testing/migrating-vstest-microsoft-testing-platform).

2dog leaves discovery, filters and results to NUnit's bridge and keeps
Godot setup, tests and teardown on NUnit's fixture thread. See
[NUnit's official MTP documentation](https://docs.nunit.org/articles/vs-test-adapter/NUnit-And-Microsoft-Test-Platform.html)
for runner options.

### Cancellation

NUnit's adapter does not signal the test's cancellation token when the
session is cancelled. Its forced stop also skips fixture teardown. To cancel
tests reliably:

- Use NUnit's
  [`[CancelAfter]`](https://docs.nunit.org/articles/nunit/writing-tests/attributes/cancelafter.html).
- Observe its token in async work and frame waits.
- Keep that timeout below any outer CI timeout.
