using System;
using System.Collections.Generic;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Godot;
using AvaloniaKey = Avalonia.Input.Key;
using MouseButton = Godot.MouseButton;

namespace twodog;

/// <summary>
/// Translates the control's Avalonia input events into Godot input events and injects them
/// through a sink (Input.ParseInputEvent by default - the hidden engine window never sees the
/// pointer). Coordinates are converted from DIPs to viewport pixels via the render scaling.
/// </summary>
internal sealed class GodotInputForwarder : IDisposable
{
    // Held keys are identified by their physical key so a layout change mid-hold still
    // releases them; keys with no physical identity (IME and synthetic keys report
    // PhysicalKey.None) fall back to the logical key so they never share one slot.
    private readonly record struct HeldKeyId(PhysicalKey Physical, AvaloniaKey Logical)
    {
        public static HeldKeyId From(KeyEventArgs e) => e.PhysicalKey == PhysicalKey.None
            ? new HeldKeyId(PhysicalKey.None, e.Key)
            : new HeldKeyId(e.PhysicalKey, AvaloniaKey.None);
    }

    private readonly GodotControl _control;
    private readonly Func<bool> _isReady;
    private readonly Action<InputEvent> _dispatch;
    private readonly Dictionary<HeldKeyId, AvaloniaKey> _heldKeys = [];
    // Kept in DIPs and converted per use: RenderScaling can change between events (monitor
    // move), and a baseline stored in old pixel space would skew the next relative delta.
    private Avalonia.Point _lastPos;
    private bool _hasLastPos;
    private MouseButtonMask _buttonMask;
    private DisplayServer.CursorShape? _lastShape;
    private Input.MouseModeEnum? _lastMouseMode;
    private IPointer? _pointer;
    private KeyModifiers _modifiers;
    private readonly WindowsMouseCapture? _mouseCapture;
    private readonly WindowBase? _window;
    private bool _captureLost;
    private Cursor? _hiddenCursor;

    public GodotInputForwarder(GodotControl control, GodotSession session)
        : this(control, () => session.IsStarted, Dispatch) { }

    internal GodotInputForwarder(GodotControl control, Func<bool> isReady, Action<InputEvent> dispatch)
    {
        _control = control;
        _isReady = isReady;
        _dispatch = dispatch;
        if (OperatingSystem.IsWindows()) _mouseCapture = new WindowsMouseCapture(control);
        _window = TopLevel.GetTopLevel(control) as WindowBase;
        if (_window is not null) _window.Deactivated += OnLostFocus;

        _control.PointerEntered += OnPointerEntered;
        _control.PointerMoved += OnPointerMoved;
        _control.PointerPressed += OnPointerPressed;
        _control.PointerReleased += OnPointerReleased;
        _control.PointerWheelChanged += OnPointerWheelChanged;
        _control.PointerCaptureLost += OnPointerCaptureLost;
        _control.KeyDown += OnKeyDown;
        _control.KeyUp += OnKeyUp;
        _control.LostFocus += OnLostFocus;
    }

    public void Dispose()
    {
        // Detaching mid-gesture must not leave keys or buttons wedged in the still-running session.
        ReleaseHeldKeys();
        ReleaseHeldButtons();
        ReleasePointer();
        if (OperatingSystem.IsWindows()) _mouseCapture?.Release();
        _control.Cursor = null;
        _hiddenCursor?.Dispose();

        _control.PointerEntered -= OnPointerEntered;
        _control.PointerMoved -= OnPointerMoved;
        _control.PointerPressed -= OnPointerPressed;
        _control.PointerReleased -= OnPointerReleased;
        _control.PointerWheelChanged -= OnPointerWheelChanged;
        _control.PointerCaptureLost -= OnPointerCaptureLost;
        _control.KeyDown -= OnKeyDown;
        _control.KeyUp -= OnKeyUp;
        _control.LostFocus -= OnLostFocus;
        if (_window is not null) _window.Deactivated -= OnLostFocus;
    }

    /// <summary>Reflects the engine's requested cursor shape onto the control, once per change.</summary>
    public void SyncCursor()
    {
        var mode = Godot.Input.MouseMode;
        if (OperatingSystem.IsWindows() && _mouseCapture is { } capture)
        {
            if (!_captureLost) capture.Update(mode);
            if (capture.IsCaptured) _pointer?.Capture(_control);
            else if (_buttonMask == 0) ReleasePointer();
            if (!capture.HasFocus) ReleaseHeldButtons();
        }
        var shape = DisplayServer.CursorGetShape();
        if (shape == _lastShape && mode == _lastMouseMode) return;
        _lastShape = shape;
        _lastMouseMode = mode;
        _hasLastPos = false;
        _control.Cursor = mode is Input.MouseModeEnum.Hidden or Input.MouseModeEnum.Captured or
            Input.MouseModeEnum.ConfinedHidden ? _hiddenCursor ??= new Cursor(StandardCursorType.None) : CursorMap.ToCursor(shape);
    }

    internal void PollCapturedMotion()
    {
        if (!Ready || !OperatingSystem.IsWindows() || _mouseCapture is null) return;
        var relative = _mouseCapture.PollMotion();
        if (relative == Vector2.Zero) return;
        SendMotion(ToViewport(new Avalonia.Point(_control.Bounds.Width / 2, _control.Bounds.Height / 2)), relative, _modifiers);
    }

    private void ReleasePointer()
    {
        var pointer = _pointer;
        _pointer = null;
        if (pointer?.Captured == _control) pointer.Capture(null);
    }

    private double Scaling => _control.RenderScaling;

    internal Vector2 ToViewport(Avalonia.Point p)
    {
        var scale = Scaling;
        return GodotMath.CreateVector2(p.X * scale, p.Y * scale);
    }

    private static void ApplyModifiers(InputEventWithModifiers e, KeyModifiers modifiers)
    {
        e.ShiftPressed = modifiers.HasFlag(KeyModifiers.Shift);
        e.CtrlPressed = modifiers.HasFlag(KeyModifiers.Control);
        e.AltPressed = modifiers.HasFlag(KeyModifiers.Alt);
        e.MetaPressed = modifiers.HasFlag(KeyModifiers.Meta);
    }

    private bool Ready => _isReady();

    // Dispose synthesized events right after injection: Godot's buffered Ref keeps the native
    // object alive, while waiting for the finalizer would free the managed GCHandle on the
    // finalizer thread, racing the engine's weak/strong handle swaps under event fire-hose.
    // Internal: the input-injection regression tests exercise this exact pattern.
    internal static void Dispatch(InputEvent ev)
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

    // (Re)entry establishes the relative-motion baseline so the first move never reports a jump.
    private void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        _lastPos = e.GetPosition(_control);
        _hasLastPos = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!Ready) return;
        _pointer = e.Pointer;
        _modifiers = e.KeyModifiers;
        if (OperatingSystem.IsWindows() && _mouseCapture?.IsCaptured == true) return;
        var dip = e.GetPosition(_control);
        var pos = ToViewport(dip);
        SendMotion(pos, _hasLastPos ? pos - ToViewport(_lastPos) : Vector2.Zero, e.KeyModifiers);
        _lastPos = dip;
        _hasLastPos = true;
    }

    private void SendMotion(Vector2 pos, Vector2 relative, KeyModifiers modifiers)
    {
        var motion = new InputEventMouseMotion
        {
            Position = pos,
            GlobalPosition = pos,
            Relative = relative,
            ScreenRelative = relative,
            ButtonMask = _buttonMask,
        };
        ApplyModifiers(motion, modifiers);
        _dispatch(motion);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        SendButtonTransition(e, pressed: true, doubleClick: e.ClickCount % 2 == 0);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        SendButtonTransition(e, pressed: false, doubleClick: false);
    }

    private void SendButtonTransition(PointerEventArgs e, bool pressed, bool doubleClick)
    {
        if (!Ready) return;
        var button = KeyMap.ToMouseButton(e.GetCurrentPoint(_control).Properties.PointerUpdateKind);
        if (button == MouseButton.None) return;
        _pointer = e.Pointer;
        _modifiers = e.KeyModifiers;
        if (pressed)
        {
            _captureLost = false;
            e.Pointer.Capture(_control);
        }

        if (pressed) _buttonMask |= KeyMap.ToMask(button);
        else _buttonMask &= ~KeyMap.ToMask(button);

        SendMouseButton(button, pressed, ToViewport(e.GetPosition(_control)), e.KeyModifiers,
            doubleClick: doubleClick, factor: 1);
        if (_buttonMask == 0 && !(OperatingSystem.IsWindows() && _mouseCapture?.IsCaptured == true)) ReleasePointer();
        e.Handled = true;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!Ready) return;
        var pos = ToViewport(e.GetPosition(_control));

        // Godot models wheel ticks as button press/release pairs with the tick size in Factor.
        Send(e.Delta.Y, MouseButton.WheelUp, MouseButton.WheelDown);
        Send(e.Delta.X, MouseButton.WheelLeft, MouseButton.WheelRight);
        e.Handled = true;
        return;

        void Send(double delta, MouseButton positive, MouseButton negative)
        {
            if (delta == 0) return;
            var button = delta > 0 ? positive : negative;
            var factor = (float)Math.Abs(delta);
            SendMouseButton(button, pressed: true, pos, e.KeyModifiers, doubleClick: false, factor);
            SendMouseButton(button, pressed: false, pos, e.KeyModifiers, doubleClick: false, factor);
        }
    }

    private void SendMouseButton(MouseButton button, bool pressed, Vector2 pos, KeyModifiers modifiers,
        bool doubleClick, float factor)
    {
        var ev = new InputEventMouseButton
        {
            ButtonIndex = button,
            Pressed = pressed,
            DoubleClick = doubleClick,
            Factor = factor,
            Position = pos,
            GlobalPosition = pos,
            ButtonMask = _buttonMask,
        };
        ApplyModifiers(ev, modifiers);
        _dispatch(ev);
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _pointer)) return;
        _captureLost = true;
        if (OperatingSystem.IsWindows()) _mouseCapture?.Release();
        _hasLastPos = false;
        ReleaseHeldButtons();
    }

    // A drag left the control's world (popup opened, capture stolen, control detached): release
    // every pressed button so the engine's state cannot wedge.
    private void ReleaseHeldButtons()
    {
        if (!Ready || _buttonMask == 0) return;
        foreach (var button in (ReadOnlySpan<MouseButton>)
                 [MouseButton.Left, MouseButton.Right, MouseButton.Middle, MouseButton.Xbutton1, MouseButton.Xbutton2])
        {
            if ((_buttonMask & KeyMap.ToMask(button)) == 0) continue;
            _buttonMask &= ~KeyMap.ToMask(button);
            SendMouseButton(button, pressed: false, ToViewport(_lastPos), KeyModifiers.None, doubleClick: false, factor: 1);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e) => SendKey(e, pressed: true);

    private void OnKeyUp(object? sender, KeyEventArgs e) => SendKey(e, pressed: false);

    private void SendKey(KeyEventArgs e, bool pressed)
    {
        if (!Ready) return;
        _modifiers = e.KeyModifiers;
        // Avalonia repeats arrive as repeated KeyDown events; Godot wants them flagged as echo.
        var id = HeldKeyId.From(e);
        AvaloniaKey key;
        bool echo;
        if (pressed)
        {
            echo = !_heldKeys.TryAdd(id, e.Key);
            key = echo ? _heldKeys[id] : e.Key;
        }
        else
        {
            echo = false;
            if (!_heldKeys.Remove(id, out key)) return;
        }

        var keycode = KeyMap.ToKeycode(key, e.PhysicalKey);
        var ev = new InputEventKey
        {
            Keycode = keycode,
            PhysicalKeycode = KeyMap.ToPhysicalKeycode(e.PhysicalKey),
            KeyLabel = keycode,
            Unicode = e.KeySymbol is { Length: > 0 } s && Rune.TryGetRuneAt(s, 0, out var rune) ? rune.Value : 0,
            Pressed = pressed,
            Echo = pressed && echo,
        };
        ApplyModifiers(ev, e.KeyModifiers);
        _dispatch(ev);
        e.Handled = true;
    }

    // The eventual KeyUp goes elsewhere once focus moves on: release everything still held.
    private void ReleaseHeldKeys()
    {
        if (Ready)
        {
            foreach (var (id, key) in _heldKeys)
            {
                var keycode = KeyMap.ToKeycode(key, id.Physical);
                _dispatch(new InputEventKey
                {
                    Keycode = keycode,
                    PhysicalKeycode = KeyMap.ToPhysicalKeycode(id.Physical),
                    KeyLabel = keycode,
                    Pressed = false,
                });
            }
        }
        _heldKeys.Clear();
    }

    // Deliberately no GodotInstance.FocusIn/FocusOut here: those are for cross-process
    // embedding and corrupt display-server state in-process; the engine's own window
    // keeps app focus.
    private void OnLostFocus(object? sender, EventArgs e)
    {
        ReleaseHeldKeys();
        ReleaseHeldButtons();
        ReleasePointer();
        if (OperatingSystem.IsWindows()) _mouseCapture?.Release();
        _hasLastPos = false;
    }
}
