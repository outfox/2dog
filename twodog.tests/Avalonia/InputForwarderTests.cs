using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Godot;
using twodog.Testing.Xunit;
using MouseButton = Godot.MouseButton;
using Button = Avalonia.Controls.Button;
using Orientation = Avalonia.Layout.Orientation;

namespace twodog.tests.AvaloniaTests;

[Collection<HeadlessCollection>]
public class InputForwarderTests
{
    [Fact]
    public void RightDrag_PreservesMotionOutsideControl_AndReleasesOnCaptureOrFocusLoss() =>
        GodotControlLifecycleTests.Dispatch(() =>
        {
            var buttons = new List<(MouseButton Button, bool Pressed)>();
            var motions = new List<(Vector2 Relative, Vector2 ScreenRelative, MouseButtonMask Buttons)>();
            var control = new GodotControl { Width = 100, Height = 100 };
            var other = new Button { Width = 100, Height = 100, Focusable = true };
            var window = new Avalonia.Controls.Window
            {
                Width = 200, Height = 100,
                Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { control, other } },
            };
            IPointer? pointer = null;
            control.PointerPressed += (_, e) => pointer = e.Pointer;
            using var forwarder = new GodotInputForwarder(control, () => true, ev =>
            {
                using (ev)
                {
                    if (ev is InputEventMouseButton button) buttons.Add((button.ButtonIndex, button.Pressed));
                    if (ev is InputEventMouseMotion motion) motions.Add((motion.Relative, motion.ScreenRelative, motion.ButtonMask));
                }
            });
            try
            {
                window.Show();
                window.MouseMove(new Point(10, 10));
                window.MouseDown(new Point(10, 10), Avalonia.Input.MouseButton.Right);
                Assert.NotNull(pointer);
                Assert.Same(control, pointer.Captured);
                window.MouseMove(new Point(150, 20), RawInputModifiers.RightMouseButton);
                Assert.Equal((new Vector2(140, 10), new Vector2(140, 10), MouseButtonMask.Right), motions[^1]);
                window.MouseUp(new Point(150, 20), Avalonia.Input.MouseButton.Right);
                Assert.Null(pointer.Captured);
                Assert.Equal([(MouseButton.Right, true), (MouseButton.Right, false)], buttons);

                buttons.Clear();
                window.MouseDown(new Point(10, 10), Avalonia.Input.MouseButton.Right);
                pointer.Capture(other);
                Assert.Equal([(MouseButton.Right, true), (MouseButton.Right, false)], buttons);
                pointer.Capture(null);
                window.MouseUp(new Point(10, 10), Avalonia.Input.MouseButton.Right);

                buttons.Clear();
                window.MouseDown(new Point(10, 10), Avalonia.Input.MouseButton.Right);
                other.Focus();
                Assert.Null(pointer.Captured);
                Assert.Equal([(MouseButton.Right, true), (MouseButton.Right, false)], buttons);
                window.MouseUp(new Point(10, 10), Avalonia.Input.MouseButton.Right);

            }
            finally
            {
                window.Close();
            }
        });
}
