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
dotnet test MyGame.tests
```

Edit `MyGame.tests/BasicTests.cs` to add tests. Use `HeadlessFixture` for headless
tests or `Fixture` for rendered tests. See [Testing with xUnit](/testing)
for examples, collections, and CI setup.

## Shared State

Tests in a collection share one engine. Keep collection parallelism disabled
and free nodes you create. See [Single Godot Instance](/known-issues/single-instance)
for engine lifetime rules.
