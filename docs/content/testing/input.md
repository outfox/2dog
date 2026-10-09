---
title: Simulating Input
description: "Simulate Godot input in xUnit and NUnit through the input server and viewport router, preserving GUI hit testing, focus and event consumption."
---

# Simulating Input

Every `twodog.Testing.FixtureBase` exposes `Input`, a `TestInput` helper in
`2dog.engine`. Use `godot.Input` in an xUnit collection or `Godot.Input` in a
derived NUnit fixture. Both use the same implementation on the engine thread.

## Aim at a Control

```csharp
godot.Engine.Iteration(); // Settle layout first.
godot.Input.Click(playButton);
godot.Input.Click(playButton, localPosition: new Vector2(10, 10));
```

`Click(control)` transforms its center, or the supplied control-local point,
into its owning viewport's coordinates. It sends mouse motion, a press and
a release through `Viewport.PushInput`. Godot chooses the recipient through
its normal input and GUI stages. The helper does not call `_GuiInput`, emit
`Pressed`, grab focus explicitly, or guarantee that the aimed-at control
receives the event.

Foreground controls, mouse filters, clipping, visibility, disabled buttons,
paused processing and viewport input suppression retain their normal behavior.
An `_Input` handler can consume the event before GUI handling. Assert the
resulting signal or state to distinguish a received click from a blocked one.
Layout, animation and deferred changes still need frames; clicks do not advance
the engine automatically.

Headless windows can have a small root viewport. Check `GetVisibleRect()` or
create a `SubViewport` with an explicit size for isolated UI tests, as in the
[NUnit example](/testing/nunit#fixture-lifetime). Aiming at a point outside the
viewport does not make it visible or clickable.

For an explicit viewport-local point:

```csharp
godot.Input.Click(new Vector2(80, 40)); // Root viewport.
godot.Input.Click(new Vector2(80, 40), viewport: gameViewport);
```

The control overload targets its **owning viewport**. It does not simulate
forwarding through an outer `SubViewportContainer`, another window or the OS.
To test outer overlays or forwarding, inject at the outer viewport's coordinates
instead. Tests of desktop window focus and OS input delivery need a windowed
integration test.

## Keyboard Focus and Custom Events

`PushToViewport` accepts any `InputEvent`, in viewport coordinates. Keyboard
events go to the existing focus owner; they are not addressed to a control.
To test focus acquisition, click or tab through the UI first.

```csharp
using var down = new InputEventKey { Keycode = Key.Space, Pressed = true };
using var up = new InputEventKey { Keycode = Key.Space, Pressed = false };
try { godot.Input.PushToViewport(down, gameViewport); }
finally { godot.Input.PushToViewport(up, gameViewport); }
```

Touch, drag and wheel tests can construct the corresponding Godot events and
send them through this same method. The caller owns and disposes custom events.
For hover tests in a manually managed standalone `SubViewport`, call
`viewport.NotifyMouseEntered()` once when the pointer enters, and
`NotifyMouseExited()` when it leaves. Windows and `SubViewportContainer` manage
these notifications during normal input forwarding; the helper does not force
mouse-enter notifications on every click.

## Global Input Polling

Viewport delivery is local routing. It does **not** update global
`Godot.Input.IsKeyPressed`, `IsMouseButtonPressed` or action state. Use `Send`
when the code under test polls `Input` or when testing normal window delivery:

```csharp
using var down = new InputEventKey { Keycode = Key.F13, Pressed = true };
using var up = new InputEventKey { Keycode = Key.F13, Pressed = false };
try
{
    godot.Input.Send(down);
    // Godot.Input.IsKeyPressed(Key.F13) is now true.
    godot.Engine.Iteration(); // Let _Process/_PhysicsProcess react if needed.
}
finally { godot.Input.Send(up); }
```

`Send` calls `Input.ParseInputEvent` and flushes buffered events immediately.
This also flushes any other pending input. Window routing and project input
emulation apply; event coordinates follow Godot's device/window conventions.
Use a fresh event object for every press and release, and always release held
keys, actions and mouse buttons in `finally`. The fixture is shared across tests.
Do not send the same event through both methods: that would deliver it twice.

These modes follow Godot's documented [viewport delivery](https://docs.godotengine.org/en/stable/classes/class_viewport.html#class-viewport-method-push-input)
and [input server](https://docs.godotengine.org/en/stable/classes/class_input.html#class-input-method-parse-input-event)
APIs. GUI event consumption does not clear global polling state.
