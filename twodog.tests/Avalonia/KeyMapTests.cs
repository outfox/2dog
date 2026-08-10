using Avalonia.Input;
using twodog;
using AvaloniaKey = Avalonia.Input.Key;
using GodotKey = Godot.Key;
using MouseButton = Godot.MouseButton;
using MouseButtonMask = Godot.MouseButtonMask;

namespace twodog.tests.AvaloniaTests;

// Pure mapping tables - no engine, no Avalonia platform; runs in parallel.
public class KeyMapTests
{
    [Fact]
    public void Letters_MapContiguously()
    {
        for (var key = AvaloniaKey.A; key <= AvaloniaKey.Z; key++)
            Assert.Equal(GodotKey.A + (key - AvaloniaKey.A), KeyMap.ToKeycode(key, PhysicalKey.None));
    }

    [Fact]
    public void Digits_MapContiguously()
    {
        for (var key = AvaloniaKey.D0; key <= AvaloniaKey.D9; key++)
            Assert.Equal(GodotKey.Key0 + (key - AvaloniaKey.D0), KeyMap.ToKeycode(key, PhysicalKey.None));
    }

    [Fact]
    public void NumPad_MapsToKeypad()
    {
        for (var key = AvaloniaKey.NumPad0; key <= AvaloniaKey.NumPad9; key++)
            Assert.Equal(GodotKey.Kp0 + (key - AvaloniaKey.NumPad0), KeyMap.ToKeycode(key, PhysicalKey.None));
    }

    [Fact]
    public void FunctionKeys_MapContiguously()
    {
        for (var key = AvaloniaKey.F1; key <= AvaloniaKey.F24; key++)
            Assert.Equal(GodotKey.F1 + (key - AvaloniaKey.F1), KeyMap.ToKeycode(key, PhysicalKey.None));
    }

    [Theory]
    [InlineData(AvaloniaKey.Return, GodotKey.Enter)]
    [InlineData(AvaloniaKey.Escape, GodotKey.Escape)]
    [InlineData(AvaloniaKey.Back, GodotKey.Backspace)]
    [InlineData(AvaloniaKey.Space, GodotKey.Space)]
    [InlineData(AvaloniaKey.LeftShift, GodotKey.Shift)]
    [InlineData(AvaloniaKey.RightCtrl, GodotKey.Ctrl)]
    [InlineData(AvaloniaKey.LWin, GodotKey.Meta)]
    [InlineData(AvaloniaKey.OemTilde, GodotKey.Quoteleft)]
    [InlineData(AvaloniaKey.OemPlus, GodotKey.Equal)]
    [InlineData(AvaloniaKey.OemQuestion, GodotKey.Slash)]
    [InlineData(AvaloniaKey.PageUp, GodotKey.Pageup)]
    [InlineData(AvaloniaKey.PrintScreen, GodotKey.Print)]
    [InlineData(AvaloniaKey.Print, GodotKey.Print)]
    [InlineData(AvaloniaKey.Separator, GodotKey.KpPeriod)]
    [InlineData(AvaloniaKey.Cancel, GodotKey.Unknown)]
    public void Keycode_SpotChecks(AvaloniaKey avalonia, GodotKey expected) =>
        Assert.Equal(expected, KeyMap.ToKeycode(avalonia, PhysicalKey.None));

    [Fact]
    public void KanaMode_OnlyJisKanaKey_ReadsAsKana()
    {
        // Avalonia aliases HangulMode to KanaMode; the physical key tells them apart.
        Assert.Equal(GodotKey.JisKana, KeyMap.ToKeycode(AvaloniaKey.KanaMode, PhysicalKey.KanaMode));
        Assert.Equal(GodotKey.Unknown, KeyMap.ToKeycode(AvaloniaKey.HangulMode, PhysicalKey.Lang1));
    }

    [Theory]
    [InlineData(PhysicalKey.A, GodotKey.A)]
    [InlineData(PhysicalKey.Z, GodotKey.Z)]
    [InlineData(PhysicalKey.Digit0, GodotKey.Key0)]
    [InlineData(PhysicalKey.Backquote, GodotKey.Quoteleft)]
    [InlineData(PhysicalKey.Quote, GodotKey.Apostrophe)]
    [InlineData(PhysicalKey.NumPadEnter, GodotKey.KpEnter)]
    [InlineData(PhysicalKey.ArrowLeft, GodotKey.Left)]
    [InlineData(PhysicalKey.MetaRight, GodotKey.Meta)]
    [InlineData(PhysicalKey.IntlYen, GodotKey.Yen)]
    [InlineData(PhysicalKey.None, GodotKey.Unknown)]
    public void PhysicalKeycode_SpotChecks(PhysicalKey physical, GodotKey expected) =>
        Assert.Equal(expected, KeyMap.ToPhysicalKeycode(physical));

    [Theory]
    [InlineData(PointerUpdateKind.LeftButtonPressed, MouseButton.Left)]
    [InlineData(PointerUpdateKind.LeftButtonReleased, MouseButton.Left)]
    [InlineData(PointerUpdateKind.RightButtonPressed, MouseButton.Right)]
    [InlineData(PointerUpdateKind.MiddleButtonReleased, MouseButton.Middle)]
    [InlineData(PointerUpdateKind.XButton1Pressed, MouseButton.Xbutton1)]
    [InlineData(PointerUpdateKind.XButton2Released, MouseButton.Xbutton2)]
    [InlineData(PointerUpdateKind.Other, MouseButton.None)]
    public void MouseButton_SpotChecks(PointerUpdateKind kind, MouseButton expected) =>
        Assert.Equal(expected, KeyMap.ToMouseButton(kind));

    [Theory]
    [InlineData(MouseButton.Left, MouseButtonMask.Left)]
    [InlineData(MouseButton.Right, MouseButtonMask.Right)]
    [InlineData(MouseButton.Middle, MouseButtonMask.Middle)]
    [InlineData(MouseButton.Xbutton1, MouseButtonMask.MbXbutton1)]
    [InlineData(MouseButton.Xbutton2, MouseButtonMask.MbXbutton2)]
    [InlineData(MouseButton.WheelUp, (MouseButtonMask)0)]
    public void Mask_SpotChecks(MouseButton button, MouseButtonMask expected) =>
        Assert.Equal(expected, KeyMap.ToMask(button));
}
