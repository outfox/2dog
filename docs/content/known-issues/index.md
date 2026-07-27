---
title: Known Issues
description: "Current 2dog limitations and their workarounds: one Godot instance per load context, xUnit discovery crashes with Godot types, and GD.Print visibility in tests."
---

# Known Issues

2dog is early-stage software. These pages track current limitations and their
workarounds.

## Current Issues

- **[Single Godot Instance](./single-instance)**: one instance per assembly load context at a time; sequential restart is supported and isolated concurrent hosting is experimental
- **[xUnit Test Discovery](./xunit-discovery)**: Godot types in `[MemberData]` crash discovery because the runner cannot resolve them then
- **[GD.Print in Tests](./gd-print-output)**: `GD.Print` output is hidden by default

If you encounter issues not listed here, please [open an issue on GitHub](https://github.com/outfox/2dog/issues).
