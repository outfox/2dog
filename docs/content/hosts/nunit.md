---
title: NUnit
description: "Add an NUnit test project alongside your Godot game."
tagline: "Learn how to test your Godot game with NUnit."
---

# NUnit

The NUnit host is a .NET test project that runs your game in a real headless
Godot engine on Windows, Linux, and macOS.

![Illus. Moritz Voss: a white anthro dog in a hacker hoodie and their robot dog sniffing the ground](../img/2dog-investigation.png)

From your Godot project directory:

```bash
dnx 2dog add --nunit
dotnet test MyGame.nunit
```

Start with `MyGame.nunit/BasicTests.cs`, then follow
[Testing with NUnit](/testing/nunit) to write your own tests.
See [NUnit configuration](/configuration/nunit) for build settings.

::: tip Starting a new game?
Use `dnx 2dog new MyGame --nunit`. Choose `--xunit` instead for an xUnit test host.
:::
