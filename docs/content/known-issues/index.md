# Known Issues

2dog is early-stage software currently published as **prerelease packages**. While it's functional and actively developed, you may encounter rough edges, limitations, or breaking changes between versions.

This section documents known limitations and their workarounds to help you work around current constraints.

## Current Issues

- **[Single Godot Instance](./single-instance)** — Only one Godot instance can exist per process
- **[xUnit Test Discovery](./xunit-discovery)** — Using Godot types in `[MemberData]` crashes the test runner
- **[GD.Print in Tests](./gd-print-output)** — `GD.Print` output is hidden by default in test runs

If you encounter issues not listed here, please [open an issue on GitHub](https://github.com/outfox/2dog/issues).
