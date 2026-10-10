---
title: Testing
description: "Test your Godot game with xUnit or NUnit, from a first test to signals and simulated input."
---

# Testing

Load scenes, click buttons, and check signals from a normal .NET test runner.
2dog starts a real Godot engine for your tests, so they use your game's scripts
and resources.

## Choose your framework

Test hosts are opt-in. Choose `--xunit` or `--nunit` when adding one.

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

::: details Where they still differ
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

## Find a guide

| Task | Guide |
| --- | --- |
| Set up a test project | [xUnit](./xunit) or [NUnit](./nunit) |
| Wait for frames, check signals, or verify deletion | [Writing engine tests](./writing-tests) |
| Click controls or send keyboard events | [Simulating input](./input) |
| Change packages, build variants, or import settings | [xUnit configuration](/configuration/xunit) or [NUnit configuration](/configuration/nunit) |

::: tip Keep simple tests simple
Code that does not use Godot can stay in ordinary unit tests. Use an engine
fixture when the behavior depends on scenes, resources, or Godot's main loop.
:::
