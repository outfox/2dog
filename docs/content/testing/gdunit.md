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
commands from the project root, where the setup's `global.json` selects MTP:

```bash
dotnet test --project MyGame.gdunit
dotnet test --project MyGame.gdunit --list-tests
dotnet test --project MyGame.gdunit --gdunit-filter "*test_answer"
```

Plain `dotnet test` runs the GdUnit host together with the
[xUnit](/hosts/xunit) and [NUnit](/hosts/nunit) hosts. If the root
`global.json` does not select MTP, run the commands from `MyGame.gdunit/`. That
folder has its own `global.json`; see
[Select the runner](/configuration/gdunit#select-the-runner).

Use the host's MTP options for these commands. Do not use GdUnit's
[standalone command-line options](https://godot-gdunit-labs.github.io/gdUnit4/latest/advanced_testing/cmd/);
only GdUnit's own runner accepts them.

`--gdunit-filter` matches the fully qualified test name with case-sensitive
`*` and `?` wildcards. Each parameter row has its own result and a stable ID.
The host derives the ID from the resource path, the method and the row index.
To select a single row, use MTP's
[`--filter-uid`](https://learn.microsoft.com/dotnet/core/testing/microsoft-testing-platform-cli-options).

## Test a private project copy

Each discovery or execution request imports a fresh, writable copy of the
game. The copy keeps the original `res://` paths, game resource UIDs and
autoload settings. You can put test scenes and assets in the host folder, even
though its `.gdignore` hides them from the game. Only the configured test roots
become visible in the private copy. Folders under a nested `.gdignore` stay
hidden.

The source game, settings, sidecars and import cache stay unchanged. Changes
to tests or assets appear in the next request. Tests do not write
files to the source game. If a tool inspects files that the tests create,
configure the tool to read the
[artifact directory](/configuration/gdunit#artifacts).

The host's bridge replaces the game's main scene. Load game scenes explicitly in
tests. Discovery starts the engine and game autoloads, but runs no test bodies
or suite hooks. Execution uses that same engine session and runs only selected
cases.

The package supplies an unmodified, pinned official GdUnit release, with its
license and release provenance. You do not need to download the addon
separately. If the source project has its own `addons/gdUnit4` copy, the host
rejects it, so that two framework versions do not mix.

## Results and lifecycle

Assertions, skips and test errors become MTP outcomes. Suite lifecycle failures
also fail the run, even when every test body passes. Unexpected native Godot
warnings and errors fail the host. In the private project, Godot tracks the
GDScript call stack, so Release builds also report the source line of each
assertion.

The host disables GdUnit's flaky-test retries and fail-fast execution. It
preserves GdUnit session hooks; reports they generate remain in the private
project.

When MTP cancels a run, the host stops an active import process, or it asks
GdUnit to stop execution and finish teardown. The embedded session then gets
five seconds to clean up.
[Host timeouts](/configuration/gdunit#execution-options) limit the import
process and the embedded session. Neither timeout can interrupt a blocked
native call.

## Current host scope

The host executes GDScript suites headlessly and serializes requests within
its load context. Godot starts, runs and stops on its owner thread.
Scenes can load ordinary C# game components, but the host does not load
GdUnit4Net's C# test adapter. For C# test suites, use the [xUnit](/hosts/xunit)
or [NUnit](/hosts/nunit) hosts.

