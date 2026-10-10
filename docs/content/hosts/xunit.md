---
title: xUnit
description: "Add an xUnit test project alongside your Godot game."
---

# xUnit

The xUnit host is a .NET test project that runs your game in a real Godot
engine. The scaffold uses a headless fixture; rendering is available too.

From your Godot project directory:

```bash
dnx 2dog add --xunit
dotnet test MyGame.xunit
```

Start with `MyGame.xunit/BasicTests.cs`, then follow
[Testing with xUnit](/testing/xunit) to write your own tests.
See [xUnit configuration](/configuration/xunit) for build settings.

::: info Existing test projects
Older `.tests` hosts and custom names still work. Use their existing paths
with `dotnet test`; adding or updating a host does not rename them.
:::
