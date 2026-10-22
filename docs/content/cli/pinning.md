---
title: Pinning
description: "Choose which version of the 2dog tool to run and keep your project's package versions fixed."
tagline: "Learn how to pin the 2dog tool and keep your package versions steady."
---

# Pinning a specific Version

2dog already pins your project to a specific version in
`Directory.Build.props`. You can edit this file manually.

You can also choose the version of the **2dog tool** that you run. Separately,
you can choose the version of the **2dog packages** that the tool uses.

## Run a specific tool version

Add `@` and the version number after `2dog`:

```bash
dnx 2dog@:2dog-version: doctor
```
Replace the number with the version that you want to use. This example runs
`doctor` from that release of the tool. If that release is not on your computer,
`dnx` downloads it.

Use the same syntax with other commands. For example, this command previews a
project update with the selected tool version:

```bash
dnx 2dog@:2dog-version: update --dry-run
```

Remove `--dry-run` to apply the update. The update command changes the project's
package versions to the tool's versions; it does not downgrade newer versions.
See [update](/cli/update) for details.

## Print the version

Use the `version` command to show the tool's version and its package versions:

```bash
dnx 2dog@:2dog-version: version
```

`--version` belongs to `dnx` and **chooses** the tool version.
`version` is a 2dog command and **prints** version information.
See the [version command reference](/cli/version) for the output format.

## Keep the project's package versions

Projects created by 2dog store their package versions in `Directory.Build.props`.
For example:

```xml
<Project>
  <PropertyGroup Label="2dog">
    <TwoDogVersion>:2dog-version:</TwoDogVersion>
  </PropertyGroup>
</Project>
```

This is a shortened example; keep the other version properties in your file.
Projects use `$(TwoDogVersion)` in their package references.

Only `2dog update` changes this value. This value does not select the tool that
`dnx` runs. To select the tool, use `@version`.
