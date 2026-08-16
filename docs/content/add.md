---
title: Adding 2dog to Existing Projects
description: "The 2dog add command adds .NET hosts to an existing Godot project in place - usage, commands, options, what gets generated, safety, and requirements."
---

# Adding 2dog to Your Project

The `2dog` tool adds .NET hosts to an existing Godot project **in place**.
Your project directory becomes the solution root, while desktop, browser, and
test hosts live in nested folders that Godot ignores. Existing game content
stays put.

The command and `dotnet new` template ship together in the `2dog` package,
separate from `2dog.engine`. [The FAQ explains why.](/faq#why-is-the-library-a-separate-package-2dog-engine-instead-of-part-of-2dog)

## Usage

Run it without installing anything (.NET 10+ SDK):

```bash
cd path/to/your/godot/project
dnx 2dog add
```

Or install the command globally:

```bash
dotnet tool install -g 2dog
2dog add
```

With a `project.godot` in the current directory, the tool asks which hosts to
add, shows its plan, and waits for confirmation:

```text
2dog 4.7.1  https://2dog.dev

project  MyGame (/home/you/games/MyGame)

Which hosts do you want?
> [x] desktop   MyGame.2dog   your own Main(), runs the game on desktop
  [x] browser   MyGame.web    WebAssembly host, published as a static bundle
  [ ] tests     MyGame.tests  xUnit project driving a headless engine
```

Every prompt has a flag, so scripts and CI can run unattended:

```bash
dnx 2dog add path/to/MyGame --no-web   # everything except the web host
dnx 2dog add path/to/MyGame --dry-run  # show the plan; change nothing
```

Naming a host option, passing `--yes`, or passing `--non-interactive` disables
the prompts. The [dnx 2dog reference](/dnx-2dog) documents every command, host
flag, and option, including the `--version`-under-`dnx` trap.

::: tip From stock Godot to the browser

```bash
dnx 2dog add path/to/MyGame # change or confirm options as needed
cd path/to/MyGame
dotnet publish MyGame.web   # static site in MyGame.web/AppBundle/
```

Publishing requires `dotnet workload install wasm-tools`. See
[Browser Host](/hosts/web) for the build and local serving guide.
:::

## Result

The normal generated layout is documented once in
[Creating a New Project](/templates). For an existing project, 2dog creates or
updates the same solution and host folders around your content. Then run:

```bash
dotnet run --project MyGame.2dog
dotnet test MyGame.tests
dotnet publish MyGame.web
```

Projects without a C# project work too. The tool creates a `Godot.NET.Sdk`
project and configures `project.godot`; GDScript scenes and scripts continue to
run unchanged.

## Safety

The tool is incremental. Existing hosts are recognized, existing files are
skipped and reported, and a run with nothing to add is a no-op. Use
`--dry-run` before changing anything and `--force` only when you intend to
replace scaffolded files.

It does not:

- move, rename, or delete game content;
- touch version control, `.gitignore`, staging, or commits;
- overwrite an existing `global.json`, even with `--force`;
- guess when multiple root solutions contain the Godot project.

Two deliberate exceptions can occur, both announced in the plan: a classic
`.sln` is converted to `.slnx` and removed (an existing root `.slnx` is
reused), and [`--rename`](#project-names-with-spaces) moves the game csproj to
a space-free name when you ask it to.

The tool runs `dotnet restore` unless you pass `--no-restore`. A wasm-related
restore failure is reported as a warning with the command to install
`wasm-tools`.

## Project names with spaces

Godot happily names a C# project after the Godot project - `Fast Dragon.csproj`
for a project called "Fast Dragon". But whitespace in a project's .NET name
[silently breaks publishing](/known-issues/spaced-project-names) any host that
references it: the game's NuGet packages are dropped from the published output,
and the app fails at runtime with `FileNotFoundException` for the first missing
assembly. `2dog add` therefore refuses to scaffold hosts against such a name.

The fix is a rename of the *.NET identity only* - the Godot display name
(`config/name`) keeps its spaces. Interactively, `2dog add` offers the rename;
scripted, pass it explicitly:

```bash
2dog add --rename FastDragon
```

This renames the csproj, sets `[dotnet] project/assembly_name` in
`project.godot`, and repoints the solution, then scaffolds as usual. It is
only available while the project has no 2dog hosts yet; afterwards the error
message lists the manual steps instead (each host's `ProjectReference`,
`TrimmerRootAssembly`, and `RootNamespace` also carry the name).

## Requirements

- .NET 10 SDK, also required by `dnx`.
- The wasm-tools workload only when publishing the browser host:
  `dotnet workload install wasm-tools`.
