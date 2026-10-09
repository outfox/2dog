---
title: xUnit
description: "Run tests against your Godot project with the 2dog.xunit host."
---

# xUnit

The xUnit host runs tests against your Godot project using a real engine.
Tests run headless by default; an xUnit fixture owns the engine's lifetime.

## Use It

From your Godot project directory:

```bash
dnx 2dog add --tests
dotnet test MyGame.xunit
```

Edit `MyGame.xunit/BasicTests.cs` to add tests. The template includes async/await,
signal arguments and expectations, enter/exit-tree signals, timers and deferred
deletion examples. See
[Testing with xUnit](/testing#async-work-signals-and-deferred-deletion) for the helpers.

Existing `.tests` hosts and custom project names remain supported; adding or
updating hosts does not rename them. Use their existing paths with `dotnet test`.

Use `HeadlessFixture` for headless tests or `Fixture` for rendered tests. See
[Testing with xUnit](/testing) for examples, collections, and CI setup.

See [xUnit Configuration](/configuration/xunit) for native variants and Editor tests.

## Shared State

Tests in a collection share one engine. Keep collection parallelism disabled
and free nodes you create. See [Single Godot Instance](/known-issues/single-instance)
for engine lifetime rules.
