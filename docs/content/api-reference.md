# API Reference

The 2dog API is deliberately small. Use `twodog.Engine` to own one Godot
instance, or use the fixture classes when xUnit owns the process.

## Engine

| Type | Use it to |
| --- | --- |
| [`twodog.Engine`](./api/engine) | Configure, start, run, and stop embedded Godot |
| [`Godot.GodotInstance`](./api/godot-instance) | Pump or control the running engine instance |

`Engine` is the normal entry point. It comes from the `2dog.engine` package;
`GodotInstance` is the handle returned by `Engine.Start()`.

## Testing

| Type | Use it to |
| --- | --- |
| [`GodotFixtureBase`](./api/godot-fixture-base) | Build a fixture with custom Godot arguments |
| [`GodotFixture`](./api/godot-fixture) | Run tests with rendering enabled |
| [`GodotHeadlessFixture`](./api/godot-headless-fixture) | Run headless tests, including CI |
| [`GodotCollection`](./api/godot-collection) | Share a rendered fixture across an xUnit collection |
| [`GodotHeadlessCollection`](./api/godot-headless-collection) | Share a headless fixture across an xUnit collection |
| [`AssemblyPreloader`](./api/assembly-preloader) | Preload game assemblies in a custom test host |

The fixtures ship in `2dog.engine`. The ready-made collection definitions ship
in `2dog.xunit`, which includes the engine package.

::: info Looking for Godot classes?
2dog exposes the ordinary GodotSharp API after the engine starts. Use the
[Godot class reference](https://docs.godotengine.org/en/latest/classes/) for
nodes, resources, scenes, and other engine types.
:::

## Lifetime at a Glance

```csharp
using var engine = new twodog.Engine(
    "MyGame", twodog.Engine.ResolveProjectDir(), args);
using var godot = engine.Start();

while (!godot.Iteration())
{
    // One frame has completed.
}
```

Dispose `GodotInstance` before its owning `Engine`. Only one classic instance
can run in a process at a time; see [Single Godot Instance](./known-issues/single-instance).
