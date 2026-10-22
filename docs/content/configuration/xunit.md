---
title: xUnit Configuration
description: "MSBuild configuration for xUnit hosts: engine variants, resource import, and Editor API references."
tagline: "Learn how to configure xUnit hosts, resource import and Editor APIs."
---

# xUnit Configuration

Set properties in `MyGame.xunit/MyGame.xunit.csproj`. Reference `2dog.xunit`
and explicitly select `2dog.engine` or `2dog.engine.double`. `2dog.xunit`
compiles its collection definitions into your test project.
The [global settings](/configuration#properties) control the game directory,
resource import, analyzers, and native variant.

Debug, Release, and Editor select the matching [native variant](/build-configurations).
For tests that use Editor APIs:

```bash
dotnet test MyGame.xunit -c Editor
```

The generated Editor configuration also defines `EDITOR` and references
`2dog.godotsharp.editor`. A test project that you create manually needs
this managed reference to compile Editor API calls. The editor native variant
alone does not add it.

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

Coverage reports go to `TestResults/`. To move an older VSTest host to MTP, add the
properties above and replace `xunit.v3` with `xunit.v3.mtp-v2`. For the other
steps, see Microsoft's
[migration guide](https://learn.microsoft.com/dotnet/core/testing/migrating-vstest-microsoft-testing-platform).

2dog delegates discovery, filters, result reporting and cancellation to
xUnit. The 2dog entry point keeps Godot on its owner thread. On macOS, that thread
is the process main thread. See
[xUnit's official MTP documentation](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform)
for runner options and IDE support.
