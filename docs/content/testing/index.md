---
title: Testing
description: "Test your Godot game with GdUnit, xUnit or NUnit, from a first test to signals and simulated input."
tagline: "Learn how to test your Godot game, from a first test to simulated input."
---

# Testing

Load scenes, click buttons, and check signals from a normal .NET test runner.
2dog starts a real Godot engine for your tests, so they use your game's scripts
and resources.

![Illus. Moritz Voss: a white anthro dog in a hacker hoodie and their robot dog sniffing the ground](../img/2dog-investigation.png)
*"Find those bugs, wherever they hide."*

2dog supports several popular testing frameworks, but also provides generic fixtures just in case you need something specific.

## Choose your framework

Test hosts are opt-in. Choose `--xunit`, `--gdunit` or `--nunit` when adding one.

:::: columns
::: column xUnit
- Headless and rendering collections
- One engine shared across several test classes
- Custom engine arguments
- Combinatorial and pairwise rows through `TestMatrix`
- Windowed tests on macOS through the main-thread runner
:::
::: column NUnit
- Headless fixtures on all desktop platforms
- Rendering fixtures on Windows and Linux
- One engine per test class or parameterized fixture
- Async setup and teardown hooks
- Built-in combinatorial and pairwise test cases
:::
::::

Both integrations provide frame waits, signal expectations, input simulation,
async lifecycle support, and automatic checks for Godot errors and warnings.

::: details Minor differences
| Capability | xUnit | NUnit |
| --- | --- | --- |
| Engine shared across test classes | Collection fixture | One engine per fixture; no cross-class sharing |
| Rendering on macOS | Main-thread runner included | Needs a dedicated runner; use headless tests for now |
| Class-wide configuration matrix | Separate fixture types and collections | `[TestFixture(...)]` |
| Method input combinations | `TestMatrix` with `[MemberData]` | `[Combinatorial]` and `[Pairwise]` |
:::

## Test runners

All three scaffolded hosts run on Microsoft.Testing.Platform (MTP):

| Host   | MTP integration                                            | Setup                                                          |
|--------|------------------------------------------------------------|----------------------------------------------------------------|
| GdUnit | 2dog's framework drives the embedded GDScript runner       | [Configuration](/configuration/gdunit)                         |
| NUnit  | Official NUnit adapter bridge                              | [Configuration](/configuration/nunit#microsofttestingplatform) |
| xUnit  | Upstream xUnit runner with 2dog's owner-thread entry point | [Configuration](/configuration/xunit#microsofttestingplatform) |

Scaffolding a test host creates a root `global.json` that selects MTP, unless
the project already has one; 2dog never edits an existing file. Run
`dotnet test` from the project root to run every test host. To select MTP in
your own `global.json`, add:

```json
{ "test": { "runner": "Microsoft.Testing.Platform" } }
```

[`2dog doctor`](/cli/doctor) reports hosts or a `global.json` still on VSTest
as `layout.test-runner`. See Microsoft's [`dotnet test` guide](https://learn.microsoft.com/dotnet/core/testing/unit-testing-with-dotnet-test)
for runner selection and options such as `--coverage`.

## Find a guide

| Task                                                | Guide                                                                                      |
|-----------------------------------------------------|--------------------------------------------------------------------------------------------|
| Set up a test project                               | [xUnit](./xunit) or [NUnit](./nunit)                                                       |
| Run GDScript suites through `dotnet test`           | [GdUnit](./gdunit)                                                                         |
| Wait for frames, check signals, or verify deletion  | [Writing engine tests](./writing-tests)                                                    |
| Click controls or send keyboard events              | [Simulating input](./input)                                                                |
| Change packages, build variants, or import settings | [xUnit configuration](/configuration/xunit) or [NUnit configuration](/configuration/nunit) |

::: tip Keep simple tests simple
Code that does not use Godot can stay in ordinary unit tests. Use an engine
fixture when the behavior depends on scenes, resources, or Godot's main loop.
:::
