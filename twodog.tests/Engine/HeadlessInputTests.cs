#if REAL_T_IS_DOUBLE
using Real = System.Double;
#else
using Real = System.Single;
#endif

using Godot;
using twodog.Testing;
using twodog.Testing.Xunit;

namespace twodog.tests.EngineTests;

// Characterize modern headless behavior separately from the explicitly sized SubViewport tests.
// See godotengine/godot#73557 (dispatch) and #92806 (headless dispatch implementation).
[Collection<HeadlessCollection>]
public class HeadlessInputTests(HeadlessFixture godot, ITestOutputHelper output)
{
    [Fact]
    public void HeadlessRootStartsAtMinimumSizeDespiteProjectViewportSettings()
    {
        var root = godot.Tree.Root;
        output.WriteLine($"Godot {Godot.Engine.GetVersionInfo()["string"]}; display={DisplayServer.GetName()}; " +
            $"native={DisplayServer.WindowGetSize()}; root={root.Size}; visible={root.GetVisibleRect()}; " +
            $"content={root.ContentScaleSize}; mode={root.ContentScaleMode}; aspect={root.ContentScaleAspect}");
        Assert.Equal("headless", DisplayServer.GetName());
        Assert.Equal(Vector2I.Zero, DisplayServer.WindowGetSize());
        Assert.Equal(root.MinSize, root.Size);
        Assert.NotEqual(ProjectViewportSize(), root.Size);
    }

    [Theory]
    [InlineData(Window.ContentScaleModeEnum.Disabled)]
    [InlineData(Window.ContentScaleModeEnum.CanvasItems)]
    [InlineData(Window.ContentScaleModeEnum.Viewport)]
    public void RootResizeRestoresProjectLayoutAndAllowsGuiClicks(Window.ContentScaleModeEnum mode)
    {
        var root = godot.Tree.Root;
        using var restore = new RestoreWindow(root);
        root.ContentScaleMode = mode;
        root.ContentScaleAspect = Window.ContentScaleAspectEnum.Ignore;
        root.ContentScaleFactor = 1;
        root.ContentScaleSize = ProjectViewportSize();
        root.Size = ProjectViewportSize();
        var panel = new Control();
        try
        {
            root.AddChild(panel);
            panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            var button = new Button { Position = new Vector2(200, 100), Size = new Vector2(100, 50) };
            panel.AddChild(button);
            godot.Engine.Iteration();
            output.WriteLine($"mode={mode}; root={root.Size}; visible={root.GetVisibleRect()}; panel={panel.Size}");
            Assert.Equal((Vector2)ProjectViewportSize(), root.GetVisibleRect().Size);
            Assert.Equal(root.GetVisibleRect().Size, panel.Size);
            using var pressed = GodotAssert.ExpectSignal(button, BaseButton.SignalName.Pressed);
            godot.Input.Click(button);
            pressed.AssertEmitted();
            Assert.False(button.IsPressed());
            Assert.Equal(Vector2I.Zero, DisplayServer.WindowGetSize());
        }
        finally { panel.Free(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RootResizeMakesOutOfBoundsGuiClickableWithAndWithoutAncestorClipping(bool clipContents)
    {
        var root = godot.Tree.Root;
        using var restore = new RestoreWindow(root);
        root.ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        root.ContentScaleFactor = 1;
        root.Size = root.MinSize;
        var panel = new Control { ClipContents = clipContents };
        try
        {
            root.AddChild(panel);
            panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            var button = new Button { Position = new Vector2(200, 100), Size = new Vector2(100, 50) };
            panel.AddChild(button);
            godot.Engine.Iteration();
            Assert.False(root.GetVisibleRect().HasPoint(button.GetGlobalRect().GetCenter()));
            using var pressed = GodotAssert.ExpectSignal(button, BaseButton.SignalName.Pressed);
            godot.Input.Click(button);
            pressed.AssertEmitted(0);

            root.Size = ProjectViewportSize();
            godot.Engine.Iteration();
            Assert.True(root.GetVisibleRect().HasPoint(button.GetGlobalRect().GetCenter()));
            godot.Input.Click(button);
            pressed.AssertEmitted();
        }
        finally { panel.Free(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeyboardReachesFocusedGuiThroughBothDeliveryMethods(bool throughInputServer)
    {
        var button = new Button { Position = new Vector2(5, 5), Size = new Vector2(40, 40) };
        try
        {
            godot.Tree.Root.AddChild(button);
            godot.Engine.Iteration();
            button.GrabFocus();
            using var pressed = GodotAssert.ExpectSignal(button, BaseButton.SignalName.Pressed);
            using var down = new InputEventKey { Keycode = Key.Space, Pressed = true };
            using var up = new InputEventKey { Keycode = Key.Space };
            try
            {
                Deliver(down);
                Assert.Equal(throughInputServer, Input.IsKeyPressed(Key.Space));
            }
            finally { Deliver(up); }
            pressed.AssertEmitted();
            Assert.False(Input.IsKeyPressed(Key.Space));
        }
        finally { button.Free(); }

        void Deliver(InputEvent inputEvent)
        {
            if (throughInputServer) godot.Input.Send(inputEvent);
            else godot.Input.PushToViewport(inputEvent);
        }
    }

    [Fact]
    public void HeadlessDisplayServerFlushesBufferedEventsIntoNodeInputStages()
    {
        using var script = new GDScript
        {
            SourceCode = "extends Node\nvar received = 0\nvar unhandled = 0\n" +
                "func _input(event):\n    if event is InputEventKey and event.keycode == KEY_F13 and event.pressed:\n        received += 1\n" +
                "func _unhandled_input(event):\n    if event is InputEventKey and event.keycode == KEY_F13 and event.pressed:\n        unhandled += 1\n"
        };
        Assert.Equal(Error.Ok, script.Reload());
        var probe = new Node();
        probe.SetScript(script);
        var accumulated = Input.UseAccumulatedInput;
        try
        {
            godot.Tree.Root.AddChild(probe);
            Input.UseAccumulatedInput = true;
            using var down = new InputEventKey { Keycode = Key.F13, Pressed = true };
            Input.ParseInputEvent(down);
            Assert.Equal(0, probe.Get("received").AsInt32());
            DisplayServer.ProcessEvents();
            Assert.True(Input.IsKeyPressed(Key.F13));
            Assert.Equal(1, probe.Get("received").AsInt32());
            Assert.Equal(1, probe.Get("unhandled").AsInt32());
        }
        finally
        {
            using var up = new InputEventKey { Keycode = Key.F13 };
            godot.Input.Send(up);
            Input.UseAccumulatedInput = accumulated;
            probe.Free();
        }
    }

    [Theory]
    [InlineData(Window.ContentScaleModeEnum.Disabled)]
    [InlineData(Window.ContentScaleModeEnum.CanvasItems)]
    [InlineData(Window.ContentScaleModeEnum.Viewport)]
    public void ServerMouseEventsReachWindowAndGuiWithoutWarpingTheCursor(Window.ContentScaleModeEnum mode)
    {
        var root = godot.Tree.Root;
        using var restore = new RestoreWindow(root);
        root.ContentScaleMode = mode;
        root.ContentScaleAspect = Window.ContentScaleAspectEnum.Ignore;
        root.ContentScaleFactor = 1;
        root.ContentScaleSize = new Vector2I(400, 300);
        root.Size = new Vector2I(800, 600);
        var originalMousePosition = root.GetFinalTransform() * root.GetMousePosition();
        var button = new Button { Position = new Vector2(200, 100), Size = new Vector2(100, 50) };
        var windowEvents = 0;
        void OnWindowInput(InputEvent inputEvent)
        {
            if (inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) windowEvents++;
        }
        root.WindowInput += OnWindowInput;
        try
        {
            root.AddChild(button);
            godot.Engine.Iteration();
            using var pressed = GodotAssert.ExpectSignal(button, BaseButton.SignalName.Pressed);
            using var entered = GodotAssert.ExpectSignal(button, Control.SignalName.MouseEntered);
            // Send takes window/device coordinates; Click takes logical viewport coordinates.
            var position = root.GetFinalTransform() * button.GetGlobalRect().GetCenter();
            using var motion = new InputEventMouseMotion { Position = position, GlobalPosition = position };
            using var down = new InputEventMouseButton
            {
                Position = position, GlobalPosition = position, ButtonIndex = MouseButton.Left,
                ButtonMask = MouseButtonMask.Left, Pressed = true
            };
            using var up = new InputEventMouseButton
            {
                Position = position, GlobalPosition = position, ButtonIndex = MouseButton.Left
            };
            godot.Input.Send(motion);
            try
            {
                godot.Input.Send(down);
                Assert.True(Input.IsMouseButtonPressed(MouseButton.Left));
            }
            finally { godot.Input.Send(up); }
            Assert.Equal(1, windowEvents);
            entered.AssertEmitted();
            pressed.AssertEmitted();
            Assert.False(Input.IsMouseButtonPressed(MouseButton.Left));
            godot.Input.Click(button);
            pressed.AssertEmitted(2);
            Assert.Equal(1, windowEvents);
            Assert.False(Input.IsMouseButtonPressed(MouseButton.Left));
        }
        finally
        {
            root.WindowInput -= OnWindowInput;
            button.Free();
            using var reset = new InputEventMouseMotion { Position = originalMousePosition, GlobalPosition = originalMousePosition };
            godot.Input.Send(reset);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SpoofedMouseMotionReachesNodeAndGuiWithPositionAndRelativeMovement(bool throughInputServer)
    {
        var root = godot.Tree.Root;
        using var restore = new RestoreWindow(root);
        root.ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        root.ContentScaleFactor = 1;
        root.Size = new Vector2I(800, 600);
        var originalMousePosition = root.GetMousePosition();
        using var script = new GDScript
        {
            SourceCode = "extends Node\nvar received = 0\nvar position = Vector2.ZERO\nvar relative = Vector2.ZERO\n" +
                "func _input(event):\n    if event is InputEventMouseMotion:\n        received += 1\n" +
                "        position = event.position\n        relative = event.relative\n"
        };
        Assert.Equal(Error.Ok, script.Reload());
        var probe = new Node();
        probe.SetScript(script);
        var button = new Button { Position = new Vector2(200, 100), Size = new Vector2(100, 50) };
        var guiMotion = 0;
        var guiPosition = Vector2.Zero;
        var guiRelative = Vector2.Zero;
        button.GuiInput += inputEvent =>
        {
            if (inputEvent is not InputEventMouseMotion motion) return;
            guiMotion++;
            guiPosition = motion.Position;
            guiRelative = motion.Relative;
        };
        try
        {
            root.AddChild(probe);
            root.AddChild(button);
            godot.Engine.Iteration();
            using var entered = GodotAssert.ExpectSignal(button, Control.SignalName.MouseEntered);
            using var exited = GodotAssert.ExpectSignal(button, Control.SignalName.MouseExited);
            var position = button.GetGlobalRect().GetCenter();
            var relative = new Vector2(13, 37);
            using var motion = new InputEventMouseMotion
            {
                Position = position, GlobalPosition = position, Relative = relative, ScreenRelative = relative
            };
            Deliver(motion);
            Assert.Equal(1, probe.Get("received").AsInt32());
            Assert.Equal(position, probe.Get("position").AsVector2());
            Assert.Equal(relative, probe.Get("relative").AsVector2());
            Assert.Equal(1, guiMotion);
            Assert.Equal(button.Size / 2, guiPosition);
            Assert.Equal(relative, guiRelative);
            // The root falls back to the Input singleton in headless mode. Direct viewport
            // injection updates event/hover state, but does not change that polling position.
            Assert.Equal(throughInputServer ? position : originalMousePosition, root.GetMousePosition());
            entered.AssertEmitted();
            Assert.Same(button, root.GuiGetHoveredControl());
            // A synthesized event updates Godot, not the headless driver's nonexistent OS cursor.
            Assert.Equal(Vector2I.Zero, DisplayServer.MouseGetPosition());
            using var leave = new InputEventMouseMotion { Position = new Vector2(10, 10), GlobalPosition = new Vector2(10, 10) };
            Deliver(leave);
            exited.AssertEmitted();
            Assert.Null(root.GuiGetHoveredControl());
        }
        finally
        {
            button.Free();
            probe.Free();
            using var reset = new InputEventMouseMotion { Position = originalMousePosition, GlobalPosition = originalMousePosition };
            godot.Input.Send(reset);
        }

        void Deliver(InputEvent inputEvent)
        {
            if (throughInputServer) godot.Input.Send(inputEvent);
            else godot.Input.PushToViewport(inputEvent);
        }
    }

    private static Vector2I ProjectViewportSize() => new(
        ProjectSettings.GetSettingWithOverride("display/window/size/viewport_width").AsInt32(),
        ProjectSettings.GetSettingWithOverride("display/window/size/viewport_height").AsInt32());

    private sealed class RestoreWindow(Window window) : IDisposable
    {
        private readonly Vector2I _size = window.Size;
        private readonly Vector2I _contentSize = window.ContentScaleSize;
        private readonly Window.ContentScaleModeEnum _mode = window.ContentScaleMode;
        private readonly Window.ContentScaleAspectEnum _aspect = window.ContentScaleAspect;
        private readonly Real _factor = window.ContentScaleFactor;

        public void Dispose()
        {
            window.ContentScaleMode = _mode;
            window.ContentScaleAspect = _aspect;
            window.ContentScaleFactor = _factor;
            window.ContentScaleSize = _contentSize;
            window.Size = _size;
        }
    }
}
