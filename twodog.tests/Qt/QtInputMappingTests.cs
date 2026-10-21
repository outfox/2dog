using System.Runtime.InteropServices;
using Godot;
using GodotKey = Godot.Key;

namespace twodog.tests.QtTests;

// Pure mapping tables and the native ABI layout - no engine, no Qt; runs in parallel.
public class QtInputMappingTests
{
    private const int KeypadModifier = 0x20000000;

    [Fact]
    public void Letters_AndDigits_PassThroughAsCodePoints()
    {
        for (var key = 'A'; key <= 'Z'; key++)
            Assert.Equal(GodotKey.A + (key - 'A'), QtInputForwarder.ToKeycode(key, 0));
        for (var key = '0'; key <= '9'; key++)
            Assert.Equal(GodotKey.Key0 + (key - '0'), QtInputForwarder.ToKeycode(key, 0));
    }

    [Fact]
    public void KeypadModifier_SelectsKeypadKeys()
    {
        for (var key = '0'; key <= '9'; key++)
            Assert.Equal(GodotKey.Kp0 + (key - '0'), QtInputForwarder.ToKeycode(key, KeypadModifier));
        Assert.Equal(GodotKey.KpAdd, QtInputForwarder.ToKeycode('+', KeypadModifier));
        Assert.Equal(GodotKey.KpEnter, QtInputForwarder.ToKeycode(0x01000005, KeypadModifier));
        // Keys without a keypad variant keep their meaning, e.g. Home on a numpad with NumLock off.
        Assert.Equal(GodotKey.Home, QtInputForwarder.ToKeycode(0x01000010, KeypadModifier));
    }

    [Fact]
    public void FunctionKeys_MapContiguously()
    {
        for (var n = 0; n < 35; n++)
            Assert.Equal(GodotKey.F1 + n, QtInputForwarder.ToKeycode(0x01000030 + n, 0));
    }

    [Theory]
    [InlineData(0x01000000, GodotKey.Escape)]
    [InlineData(0x01000003, GodotKey.Backspace)]
    [InlineData(0x01000004, GodotKey.Enter)]
    [InlineData(0x01000012, GodotKey.Left)]
    [InlineData(0x01000015, GodotKey.Down)]
    [InlineData(0x01000020, GodotKey.Shift)]
    [InlineData(0x01000023, GodotKey.Alt)]
    [InlineData(0x01001103, GodotKey.Alt)]
    [InlineData(0x01000080, GodotKey.Mediaplay)]
    [InlineData(0x01ffffff, GodotKey.Unknown)]
    public void SpecialKeys_SpotChecks(int qtKey, GodotKey expected) =>
        Assert.Equal(expected, QtInputForwarder.ToKeycode(qtKey, 0));

    [Fact]
    public void ControlAndMeta_FollowTheKeyLabels()
    {
        // Qt reports Command as Control on macOS; Godot calls it Meta there.
        var control = OperatingSystem.IsMacOS() ? GodotKey.Meta : GodotKey.Ctrl;
        Assert.Equal(control, QtInputForwarder.ToKeycode(0x01000021, 0));
    }

    [Fact]
    public void ButtonMask_CombinesQtButtons()
    {
        Assert.Equal(MouseButtonMask.Left | MouseButtonMask.MbXbutton2, QtInputForwarder.ToMask(0x01 | 0x10));
        Assert.Equal(MouseButton.Xbutton1, QtInputForwarder.ToMouseButton(0x08));
        Assert.Equal(MouseButton.None, QtInputForwarder.ToMouseButton(0x20));
    }

    [Fact]
    public void EveryCursorShape_HasAnExplicitMapping()
    {
        // ArrowCursor (0) doubles as the fallback, so among known shapes only Arrow itself may map to it.
        for (var shape = DisplayServer.CursorShape.Arrow; shape < DisplayServer.CursorShape.Max; shape++)
        {
            var mapped = QtInputForwarder.ToQtCursor(shape, Godot.Input.MouseModeEnum.Visible);
            Assert.Equal(shape == DisplayServer.CursorShape.Arrow, mapped == 0);
        }
        Assert.Equal(10, QtInputForwarder.ToQtCursor(DisplayServer.CursorShape.Arrow, Godot.Input.MouseModeEnum.Captured));
    }

    [Fact]
    public void InputStruct_MatchesTheNativeLayout()
    {
        // TwoDogQtInput in twodog.qt/native/godotview.h.
        Assert.Equal(64, Marshal.SizeOf<QtInput>());
        Assert.Equal(16, (int)Marshal.OffsetOf<QtInput>(nameof(QtInput.X)));
        Assert.Equal(48, (int)Marshal.OffsetOf<QtInput>(nameof(QtInput.Key)));
        Assert.Equal(60, (int)Marshal.OffsetOf<QtInput>(nameof(QtInput.ScanCode)));
    }
}
