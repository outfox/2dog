---
title: Testing
description: "Test your Godot game with GdUnit, xUnit or NUnit, from a first test to signals and simulated input."
---

# Testing

Load scenes, click buttons, and check signals from a normal .NET test runner.
2dog starts a real Godot engine for your tests, so they use your game's scripts
and resources.

![Illus. Moritz Voss: a white anthro dog in a hacker hoodie and ther robot dog sniffing the ground](../img/2dog-investigation.png)
*"Find those bugs, where ever they hide."*

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

xUnit already supports async setup and cleanup through `IAsyncLifetime`.
NUnit's attributes make those hooks more explicit; this is a difference in
style, rather than missing async support.
:::

## Test runners

All three scaffolded hosts run on Microsoft.Testing.Platform (MTP):

| Host | MTP integration | Setup |
| --- | --- | --- |
| GdUnit | 2dog's framework drives the embedded GDScript runner | [Configuration](/configuration/gdunit) |
| NUnit | Official NUnit adapter bridge | [Configuration](/configuration/nunit#microsofttestingplatform) |
| xUnit | Upstream xUnit runner with 2dog's owner-thread entry point | [Configuration](/configuration/xunit#microsofttestingplatform) |

The .NET 10 SDK runs every project of a `dotnet test` run on the runner that
`global.json` selects. When 2dog scaffolds a test host into a project without
a root `global.json`, it creates one that selects MTP, so `dotnet test` from
the project root runs all of them. The SDK looks for `global.json` from the
working directory, not from the project path: `dotnet test MyGame/MyGame.xunit`
from the parent directory falls back to VSTest and fails with "Testing with
VSTest target is no longer supported". Every scaffolded host references
`Microsoft.Testing.Extensions.CodeCoverage`, so `dotnet test --coverage`
collects coverage for all of them. NUnit's adapter has a
[session cancellation limitation](/configuration/nunit#cancellation);
use its cooperative test timeout before any outer runner timeout.

## Find a guide

| Task | Guide |
| --- | --- |
| Set up a test project | [xUnit](./xunit) or [NUnit](./nunit) |
| Run GDScript suites through `dotnet test` | [GdUnit](./gdunit) |
| Wait for frames, check signals, or verify deletion | [Writing engine tests](./writing-tests) |
| Click controls or send keyboard events | [Simulating input](./input) |
| Change packages, build variants, or import settings | [xUnit configuration](/configuration/xunit) or [NUnit configuration](/configuration/nunit) |

::: tip Keep simple tests simple
Code that does not use Godot can stay in ordinary unit tests. Use an engine
fixture when the behavior depends on scenes, resources, or Godot's main loop.
:::
