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
