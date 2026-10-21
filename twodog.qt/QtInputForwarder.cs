using System;
using System.Collections.Generic;
using Godot;
using GodotKey = Godot.Key;

namespace twodog;

/// <summary>
/// Turns the GodotView item's Qt input into Godot input events. Positions arrive in viewport pixels; the
/// hidden engine window never sees the pointer, so events go through Input.ParseInputEvent.
/// </summary>
internal sealed class QtInputForwarder
{
    // Qt::KeyboardModifier and Qt::MouseButton values.
    private const int ShiftModifier = 0x02000000;
    private const int ControlModifier = 0x04000000;
    private const int AltModifier = 0x08000000;
    private const int MetaModifier = 0x10000000;
    private const int KeypadModifier = 0x20000000;

    // Keyed by scan code: Qt reports a release under the modifiers current at release time, so the logical
    // key can differ from the press. Keys without a scan code fall back to their keycode.
    private readonly Dictionary<(uint Scan, GodotKey Key), GodotKey> _heldKeys = [];
    private Vector2 _lastPosition;
    private bool _hasLastPosition;
    private MouseButtonMask _buttonMask;

    public void Handle(in QtInput input)
    {
        switch (input.Type)
        {
            case QtInputType.MouseMove:
                SendMotion(input);
                break;
            case QtInputType.MousePress or QtInputType.MouseDoubleClick or QtInputType.MouseRelease:
                SendButton(input);
                break;
            case QtInputType.Wheel:
                SendWheel(input);
                break;
            case QtInputType.KeyPress or QtInputType.KeyRelease:
                SendKey(input);
                break;
            case QtInputType.FocusOut:
                ReleaseAll();
                break;
        }
    }

    /// <summary>Releases held keys and buttons, whose release events now go elsewhere.</summary>
    public void ReleaseAll()
    {
        foreach (var key in _heldKeys.Values)
            Dispatch(new InputEventKey { Keycode = key, PhysicalKeycode = key, KeyLabel = key, Pressed = false });
        _heldKeys.Clear();
        foreach (var button in (ReadOnlySpan<MouseButton>)
                 [MouseButton.Left, MouseButton.Right, MouseButton.Middle, MouseButton.Xbutton1, MouseButton.Xbutton2])
        {
            var mask = ToMask(button);
            if ((_buttonMask & mask) == 0) continue;
            _buttonMask &= ~mask;
            Dispatch(new InputEventMouseButton
            {
                ButtonIndex = button, Pressed = false, Position = _lastPosition, GlobalPosition = _lastPosition,
            });
        }
        _hasLastPosition = false;
    }

    // Disposed right after injection, as in 2dog.avalonia: a finalizer-thread release races the engine's
    // GCHandle swaps under an event fire-hose.
    private static void Dispatch(InputEvent ev)
    {
        try
        {
            Godot.Input.ParseInputEvent(ev);
        }
        finally
        {
            ev.Dispose();
        }
    }

    private void SendMotion(in QtInput input)
    {
        var position = new Vector2((float)input.X, (float)input.Y);
        var relative = _hasLastPosition ? position - _lastPosition : Vector2.Zero;
        _lastPosition = position;
        _hasLastPosition = true;
        _buttonMask = ToMask(input.Buttons);
        var motion = new InputEventMouseMotion
        {
            Position = position,
            GlobalPosition = position,
            Relative = relative,
            ScreenRelative = relative,
            ButtonMask = _buttonMask,
        };
        ApplyModifiers(motion, input.Modifiers);
        Dispatch(motion);
    }

    private void SendButton(in QtInput input)
    {
        var button = ToMouseButton(input.Button);
        if (button == MouseButton.None) return;
        var position = new Vector2((float)input.X, (float)input.Y);
        _lastPosition = position;
        _hasLastPosition = true;
        _buttonMask = ToMask(input.Buttons);
        var ev = new InputEventMouseButton
        {
            ButtonIndex = button,
            Pressed = input.Type != QtInputType.MouseRelease,
            // Qt reports the second press of a double click as its own event type.
            DoubleClick = input.Type == QtInputType.MouseDoubleClick,
            Factor = 1,
            Position = position,
            GlobalPosition = position,
            ButtonMask = _buttonMask,
        };
        ApplyModifiers(ev, input.Modifiers);
        Dispatch(ev);
    }

    private void SendWheel(in QtInput input)
    {
        var position = new Vector2((float)input.X, (float)input.Y);
        var mask = ToMask(input.Buttons);
        var modifiers = input.Modifiers;
        // Godot models wheel ticks as press/release pairs with the tick count in Factor.
        Send(input.DeltaY, MouseButton.WheelUp, MouseButton.WheelDown);
        Send(input.DeltaX, MouseButton.WheelLeft, MouseButton.WheelRight);
        return;

        void Send(double delta, MouseButton positive, MouseButton negative)
        {
            if (delta == 0) return;
            foreach (var pressed in (ReadOnlySpan<bool>)[true, false])
            {
                var ev = new InputEventMouseButton
                {
                    ButtonIndex = delta > 0 ? positive : negative,
                    Pressed = pressed,
                    Factor = (float)Math.Abs(delta),
                    Position = position,
                    GlobalPosition = position,
                    ButtonMask = mask,
                };
                ApplyModifiers(ev, modifiers);
                Dispatch(ev);
            }
        }
    }

    private void SendKey(in QtInput input)
    {
        var pressed = input.Type == QtInputType.KeyPress;
        var keycode = ToKeycode(input.Key, input.Modifiers);
        var id = input.ScanCode != 0 ? (input.ScanCode, GodotKey.None) : (0u, keycode);
        if (pressed) _heldKeys.TryAdd(id, keycode);
        else if (_heldKeys.Remove(id, out var held)) keycode = held;
        else return;
        var ev = new InputEventKey
        {
            Keycode = keycode,
            // shortcut: assumes a US layout for physical keys; map input.ScanCode per platform when non-US
            // layouts need position-based bindings (WASD on AZERTY).
            PhysicalKeycode = keycode,
            KeyLabel = keycode,
            Unicode = pressed ? input.Unicode : 0,
            Pressed = pressed,
            Echo = pressed && input.AutoRepeat != 0,
        };
        ApplyModifiers(ev, input.Modifiers);
        Dispatch(ev);
    }

    private static void ApplyModifiers(InputEventWithModifiers ev, int modifiers)
    {
        // Qt reports Command as Control on macOS and the Control key as Meta; Godot names them as labeled.
        var control = OperatingSystem.IsMacOS() ? MetaModifier : ControlModifier;
        var meta = OperatingSystem.IsMacOS() ? ControlModifier : MetaModifier;
        ev.ShiftPressed = (modifiers & ShiftModifier) != 0;
        ev.CtrlPressed = (modifiers & control) != 0;
        ev.AltPressed = (modifiers & AltModifier) != 0;
        ev.MetaPressed = (modifiers & meta) != 0;
    }

    internal static MouseButton ToMouseButton(int qtButton) => qtButton switch
    {
        0x01 => MouseButton.Left,
        0x02 => MouseButton.Right,
        0x04 => MouseButton.Middle,
        0x08 => MouseButton.Xbutton1,
        0x10 => MouseButton.Xbutton2,
        _ => MouseButton.None,
    };

    private static MouseButtonMask ToMask(MouseButton button) => button switch
    {
        MouseButton.Left => MouseButtonMask.Left,
        MouseButton.Right => MouseButtonMask.Right,
        MouseButton.Middle => MouseButtonMask.Middle,
        MouseButton.Xbutton1 => MouseButtonMask.MbXbutton1,
        MouseButton.Xbutton2 => MouseButtonMask.MbXbutton2,
        _ => 0,
    };

    internal static MouseButtonMask ToMask(int qtButtons)
    {
        MouseButtonMask mask = 0;
        for (var bit = 0x01; bit <= 0x10; bit <<= 1)
        {
            if ((qtButtons & bit) != 0) mask |= ToMask(ToMouseButton(bit));
        }
        return mask;
    }

    /// <summary>Maps a Qt::Key to Godot's logical keycode.</summary>
    internal static GodotKey ToKeycode(int qtKey, int modifiers)
    {
        if ((modifiers & KeypadModifier) != 0)
        {
            switch (qtKey)
            {
                case >= '0' and <= '9': return GodotKey.Kp0 + (qtKey - '0');
                case '*': return GodotKey.KpMultiply;
                case '+': return GodotKey.KpAdd;
                case '-': return GodotKey.KpSubtract;
                case '.' or ',': return GodotKey.KpPeriod;
                case '/': return GodotKey.KpDivide;
            }
        }
        var macOS = OperatingSystem.IsMacOS();
        return qtKey switch
        {
            // Below Qt's special range, keys are Unicode code points (letters upper case), as are Godot's.
            < 0x01000000 => (GodotKey)qtKey,
            >= 0x01000030 and <= 0x01000052 => GodotKey.F1 + (qtKey - 0x01000030),
            0x01000000 => GodotKey.Escape,
            0x01000001 => GodotKey.Tab,
            0x01000002 => GodotKey.Backtab,
            0x01000003 => GodotKey.Backspace,
            0x01000004 => GodotKey.Enter,
            0x01000005 => GodotKey.KpEnter,
            0x01000006 => GodotKey.Insert,
            0x01000007 => GodotKey.Delete,
            0x01000008 => GodotKey.Pause,
            0x01000009 => GodotKey.Print,
            0x0100000a => GodotKey.Sysreq,
            0x0100000b => GodotKey.Clear,
            0x01000010 => GodotKey.Home,
            0x01000011 => GodotKey.End,
            0x01000012 => GodotKey.Left,
            0x01000013 => GodotKey.Up,
            0x01000014 => GodotKey.Right,
            0x01000015 => GodotKey.Down,
            0x01000016 => GodotKey.Pageup,
            0x01000017 => GodotKey.Pagedown,
            0x01000020 => GodotKey.Shift,
            0x01000021 => macOS ? GodotKey.Meta : GodotKey.Ctrl,
            0x01000022 => macOS ? GodotKey.Ctrl : GodotKey.Meta,
            0x01000023 or 0x01001103 => GodotKey.Alt,
            0x01000024 => GodotKey.Capslock,
            0x01000025 => GodotKey.Numlock,
            0x01000026 => GodotKey.Scrolllock,
            0x01000053 or 0x01000054 => GodotKey.Meta,
            0x01000055 => GodotKey.Menu,
            0x01000056 or 0x01000057 => GodotKey.Hyper,
            0x01000058 => GodotKey.Help,
            0x01000061 => GodotKey.Back,
            0x01000062 => GodotKey.Forward,
            0x01000063 => GodotKey.Stop,
            0x01000064 => GodotKey.Refresh,
            0x01000070 => GodotKey.Volumedown,
            0x01000071 => GodotKey.Volumemute,
            0x01000072 => GodotKey.Volumeup,
            0x01000080 or 0x01000086 => GodotKey.Mediaplay,
            0x01000081 => GodotKey.Mediastop,
            0x01000082 => GodotKey.Mediaprevious,
            0x01000083 => GodotKey.Medianext,
            0x01000084 => GodotKey.Mediarecord,
            0x01000090 => GodotKey.Homepage,
            0x01000091 => GodotKey.Favorites,
            0x01000092 => GodotKey.Search,
            0x01000093 => GodotKey.Standby,
            0x01000094 => GodotKey.Openurl,
            0x010000a0 => GodotKey.Launchmail,
            0x010000a1 => GodotKey.Launchmedia,
            0x010000a2 => GodotKey.Launch0,
            0x010000a3 => GodotKey.Launch1,
            _ => GodotKey.Unknown,
        };
    }

    /// <summary>Maps the engine's cursor to a Qt::CursorShape.</summary>
    internal static int ToQtCursor(DisplayServer.CursorShape shape, Godot.Input.MouseModeEnum mode)
    {
        if (mode is Godot.Input.MouseModeEnum.Hidden or Godot.Input.MouseModeEnum.Captured
            or Godot.Input.MouseModeEnum.ConfinedHidden)
            return 10; // BlankCursor
        return shape switch
        {
            DisplayServer.CursorShape.Ibeam => 4,
            DisplayServer.CursorShape.PointingHand => 13,
            DisplayServer.CursorShape.Cross => 2,
            DisplayServer.CursorShape.Wait => 3,
            DisplayServer.CursorShape.Busy => 16,
            DisplayServer.CursorShape.Drag => 18,
            DisplayServer.CursorShape.CanDrop => 20,
            DisplayServer.CursorShape.Forbidden => 14,
            DisplayServer.CursorShape.Vsize => 5,
            DisplayServer.CursorShape.Hsize => 6,
            DisplayServer.CursorShape.Bdiagsize => 7,
            DisplayServer.CursorShape.Fdiagsize => 8,
            DisplayServer.CursorShape.Move => 9,
            DisplayServer.CursorShape.Vsplit => 11,
            DisplayServer.CursorShape.Hsplit => 12,
            DisplayServer.CursorShape.Help => 15,
            _ => 0,
        };
    }
}
