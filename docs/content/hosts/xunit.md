---
title: xUnit
description: "Add an xUnit test project alongside your Godot game."
tagline: "Learn how to test your Godot game with xUnit."
keywords: "Godot xUnit, Godot C# unit testing"
---

# xUnit

The xUnit host is a .NET test project that runs your game in a real Godot
engine. The setup uses a headless fixture; rendering is available too.

![Illus. Moritz Voss: a white anthro dog in a hacker hoodie and their robot dog sniffing the ground](../img/2dog-investigation.png)

::: tip It's what we use!
**⤜outfox⤏** tests primarily on xUnit in production, it's great! :thumbsup:
:::

Run these commands from your Godot project directory:

```bash
dnx 2dog add --xunit
dotnet test MyGame.xunit
```

Start with `MyGame.xunit/BasicTests.cs`, then follow
[Testing with xUnit](/testing/xunit) to write your own tests.
See [xUnit configuration](/configuration/xunit) for build settings.

::: info Existing test projects
Older `.tests` hosts and hosts with custom names still work. Use their
existing paths with `dotnet test`. `2dog add` and `2dog update` do not rename
them.
:::
