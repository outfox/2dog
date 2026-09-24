---
title: Version
description: "Choose which version of the 2dog tool to run, keep project package versions fixed, and understand what global.json controls."
---

# Version

You can choose the version of the **2dog tool** you run and the version of the
**2dog packages** your project uses. These are separate settings.

## Run a specific tool version

Add `@` and the version number after `2dog`:

```bash
dnx 2dog@:2dog-version: doctor
```

Replace the number with the version you want to keep using. This runs `doctor`
from that release of the tool. If it is not already downloaded, `dnx` downloads it.

You can also write the same command with `--version`:

```bash
dnx --version :2dog-version: 2dog doctor
```

Use the same syntax with other commands. For example, this previews a project
update using the selected tool version:

```bash
dnx 2dog@:2dog-version: update --dry-run
```

Remove `--dry-run` to apply the update. The update command changes the project's
package versions to the tool's versions; it does not downgrade newer versions.
See [update](/cli/update) for details.

## Print the version

Use the word `version` to show the tool's version and its package versions:

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

A version without `*` does not follow each new release. A normal build or restore
does not rewrite this value. Running `2dog update` can change it.

This property does not select the tool that `dnx` runs. Use `@version` for that.
`global.json` selects SDK versions, not the 2dog tool or engine package version.

## Build without restoring packages

If the project has already been restored and its packages are available locally,
you can skip restore:

```bash
dotnet run --project MyGame.2dog --no-restore
```

Replace `MyGame.2dog` with your host project. Choosing a version and skipping
restore are different: a fixed version may still need downloading if it is missing.
