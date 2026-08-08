---
title: API Reference
description: "Overview of the deliberately small 2dog API: twodog.Engine, Godot.GodotInstance, the twodog.Testing fixtures, xUnit collections, and object lifetimes at a glance."
---

# API Reference

The 2dog API is deliberately small. Use `twodog.Engine` to own one Godot
instance, or use the fixture classes when xUnit owns the process.

## `twodog`

| Type | Use it to |
| --- | --- |
| [`Engine`](./api/engine) | Configure, start, run, and stop embedded Godot |

`Engine` is the normal entry point from the `2dog.engine` package.

## `Godot`

| Type | Use it to |
| --- | --- |
| [`GodotInstance`](./api/godot-instance) | Pump or control the instance returned by `Engine.Start()` |

2dog adds `GodotInstance` to the GodotSharp API. Use the
[Godot class reference](https://docs.godotengine.org/en/latest/classes/) for
nodes, resources, scenes, and other engine types.

## `twodog.Testing`

| Type | Use it to |
| --- | --- |
| [`FixtureBase`](./api/fixture-base) | Build a fixture with custom Godot arguments |
| [`Fixture`](./api/fixture) | Run tests with rendering enabled |
| [`HeadlessFixture`](./api/headless-fixture) | Run headless tests, including CI |

These fixtures ship in `2dog.engine` and do not depend on a specific test
framework.

## `twodog.Testing.Xunit`

| Type | Use it to |
| --- | --- |
| [`RenderingCollection`](./api/rendering-collection) | Share a rendered fixture across an xUnit collection |
| [`HeadlessCollection`](./api/headless-collection) | Share a headless fixture across an xUnit collection |

The collection definitions ship in `2dog.xunit` and compile into the consuming
test assembly so xUnit can discover them.

## Lifetime at a Glance

```csharp
using var engine = new twodog.Engine("MyGame", args: args);
using var godot = engine.Start();

while (!godot.Iteration())
{
    // One frame has completed.
}
```

Dispose `GodotInstance` before its owning `Engine`. Only one classic instance
can run in a process at a time; see [Single Godot Instance](./known-issues/single-instance).
