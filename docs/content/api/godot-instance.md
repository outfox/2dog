# `Godot.GodotInstance`

Controls the running Godot instance returned by [`Engine.Start()`](./engine#start).

```csharp
public class GodotInstance : IDisposable
```

**Package:** `2dog.engine`  
**Namespace:** `Godot`

Create this object through `Engine.Start()` rather than its low-level static
factory methods.

## Methods

### `Iteration`

```csharp
public bool Iteration()
```

Processes one main-loop frame. Returns `true` when Godot wants to quit.

```csharp
while (!godot.Iteration())
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

Notify Godot that the host has gained or lost focus. Ordinary desktop hosts let
Godot's display server handle focus and do not call these methods directly.

### `Pause` and `Resume`

```csharp
public void Pause()
public void Resume()
```

Notify Godot that the host application has paused or resumed. These lifecycle
hooks are useful when another application framework owns the outer window.

### `Dispose`

```csharp
public void Dispose()
```

Shuts down the native instance. Dispose it before its owning `Engine`.

::: warning Choose one loop owner
Call `Iteration()` yourself or call `Engine.Run()`. Do not use both for the same
instance.
:::
