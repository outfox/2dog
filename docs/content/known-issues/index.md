---
title: Known Issues
description: "Current 2dog limitations and their workarounds: one Godot instance per load context, xUnit discovery crashes with Godot types, GD.Print visibility in tests, and spaced project names breaking publish."
---

# Known Issues

2dog is early-stage software. These pages track current limitations and their
workarounds.

## Current Issues

- **[Single Godot Instance](./single-instance)**: one instance per assembly load context at a time; sequential restart is supported and isolated concurrent hosting is experimental
- **[xUnit Test Discovery](./xunit-discovery)**: Godot types in `[MemberData]` crash discovery because the runner cannot resolve them then
- **[GD.Print in Tests](./gd-print-output)**: `GD.Print` output is hidden by default
- **[Spaced Project Names](./spaced-project-names)**: whitespace in the game's .NET name makes publish silently drop its NuGet dependencies (.NET SDK bug); `2dog add --rename` fixes it

If you encounter issues not listed here, please [open an issue on GitHub](https://github.com/outfox/2dog/issues).
