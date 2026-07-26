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
dnx 2dog
```

Or install the command globally:

```bash
dotnet tool install -g 2dog
2dog
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
the prompts.

::: tip From stock Godot to the browser
```bash
dnx 2dog add path/to/MyGame -y
cd path/to/MyGame
dotnet publish MyGame.web  # static site in MyGame.web/AppBundle/
```

Publishing requires `dotnet workload install wasm-tools`. See
[Web / Browser](/web) for the complete publishing guide.
:::

## Commands

| Command | Effect |
| --- | --- |
| `2dog` | Add hosts here, or create a project if there is none |
| `2dog new [Name] [dir]` | Create a new Godot project with 2dog hosts |
| `2dog add [path]` | Add hosts to an existing Godot project |
| `2dog convert [path]` | Alias of `add` for a project with no hosts yet |

## Options

| Option | Effect |
| --- | --- |
| `--desktop [folder]` | Add a desktop host, optionally in a named folder |
| `--web [folder]` | Add a browser (WebAssembly) host |
| `--tests [folder]` | Add an xUnit test project |
| `--no-desktop`, `--no-web`, `--no-tests` | Leave a host out of the default set |
| `-n, --name <BaseName>` | Project name (`new`) or base-name override |
| `-o, --output <dir>` | Directory for a new project |
| `-y, --yes` | Use the flags and defaults without prompting |
| `--non-interactive` | Same as `--yes` |
| `--dry-run` | Print planned actions without changing anything |
| `--force` | Overwrite existing scaffolded files; never delete or move files |
| `--no-restore` | Skip the final `dotnet restore` |
| `--verbose` | Show extra output |

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

One deliberate migration can occur: a classic `.sln` is converted to `.slnx`
and removed. An existing root `.slnx` is reused.

The tool runs `dotnet restore` unless you pass `--no-restore`. A wasm-related
restore failure is reported as a warning with the command to install
`wasm-tools`.

## Requirements

- .NET 10 SDK, also required by `dnx`.
- The wasm-tools workload only when publishing the browser host:
  `dotnet workload install wasm-tools`.
