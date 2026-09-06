---
title: Godot.GodotInstance
description: "API reference for Godot.GodotInstance, the borrowed compatibility handle returned by Engine.Start()."
---

# `Godot.GodotInstance`

Borrowed compatibility handle returned by [`twodog.Engine.Start()`](./engine#start).

```csharp
public class GodotInstance : IDisposable
```

**Package:** `2dog.engine`  
**Namespace:** `Godot`

This class is one of the few differences from stock Godot, where it's not exposed.
Its `Engine` owns the native instance and its lifecycle.

Create this object through `twodog.Engine.Start()` rather than its low-level static
factory methods.

## Compatibility Methods

### `Iteration`

```csharp
public bool Iteration()
```

Processes one main-loop frame. Returns `true` when Godot wants to quit. New host
code should call `Engine.Iteration()`.

```csharp
while (!engine.Iteration())
{
    // One frame has completed.
}
```

### `IsStarted`

```csharp
public bool IsStarted()
```

Returns whether this instance has started.

### `FocusIn` and `FocusOut`

```csharp
public void FocusIn()
public void FocusOut()
```

Notify Godot that the host has gained or lost focus. Ordinary generic hosts let
Godot's display server handle focus and do not call these methods directly.

### `Pause` and `Resume`

```csharp
public void Pause()
public void Resume()
```

Notify Godot that the host application has paused or resumed. These lifecycle
hooks are useful when another application framework owns the outer window.

::: warning Choose one loop owner
Call `Engine.Iteration()` yourself or call `Engine.Run()`. Do not use both for
the same instance. Use `Engine.RequestQuit()`, `Completion`, `Exited`, and engine
disposal for lifecycle control; do not dispose this borrowed handle.
:::
