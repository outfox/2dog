# gdUnit MTP contracts

These hosts consume the packed `2dog.gdunit` package. They share the pure and
mixed-scene fixtures with `tests/gdscript-host`, and use .NET 10's native MTP
runner through this directory's `global.json`.

After packing the current engine, bindings, desktop natives and editor tools:

```text
dotnet pack twodog.gdunit
uv run python tests/gdunit-mtp/test_host.py --configuration Debug
uv run python tests/gdunit-mtp/test_host.py --configuration Release
uv run python tests/gdunit-mtp/test_host.py --configuration Editor
```

Packing downloads and verifies the official GdUnit release pinned in
`twodog.gdunit/GdUnitRuntime.props`; no GdUnit source checkout is needed.
Subsequent packs can reuse the cached archive.

Add `--precision double` after packing the double precision packages. Artifacts
are retained in `bin/contracts/`; each case records its name, console output,
private stages, import/native logs, and serialized gdUnit events. Short artifact
directory names keep gdUnit's Windows native directory operations within their
path length limits.

The thirteen contracts cover discovery without execution, stable IDs and original
source locations, exact parameter row selection, name filters and empty
selection, assertion/skip outcomes, mixed C#/GDScript scene loading, suite hook
failures, invalid scripts and escaping paths, empty projects, overlapping roots,
cancellation with suite teardown, the packaged runtime/license/templates and
release provenance, rejection of a corrupted release archive, and the documented
consumer built outside the repository with stock .NET.
Every case verifies that the original fixture files remain unchanged. The
fixture-only `GDUNIT_TEST_*` environment variables let the harness create
isolated error and cancellation projects without changing the shared game.

The pure fixture intentionally fails one assertion and skips one test. A direct
MTP run returns 2; the regression harness succeeds only if that failure is
reported correctly. The mixed fixture passes normally.

For manual checks, run from this directory:

```text
dotnet test --project pure/gdunit.pure.csproj --list-tests
dotnet test --project pure/gdunit.pure.csproj --gdunit-filter "*test_async_parameter*"
dotnet test --project mixed/gdunit.mixed.csproj
```

The CI pack job includes `2dog.gdunit` for both precisions. All Windows, Linux
and macOS test jobs run these contracts in Debug, Release and Editor as part of
the shared MTP host suite. IDE integration still needs validation.
