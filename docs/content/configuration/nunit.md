---
title: NUnit Configuration
description: "Configure NUnit hosts, engine variants, Editor bindings and headless fixtures."
tagline: "Learn how to configure NUnit hosts, engine variants and headless fixtures."
---

# NUnit Configuration

Set properties in `MyGame.nunit/MyGame.nunit.csproj`. Reference `2dog.nunit`
and explicitly select `2dog.engine` or `2dog.engine.double`.
The scaffold also references `NUnit`, `NUnit3TestAdapter` and
`Microsoft.Testing.Extensions.CodeCoverage` for `dotnet test` and coverage
collection.

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

The host runs on the official NUnit adapter's MTP bridge. Scaffolded projects
reference `NUnit3TestAdapter` 6.x for MTP v2 and set these properties:

```xml
<PropertyGroup>
    <OutputType>Exe</OutputType>
    <EnableNUnitRunner>true</EnableNUnitRunner>
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
dotnet test MyGame.nunit
dotnet test --project MyGame.nunit --coverage
```

Coverage uses MTP's `--coverage` option and writes a `.coverage` file under
`TestResults/`; VSTest's `--collect "XPlat Code Coverage"` does not apply.

Hosts scaffolded before 2dog used MTP run on VSTest, and
[`2dog doctor`](/cli/doctor) flags them with `layout.test-runner`. To migrate
one, add the properties above, update `NUnit3TestAdapter` to 6.x, replace
`coverlet.collector` with `Microsoft.Testing.Extensions.CodeCoverage`, and
remove `Microsoft.NET.Test.Sdk`.

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
