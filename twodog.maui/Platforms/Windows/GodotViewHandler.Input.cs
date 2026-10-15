using Godot;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Grid = Microsoft.UI.Xaml.Controls.Grid;

namespace twodog.Maui;

internal sealed partial class GodotViewHandler
{
    private Vector2 _lastPointer;
    private MouseButtonMask _buttons;
    private readonly HashSet<Key> _pressedKeys = [];
    private readonly Dictionary<uint, Vector2> _touches = [];

    private void ConnectInput(Grid panel)
    {
        panel.PointerPressed += PointerPressed;
        panel.PointerReleased += PointerReleased;
        panel.PointerMoved += PointerMoved;
        panel.PointerWheelChanged += PointerWheel;
        panel.PointerCanceled += PointerCanceled;
        panel.PointerCaptureLost += PointerCanceled;
        panel.KeyDown += KeyDown;
        panel.KeyUp += KeyUp;
        panel.LostFocus += LostFocus;
    }

    private void DisconnectInput(Grid panel)
    {
        panel.PointerPressed -= PointerPressed;
        panel.PointerReleased -= PointerReleased;
        panel.PointerMoved -= PointerMoved;
        panel.PointerWheelChanged -= PointerWheel;
        panel.PointerCanceled -= PointerCanceled;
        panel.PointerCaptureLost -= PointerCanceled;
        panel.KeyDown -= KeyDown;
        panel.KeyUp -= KeyUp;
        panel.LostFocus -= LostFocus;
    }

    private Vector2 Position(PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(PlatformView).Position;
        var scale = PlatformView.XamlRoot.RasterizationScale;
        return GodotMath.CreateVector2(point.X * scale, point.Y * scale);
    }

    private void PointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (_engine is null) return;
        PlatformView.Focus(FocusState.Pointer);
        PlatformView.CapturePointer(args.Pointer);
        PointerButton(args, true);
    }

    private void PointerReleased(object sender, PointerRoutedEventArgs args)
    {
        PointerButton(args, false);
        PlatformView.ReleasePointerCapture(args.Pointer);
    }

    private void PointerButton(PointerRoutedEventArgs args, bool pressed)
    {
        if (_engine is null) return;
        var point = args.GetCurrentPoint(PlatformView);
        if (args.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch)
        {
            if (pressed) _touches[args.Pointer.PointerId] = Position(args);
            else _touches.Remove(args.Pointer.PointerId);
            using var touch = new InputEventScreenTouch
            {
                Index = (int)args.Pointer.PointerId, Position = Position(args), Pressed = pressed,
            };
            Input.ParseInputEvent(touch);
        }
        else
        {
            var button = point.Properties.PointerUpdateKind switch
            {
                Microsoft.UI.Input.PointerUpdateKind.RightButtonPressed or Microsoft.UI.Input.PointerUpdateKind.RightButtonReleased => MouseButton.Right,
                Microsoft.UI.Input.PointerUpdateKind.MiddleButtonPressed or Microsoft.UI.Input.PointerUpdateKind.MiddleButtonReleased => MouseButton.Middle,
                _ => MouseButton.Left,
            };
            var mask = (MouseButtonMask)(1 << ((int)button - 1));
            _buttons = pressed ? _buttons | mask : _buttons & ~mask;
            using var input = new InputEventMouseButton
            {
                ButtonIndex = button, Pressed = pressed, Position = Position(args),
                GlobalPosition = Position(args), ButtonMask = _buttons,
            };
            Input.ParseInputEvent(input);
        }
        args.Handled = true;
    }

    private void PointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (_engine is null) return;
        var position = Position(args);
        if (args.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch)
        {
            if (!args.GetCurrentPoint(PlatformView).IsInContact) return;
            var previous = _touches.GetValueOrDefault(args.Pointer.PointerId, position);
            _touches[args.Pointer.PointerId] = position;
            using var drag = new InputEventScreenDrag
            {
                Index = (int)args.Pointer.PointerId, Position = position, Relative = position - previous,
            };
            Input.ParseInputEvent(drag);
        }
        else
        {
            using var motion = new InputEventMouseMotion
            {
                Position = position, GlobalPosition = position, Relative = position - _lastPointer, ButtonMask = _buttons,
            };
            Input.ParseInputEvent(motion);
        }
        _lastPointer = position;
        args.Handled = true;
    }

    private void PointerWheel(object sender, PointerRoutedEventArgs args)
    {
        if (_engine is null) return;
        var properties = args.GetCurrentPoint(PlatformView).Properties;
        var button = properties.IsHorizontalMouseWheel
            ? properties.MouseWheelDelta > 0 ? MouseButton.WheelRight : MouseButton.WheelLeft
            : properties.MouseWheelDelta > 0 ? MouseButton.WheelUp : MouseButton.WheelDown;
        using var input = new InputEventMouseButton
        {
            ButtonIndex = button, Pressed = true, Factor = Math.Abs(properties.MouseWheelDelta) / 120f,
            Position = Position(args), GlobalPosition = Position(args), ButtonMask = _buttons,
        };
        Input.ParseInputEvent(input);
        input.Pressed = false;
        Input.ParseInputEvent(input);
        args.Handled = true;
    }

    private void KeyDown(object sender, KeyRoutedEventArgs args) => KeyEvent(args, true);
    private void KeyUp(object sender, KeyRoutedEventArgs args) => KeyEvent(args, false);
    private void KeyEvent(KeyRoutedEventArgs args, bool pressed)
    {
        if (_engine is null || MapKey(args.Key) is not { } key) return;
        var echo = pressed && !_pressedKeys.Add(key);
        if (!pressed) _pressedKeys.Remove(key);
        using var input = new InputEventKey
        {
            Keycode = key, PhysicalKeycode = key, Pressed = pressed, Echo = echo,
            ShiftPressed = _pressedKeys.Contains(Key.Shift), CtrlPressed = _pressedKeys.Contains(Key.Ctrl),
            AltPressed = _pressedKeys.Contains(Key.Alt), MetaPressed = _pressedKeys.Contains(Key.Meta),
        };
        Input.ParseInputEvent(input);
        args.Handled = true;
    }

    private static Key? MapKey(VirtualKey key) => key switch
    {
        >= VirtualKey.A and <= VirtualKey.Z or >= VirtualKey.Number0 and <= VirtualKey.Number9 => (Key)(int)key,
        >= VirtualKey.F1 and <= VirtualKey.F24 => Key.F1 + ((int)key - (int)VirtualKey.F1),
        VirtualKey.Escape => Key.Escape, VirtualKey.Tab => Key.Tab, VirtualKey.Space => Key.Space,
        VirtualKey.Enter => Key.Enter, VirtualKey.Back => Key.Backspace, VirtualKey.Delete => Key.Delete,
        VirtualKey.Insert => Key.Insert, VirtualKey.Home => Key.Home, VirtualKey.End => Key.End,
        VirtualKey.PageUp => Key.Pageup, VirtualKey.PageDown => Key.Pagedown,
        VirtualKey.Left => Key.Left, VirtualKey.Right => Key.Right, VirtualKey.Up => Key.Up, VirtualKey.Down => Key.Down,
        VirtualKey.Shift => Key.Shift, VirtualKey.Control => Key.Ctrl, VirtualKey.Menu => Key.Alt,
        VirtualKey.LeftWindows or VirtualKey.RightWindows => Key.Meta, _ => null,
    };

    private void PointerCanceled(object sender, PointerRoutedEventArgs args) => ReleaseInput();
    private void LostFocus(object sender, RoutedEventArgs args) => ReleaseInput();
    private void ReleaseInput()
    {
        if (_engine is not null)
        {
            foreach (var key in _pressedKeys)
            {
                using var input = new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false };
                Input.ParseInputEvent(input);
            }
            foreach (var button in new[] { MouseButton.Left, MouseButton.Right, MouseButton.Middle })
            {
                if (((int)_buttons & (1 << ((int)button - 1))) == 0) continue;
                using var input = new InputEventMouseButton { ButtonIndex = button, Pressed = false, Position = _lastPointer };
                Input.ParseInputEvent(input);
            }
            foreach (var (id, position) in _touches)
            {
                using var input = new InputEventScreenTouch { Index = (int)id, Position = position, Pressed = false };
                Input.ParseInputEvent(input);
            }
        }
        _buttons = 0;
        _pressedKeys.Clear();
        _touches.Clear();
    }
}
