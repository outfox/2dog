using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Godot;

namespace twodog;

// The hidden Godot window must never grab the pointer. Avalonia owns capture, while
// this supplies unbounded relative motion and confinement in the visible control.
[SupportedOSPlatform("windows")]
internal sealed class WindowsMouseCapture(GodotControl control)
{
    private bool _confined;
    private PixelPoint _restorePosition;
    private PixelPoint _center;

    public bool IsCaptured { get; private set; }

    public bool HasFocus => control.IsFocused &&
        TopLevel.GetTopLevel(control)?.TryGetPlatformHandle()?.Handle == GetForegroundWindow();

    public void Update(Input.MouseModeEnum mode)
    {
        var confine = mode is Input.MouseModeEnum.Captured or Input.MouseModeEnum.Confined or
            Input.MouseModeEnum.ConfinedHidden;
        if (!HasFocus || !confine || control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
        {
            Release();
            return;
        }

        var start = control.PointToScreen(default);
        var end = control.PointToScreen(new Point(control.Bounds.Width, control.Bounds.Height));
        var rect = new Rect { Left = start.X, Top = start.Y, Right = end.X, Bottom = end.Y };
        if (!ClipCursor(ref rect))
        {
            Release();
            return;
        }
        _confined = true;

        if (mode == Input.MouseModeEnum.Captured && !IsCaptured && GetCursorPos(out var position))
        {
            _restorePosition = new PixelPoint(position.X, position.Y);
            _center = Center;
            IsCaptured = SetCursorPos(_center.X, _center.Y);
        }
        else if (mode != Input.MouseModeEnum.Captured)
            RestorePosition();
    }

    private PixelPoint Center => control.PointToScreen(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2));

    // shortcut: frame-sampled cursor deltas include Windows acceleration; use raw input for raw aiming.
    // Sample once per engine frame, then recenter. Synthetic warp events never enter the
    // forwarded stream, and motion continues when a drag reaches a screen edge.
    public Vector2 PollMotion()
    {
        if (!IsCaptured) return Vector2.Zero;
        if (!HasFocus)
        {
            Release();
            return Vector2.Zero;
        }
        if (!GetCursorPos(out var position)) return Vector2.Zero;
        var relative = new Vector2(position.X - _center.X, position.Y - _center.Y);
        _center = Center;
        SetCursorPos(_center.X, _center.Y);
        return relative;
    }

    public void Release()
    {
        if (_confined) ClipCursor(0);
        _confined = false;
        RestorePosition();
    }

    private void RestorePosition()
    {
        if (!IsCaptured) return;
        IsCaptured = false;
        SetCursorPos(_restorePosition.X, _restorePosition.Y);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointI { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32")]
    private static extern bool GetCursorPos(out PointI point);
    [DllImport("user32")]
    private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32")]
    private static extern bool ClipCursor(ref Rect rect);
    [DllImport("user32")]
    private static extern bool ClipCursor(nint rect);
}
