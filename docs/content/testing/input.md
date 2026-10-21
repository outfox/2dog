---
title: Simulating Input
description: "Click Godot controls and send keyboard events in xUnit and NUnit tests."
tagline: "Learn how to click buttons and press keys in your Godot tests."
---

# Simulating input

Use the fixture's `Input` helper to interact with your game through Godot's
input system. The examples use `godot`; in NUnit, add `var godot = EngineFixture;`
inside your test.

## Click a control

Settle the layout, click, then check what happened:

```csharp
godot.Engine.Iteration();
using var pressed = GodotAssert.ExpectSignal(playButton, BaseButton.SignalName.Pressed);
godot.Input.Click(playButton);
pressed.AssertEmitted();
```

`Click(control)` aims at the control's center in its owning viewport. Godot
handles hit testing and event consumption, so an overlay or input handler can
block the click. The helper sends motion, press, and release events; it does
not advance frames.

| Aim at                       | Call                                                                |
|------------------------------|---------------------------------------------------------------------|
| A point within a control     | `godot.Input.Click(playButton, localPosition: new Vector2(10, 10))` |
| A point in the root viewport | `godot.Input.Click(new Vector2(80, 40))`                            |
| A point in another viewport  | `godot.Input.Click(new Vector2(80, 40), viewport: gameViewport)`    |

::: tip Give headless UI tests enough room
Headless, the root window can start at its minimum size, which skews layout
and hit testing. Set it to the project's viewport size before loading your
scene, then settle the layout:

```csharp
var root = godot.Tree.Root;
var originalSize = root.Size;
try
{
    root.Size = new Vector2I(
        ProjectSettings.GetSettingWithOverride("display/window/size/viewport_width").AsInt32(),
        ProjectSettings.GetSettingWithOverride("display/window/size/viewport_height").AsInt32());
    // Add your scene here.
    godot.Engine.Iteration();
    // Click controls and assert the results.
}
finally { root.Size = originalSize; }
```

The fixture is shared, so remove your scene and restore the size afterwards.
For isolated controls, use a `SubViewport` with an explicit size:

```csharp
var viewport = new SubViewport { Size = new Vector2I(400, 300) };
try
{
    godot.Tree.Root.AddChild(viewport);
    // Add your controls here, then advance a frame before clicking.
}
finally { viewport.Free(); }
```
:::

::: details What can block a click?
Foreground controls, mouse filters, clipping, hidden or disabled controls,
paused processing, and viewport input suppression keep their normal behavior.
An `_Input` handler can consume the event before GUI handling. Check a signal
or state change to confirm receipt.

The control overload targets its owning viewport directly. To test an outer
overlay or `SubViewportContainer` forwarding, inject at the outer viewport's
coordinates. Desktop window focus and OS event delivery need a windowed
integration test.
:::

## Send keyboard or custom events

`PushToViewport` sends an `InputEvent` in viewport coordinates. Keyboard events
go to the current focus owner; click or tab through the UI first when testing
focus acquisition.

```csharp
using var down = new InputEventKey { Keycode = Key.Space, Pressed = true };
using var up = new InputEventKey { Keycode = Key.Space, Pressed = false };
try { godot.Input.PushToViewport(down, gameViewport); }
finally { godot.Input.PushToViewport(up, gameViewport); }
```

Construct touch, drag, or wheel events the same way. Dispose custom events
that your test creates.

::: details Hover in a standalone SubViewport
For a manually managed `SubViewport`, call `viewport.NotifyMouseEntered()`
when the pointer enters and `NotifyMouseExited()` when it leaves. Windows and
`SubViewportContainer` normally handle these notifications during forwarding;
the helper does not send them with every click.
:::

## Test code that polls Input

Choose the delivery method based on what your game reads:

| Method                     | Delivery                                | Updates global `Godot.Input` state? |
|----------------------------|-----------------------------------------|-------------------------------------|
| `Click` / `PushToViewport` | Local viewport routing                  | No                                  |
| `Send`                     | Godot's input server and window routing | Yes                                 |

For `IsKeyPressed`, `IsMouseButtonPressed`, or action polling, use `Send`:

```csharp
using var down = new InputEventKey { Keycode = Key.F13, Pressed = true };
using var up = new InputEventKey { Keycode = Key.F13, Pressed = false };
try
{
    godot.Input.Send(down);
    // Godot.Input.IsKeyPressed(Key.F13) is now true.
    godot.Engine.Iteration(); // Let game code react.
}
finally { godot.Input.Send(up); }
```

## Spoof mouse movement

Send a mouse-motion event to test hover, `_Input`, or code that polls the
root viewport's mouse position:

```csharp
using var motion = new InputEventMouseMotion
{
    Position = new Vector2(80, 40),
    GlobalPosition = new Vector2(80, 40),
    Relative = new Vector2(10, 0),
    ScreenRelative = new Vector2(10, 0)
};
godot.Input.Send(motion);
```

`Send` uses window coordinates. With stretch scaling, convert a logical
viewport point using `godot.Tree.Root.GetFinalTransform() * viewportPosition`.
Set `Relative` when testing code that uses movement deltas, such as a camera.

`PushToViewport(motion, viewport)` uses viewport coordinates and delivers
motion and hover too. It leaves global `Input` state unchanged, including the
mouse position used by the headless root's `GetMousePosition()`. A standalone
`SubViewport` tracks its local mouse position from the events it receives.
Neither method moves the OS cursor; mouse warping is unnecessary for these
tests.

::: warning Release held input
The fixture is shared between tests. Always release keys, actions, and mouse
buttons in `finally`, and use a fresh event for each press and release. Sending
one event through both delivery methods delivers it twice.
:::

::: details Input-server behavior
`Send` calls `Input.ParseInputEvent` and immediately flushes buffered input,
including other pending events. Project input emulation and window routing
apply; coordinates use Godot's device/window conventions. GUI event consumption
does not clear global polling state.

See Godot's [viewport delivery](https://docs.godotengine.org/en/stable/classes/class_viewport.html#class-viewport-method-push-input)
and [input server](https://docs.godotengine.org/en/stable/classes/class_input.html#class-input-method-parse-input-event)
references for the underlying APIs.
:::
