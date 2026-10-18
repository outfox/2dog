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

The host uses xUnit's own runner on MTP v2. Scaffolded projects reference
`xunit.v3.mtp-v2` and `Microsoft.Testing.Extensions.CodeCoverage`, and set
these properties:

```xml
<PropertyGroup>
    <OutputType>Exe</OutputType>
    <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
</PropertyGroup>
```

On .NET 10, `dotnet test` runs every project of a run on the runner that
`global.json` selects. The root `global.json` that 2dog creates with a test
host selects MTP, so `dotnet test` from the project root runs the xUnit,
NUnit and GdUnit hosts together. 2dog never edits an existing `global.json`;
add this alongside your SDK settings:

```json
"test": {
  "runner": "Microsoft.Testing.Platform"
}
```

```bash
dotnet test MyGame.xunit
dotnet test --project MyGame.xunit --coverage --coverage-output-format cobertura
```

Coverage uses MTP's `--coverage` option from
`Microsoft.Testing.Extensions.CodeCoverage`; VSTest's
`--collect "XPlat Code Coverage"` does not apply. Reports go to `TestResults/`.

Hosts scaffolded before 2dog used MTP run on VSTest, and
[`2dog doctor`](/cli/doctor) flags them with `layout.test-runner`. To migrate
one, add the properties above, replace `xunit.v3` with `xunit.v3.mtp-v2` and
`coverlet.collector` with `Microsoft.Testing.Extensions.CodeCoverage`, and
remove `xunit.runner.visualstudio` and `Microsoft.NET.Test.Sdk`.

2dog delegates discovery, filters, result reporting and cancellation to
xUnit. Its entry point preserves the Godot owner thread, including the
process main thread on macOS. See
[xUnit's official MTP documentation](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform)
for runner options and IDE support.
