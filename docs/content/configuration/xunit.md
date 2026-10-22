---
title: xUnit Configuration
description: "MSBuild configuration for xUnit hosts: engine variants, resource import, and Editor API references."
tagline: "Learn how to configure xUnit hosts, resource import and Editor APIs."
---

# xUnit Configuration

Set properties in `MyGame.xunit/MyGame.xunit.csproj`. Reference `2dog.xunit`
for the compile-in collection definitions and explicitly select
`2dog.engine` or `2dog.engine.double`.
The [global settings](/configuration#properties) control the game directory,
resource import, analyzers, and native variant.

Debug, Release, and Editor select the matching [engine variant](/build-configurations).
For tests that use Editor APIs:

```bash
dotnet test MyGame.xunit -c Editor
```

The generated Editor configuration also defines `EDITOR` and references
`2dog.godotsharp.editor`. A manually created test project needs that managed
reference to compile Editor API calls; selecting the editor native alone
does not add it.

Tests use the game directory and its imported resource cache. See
[Resource Import](/import-tool) to require or force an import, and
[Testing with xUnit](/testing/xunit) for fixture and runner settings.

## Microsoft.Testing.Platform

The host uses xUnit's own runner on MTP v2. Projects that 2dog sets up reference
`xunit.v3.mtp-v2` and `Microsoft.Testing.Extensions.CodeCoverage`, and set
these properties:

```xml
<PropertyGroup>
    <OutputType>Exe</OutputType>
    <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
</PropertyGroup>
```

Run it from the project root; see [Test runners](/testing/#test-runners) for
the `global.json` that selects MTP.

```bash
dotnet test MyGame.xunit
dotnet test --project MyGame.xunit --coverage --coverage-output-format cobertura
```

Coverage reports go to `TestResults/`. To move an older VSTest host, add the
properties above and replace `xunit.v3` with `xunit.v3.mtp-v2`; Microsoft's
[migration guide](https://learn.microsoft.com/dotnet/core/testing/migrating-vstest-microsoft-testing-platform) covers the rest.

2dog delegates discovery, filters, result reporting and cancellation to
xUnit. Its entry point preserves the Godot owner thread, including the
process main thread on macOS. See
[xUnit's official MTP documentation](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform)
for runner options and IDE support.
