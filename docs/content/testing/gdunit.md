---
title: Testing GDScript with GdUnit
description: "Understand how the 2dog host changes GdUnit execution, resource isolation and test reporting."
---

# Testing GDScript with GdUnit

Use [GdUnit's official documentation](https://godot-gdunit-labs.github.io/gdUnit4/latest/)
for writing suites, assertions, hooks, mocks and scene tests. Its
[test suite guide](https://godot-gdunit-labs.github.io/gdUnit4/latest/testing/test-suite/)
and [parameterized test guide](https://godot-gdunit-labs.github.io/gdUnit4/latest/advanced_testing/paramerized_tests/)
describe the framework APIs. This page covers only the differences introduced
by 2dog. To create the .NET project, follow the [GdUnit host guide](/hosts/gdunit).

## Run through .NET

The 2dog host uses Microsoft.Testing.Platform (MTP) and `dotnet test`. Run
commands inside the host directory containing its MTP `global.json`:

```bash
dotnet test --project MyGame.gdunit.csproj
dotnet test --project MyGame.gdunit.csproj --list-tests
dotnet test --project MyGame.gdunit.csproj --gdunit-filter "*test_answer"
```

Use the host's MTP options for these commands. GdUnit's
[standalone command-line options](https://godot-gdunit-labs.github.io/gdUnit4/latest/advanced_testing/cmd/)
belong to its own runner.

`--gdunit-filter` matches the fully qualified test name with case-sensitive
`*` and `?` wildcards. Each parameter row has its own MTP result and stable ID,
derived from the resource path, method and row index. MTP's `--filter-uid`
selects an exact ID, including a single parameter row. An empty selection
follows MTP's no-test policy.

`--list-tests json` includes IDs and original source locations. When running
the executable directly, native Godot output can appear before or after the
JSON document as its stdout buffers flush. Read the JSON document separately
from those native messages.

## Test a private project copy

Each discovery or execution request imports a fresh, writable copy of the
game. Original `res://` paths, game resource UIDs and autoload settings are
preserved. Test scenes and assets can live beneath the host's `.gdignore`;
only configured test roots become visible in the private copy. Nested ignored
trees remain excluded.

The source game, settings, sidecars and import cache stay unchanged. Changes
to tests or assets appear in the next request. Point tools that inspect runtime
files at the [artifact directory](/configuration/gdunit#artifacts), rather
than expecting test-generated files in the source game.

The bridge replaces the game's main scene. Load game scenes explicitly in
tests. Discovery starts the engine and game autoloads, but runs no test bodies
or suite hooks. Execution uses that same engine session and runs only selected
cases.

The package supplies a pinned GdUnit runtime. A source project containing its
own `addons/gdUnit4` copy is rejected to avoid combining framework versions.

## Results and lifecycle

Assertions, skips and test errors become MTP outcomes. Suite lifecycle failures
also fail the run, even when every test body passes. Unexpected native Godot
warnings and errors fail the host. The private project enables GDScript call
stack tracking so Release builds retain assertion source lines.

The host disables GdUnit's flaky-test retries and fail-fast execution. It
preserves GdUnit session hooks; reports they generate remain in the private
project. Add MTP reporting extensions to the .NET host when you need their
formats or options.

MTP cancellation terminates an active import process or asks GdUnit to stop
execution and finish teardown. The embedded session gets a five-second cleanup
grace period. [Host timeouts](/configuration/gdunit#execution-options) bound
importing and engine pumping; neither can interrupt a blocked native call.

## Current host scope

The host executes GDScript suites headlessly and serializes requests within
its load context. Godot starts, pumps and shuts down on its owner thread.
Ordinary C# game components can be loaded in scenes; GdUnit4Net's C# test
adapter is not loaded. Use the [xUnit](/hosts/xunit) or [NUnit](/hosts/nunit)
hosts for C# test suites.

Rendering, verified IDE integration, snapshot caching and relocatable
published hosts remain follow-up work. CI exercises headless execution on
Windows, Linux and macOS in both precisions and all three build configurations.
