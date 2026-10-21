---
title: Testing GDScript with GdUnit
description: "Understand how the 2dog host changes GdUnit execution, resource isolation and test reporting."
tagline: "Learn how 2dog runs your GdUnit tests and keeps their resources apart."
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
commands from the project root, whose scaffolded `global.json` selects MTP:

```bash
dotnet test --project MyGame.gdunit
dotnet test --project MyGame.gdunit --list-tests
dotnet test --project MyGame.gdunit --gdunit-filter "*test_answer"
```

Plain `dotnet test` runs the GdUnit host together with the
[xUnit](/hosts/xunit) and [NUnit](/hosts/nunit) hosts. If the root
`global.json` does not select MTP, run from `MyGame.gdunit/`, which carries
its own; see [Select the runner](/configuration/gdunit#select-the-runner).

Use the host's MTP options for these commands. GdUnit's
[standalone command-line options](https://godot-gdunit-labs.github.io/gdUnit4/latest/advanced_testing/cmd/)
belong to its own runner.

`--gdunit-filter` matches the fully qualified test name with case-sensitive
`*` and `?` wildcards. Each parameter row has its own result and stable ID,
derived from the resource path, method and row index, so MTP's
[`--filter-uid`](https://learn.microsoft.com/dotnet/core/testing/microsoft-testing-platform-cli-options)
can select a single row.

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

The package supplies an unmodified, pinned official GdUnit release, with its
license and release provenance. No separate addon download is needed. A source
project containing its own `addons/gdUnit4` copy is rejected to avoid combining
framework versions.

## Results and lifecycle

Assertions, skips and test errors become MTP outcomes. Suite lifecycle failures
also fail the run, even when every test body passes. Unexpected native Godot
warnings and errors fail the host. The private project enables GDScript call
stack tracking so Release builds retain assertion source lines.

The host disables GdUnit's flaky-test retries and fail-fast execution. It
preserves GdUnit session hooks; reports they generate remain in the private
project.

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

