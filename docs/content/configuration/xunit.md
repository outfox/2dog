---
title: xUnit Configuration
description: "MSBuild configuration for xUnit hosts: engine variants, resource import, and Editor API references."
---

# xUnit Configuration

Set properties in `MyGame.tests.csproj`. Reference `2dog.xunit`, which brings
in `2dog.engine` and the compile-in collection definitions.
The [global settings](/configuration#properties) control the game directory,
resource import, analyzers, and native variant.

| Configuration | Default native | Use |
| --- | --- | --- |
| Debug | `debug` | Development tests with assertions |
| Release | `release` | Tests against the production engine |
| Editor | `editor` | Tests using Editor APIs and `[Tool]` scripts |

```bash
dotnet test MyGame.tests -c Editor
```

The generated Editor configuration also defines `EDITOR` and references
`2dog.godotsharp.editor`. A manually created test project needs that managed
reference to compile Editor API calls; selecting the editor native alone
does not add it.

Tests use the game directory and its imported resource cache. See
[Resource Import](/import-tool) to require or force an import, and
[Testing with xUnit](/testing) for fixture and runner settings.
