# GDScript host integration proof

This executable implements the first two GDScript test host milestones: isolated
resource importing and GdUnit execution through embedded libgodot. The shared
implementation now ships in [`2dog.gdunit`](../../twodog.gdunit/README.md), with
MTP discovery and execution tested by [`tests/gdunit-mtp`](../gdunit-mtp/README.md).
The `.gdtest` fixture suffix remains an internal fixture name.

The package uses official GdUnit 6.2.2, pinned by version and archive SHA-256 in
`twodog.gdunit/GdUnitRuntime.props`. Each configuration
reports five pure GDScript cases (three passes, one expected failure, one skip)
and one passing mixed-scene case. GDScript lint and C# formatting checks pass.

From the repository root, after packing the current bindings and engine:

```text
dotnet pack twodog.gdunit
dotnet run --project tests/gdscript-host -c Debug
dotnet run --project tests/gdscript-host -c Release
dotnet run --project tests/gdscript-host -c Editor
```

The executable returns zero only when every check passes. Its GDScript fixture
intentionally contains one failed assertion and one skipped test; those outcomes
are verified as part of the proof. An unexpected native warning/error, missing
event, changed source file, or incorrect result fails the executable. This is an
integration verification executable. The separate MTP host tests verify
`dotnet test` support and its result/exit-code contracts.

The test requires the current packed `2dog.engine`, bindings, desktop natives and
`2dog.tools`. It obtains import paths from those packages at build time and runs
the import helper in a separate process. No external Godot executable or
`GODOT_BIN` is required. The packed `2dog.gdunit` supplies the pinned
framework files. It runs headless; rendering remains a separate experiment.

Artifacts are retained under `bin/probe-artifacts/<run>/`. Pass a new output
directory after `--` to choose another location. Each fixture produces a source
copy, private stages, import logs, `godot.log`, and structured `events.json`.
The pure fixture also produces the ordinary game's pack and its contents list.
The checked-in fixtures receive no test imports; the mixed game's normal C#
build output still goes to its SDK-managed `.godot/mono` directory.

## What the proof verifies

- A normal import skips the test host's `.gdignore` tree. A stage beneath that
  ignored host imports only the selected test roots, with original `res://`
  paths. A nested ignored directory containing invalid GDScript stays excluded.
- A linked file stages as a copy of its target, while a linked directory is
  rejected.
- Game global classes and existing UIDs survive staging. Test UIDs, textures,
  and the test suite's global class appear only in the private project. Source
  resources, settings, sidecars, and import caches remain byte-for-byte unchanged
  through test import and execution.
- Staged settings preserve autoloads and replace the game's main scene with the
  bridge. Test scenes load their relative texture references, and game scripts
  resolve through their original UIDs.
- GdUnit discovers an ordinary pass, an assertion failure with a line number, a
  skip, and two parameterized async cases. Frame and timer awaits complete;
  per-test and suite teardown run; event GUIDs correlate discovery and results.
- A second engine runs a scene containing C# and GDScript components, proving
  that the staged resource root and the host's game assembly output work together.
- Newly added/deleted test assets appear in fresh stages, and changing the source
  does not change an earlier stage. A normal game export contains no host or
  GdUnit resources, even while the stages remain beneath the ignored host.

## Integration decision

Use the official release with a thin host adapter. Packaging downloads and
verifies the release archive; no upstream source modifications or fork checkout
are required. `twodog.gdunit/runtime/bridge/runner.gd` extends GdUnit's session
runner, uses its discovery callback and serialized events, disables retries,
preserves session hooks, and replaces process quit with a completion message.
.NET pumps on the thread that started the engine, disposes it, and waits for
`Completion` before the next fixture.

The private project enables
[`debug/settings/gdscript/always_track_call_stacks`](https://docs.godotengine.org/en/4.6/classes/class_projectsettings.html#class-projectsettings-property-debug-settings-gdscript-always-track-call-stacks)
before startup. Release natives otherwise omit the call stacks GdUnit needs to
report assertion source lines. The original game's setting is preserved.

GdUnit's ignored report-template folders must be copied with their `.gdignore`
markers: their HTML/CSS are runtime `FileAccess` inputs. The stage includes the
addon runtime and its license, and excludes its self-tests. It omits game build
project files, so GdUnit4Net's C# test adapter is not loaded; ordinary C# game
scripts still resolve from `ProjectAssemblyDir`.

The adapter currently depends on internal GdUnit session APIs at the pinned
release. Keep compatibility checks when updating that release. Each invocation
uses fresh copies; snapshot caching is future work. The pump timeout is
cooperative and cannot interrupt a native call that hangs. MTP IDs, selection,
cancellation and NuGet distribution now live in `2dog.gdunit`. Verified IDE
support, rendering, published host relocation and CLI scaffolding remain future
work.
