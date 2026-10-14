---
title: xUnit Configuration
description: "MSBuild configuration for xUnit hosts: engine variants, resource import, and Editor API references."
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

The host uses xUnit's own runner. Scaffolded projects use VSTest by default;
their `xunit.v3` package also supports MTP v1. For MTP v2, replace that
package with `xunit.v3.mtp-v2` and add these properties:

```xml
<PropertyGroup>
    <OutputType>Exe</OutputType>
    <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
</PropertyGroup>
```

To select native MTP integration for `dotnet test` on .NET 10, add this
alongside your SDK settings in `global.json`:

```json
"test": {
  "runner": "Microsoft.Testing.Platform"
}
```

Then run `dotnet test --project MyGame.xunit/MyGame.xunit.csproj`.
The generated project retains the VSTest adapter for existing test tooling.
VSTest coverage collector options apply to VSTest; configure MTP coverage
through its extensions.

2dog delegates discovery, filters, result reporting and cancellation to
xUnit. Its entry point preserves the Godot owner thread, including the
process main thread on macOS. See
[xUnit's official MTP documentation](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform)
for runner options and IDE support.
