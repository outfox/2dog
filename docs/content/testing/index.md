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
- Windowed tests on macOS through the main-thread runner
:::
::: column NUnit
- Headless engine fixtures
- One engine per test class or parameterized fixture
- Async setup and teardown hooks
- Built-in combinatorial and pairwise test cases
:::
::::

Both integrations provide frame waits, signal expectations, input simulation,
and automatic checks for Godot errors and warnings.

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
