using Godot;
using NUnit.Framework;
using twodog.Testing.NUnit;

namespace twodog.nunit.tests;

public class InputTests : GodotTestFixture
{
    private SubViewport _viewport = null!;

    [SetUp]
    public void CreateViewport()
    {
        _viewport = new SubViewport { Size = new Vector2I(400, 300) };
        Godot.Tree.Root.AddChild(_viewport);
    }

    [TearDown]
    public void FreeViewport() => _viewport.Free();

    private Button AddButton()
    {
        var button = new Button { Position = new Vector2(30, 30), Size = new Vector2(100, 50) };
        _viewport.AddChild(button);
        Godot.Engine.Iteration();
        return button;
    }

    [Test]
    public void ClickUsesCanvasTransformAndReleasesTheButton()
    {
        var button = AddButton();
        var layer = new CanvasLayer { Transform = new Transform2D(0.1f, new Vector2(80, 40)) };
        _viewport.AddChild(layer);
        button.Reparent(layer, keepGlobalTransform: false);
        button.Rotation = 0.2f;
        button.Scale = new Vector2(1.5f, 1.5f);
        using var pressed = GodotAssert.ExpectSignal(button, BaseButton.SignalName.Pressed);
        Godot.Input.Click(button);
        pressed.AssertEmitted();
        Assert.That(button.IsPressed(), Is.False);
    }

    [Test]
    public void RootViewportClickWorksInsideItsVisibleBounds()
    {
        Godot.Engine.Iteration();
        var button = new Button { Position = new Vector2(5, 5), Size = new Vector2(20, 20) };
        try
        {
            Godot.Tree.Root.AddChild(button);
            Godot.Engine.Iteration();
            using var pressed = GodotAssert.ExpectSignal(button, BaseButton.SignalName.Pressed);
            Godot.Input.Click(button);
            pressed.AssertEmitted();
        }
        finally { button.Free(); }
    }

    [Test]
    public void ForegroundControlBlocksTheAimedClick()
    {
        var button = AddButton();
        var overlay = new Control { Position = button.Position, Size = button.Size };
        _viewport.AddChild(overlay);
        using var pressed = GodotAssert.ExpectSignal(button, BaseButton.SignalName.Pressed);
        Godot.Input.Click(button);
        pressed.AssertEmitted(0);
        overlay.MouseFilter = Control.MouseFilterEnum.Ignore;
        Godot.Input.Click(button);
        pressed.AssertEmitted();
    }

    [Test]
    public void DisabledHiddenAndIgnoredControlsDoNotReceiveClicks()
    {
        var button = AddButton();
        using var pressed = GodotAssert.ExpectSignal(button, BaseButton.SignalName.Pressed);
        button.Disabled = true;
        Godot.Input.Click(button);
        button.Disabled = false;
        button.Hide();
        Godot.Input.Click(button);
        button.Show();
        button.MouseFilter = Control.MouseFilterEnum.Ignore;
        Godot.Input.Click(button);
        pressed.AssertEmitted(0);
    }

    [Test]
    public void ViewportSuppressionAndEarlyConsumptionBlockGuiInput()
    {
        var button = AddButton();
        using var pressed = GodotAssert.ExpectSignal(button, BaseButton.SignalName.Pressed);
        _viewport.GuiDisableInput = true;
        Godot.Input.Click(button);
        pressed.AssertEmitted(0);
        _viewport.GuiDisableInput = false;
        using var script = new GDScript
        {
            SourceCode = "extends Node\nfunc _input(_event):\n    get_viewport().set_input_as_handled()\n"
        };
        Assert.That(script.Reload(), Is.EqualTo(Error.Ok));
        var blocker = new Node();
        blocker.SetScript(script);
        _viewport.AddChild(blocker);
        Godot.Input.Click(button);
        pressed.AssertEmitted(0);
    }

    [Test]
    public void ClippedControlDoesNotReceiveTheAimedClick()
    {
        var clip = new Control { Size = new Vector2(20, 20), ClipContents = true };
        _viewport.AddChild(clip);
        var button = new Button { Position = new Vector2(50, 50), Size = new Vector2(100, 50) };
        clip.AddChild(button);
        Godot.Engine.Iteration();
        using var pressed = GodotAssert.ExpectSignal(button, BaseButton.SignalName.Pressed);
        Godot.Input.Click(button);
        pressed.AssertEmitted(0);
    }

    [Test]
    public void KeyboardInputUsesExistingFocusWithoutRetargeting()
    {
        var first = AddButton();
        var second = AddButton();
        second.Position = new Vector2(200, 30);
        first.GrabFocus();
        using var firstPressed = GodotAssert.ExpectSignal(first, BaseButton.SignalName.Pressed);
        using var secondPressed = GodotAssert.ExpectSignal(second, BaseButton.SignalName.Pressed);
        using var down = new InputEventKey { Keycode = Key.Space, Pressed = true };
        using var up = new InputEventKey { Keycode = Key.Space, Pressed = false };
        Godot.Input.PushToViewport(down, _viewport);
        Godot.Input.PushToViewport(up, _viewport);
        firstPressed.AssertEmitted();
        secondPressed.AssertEmitted(0);
    }

    [Test]
    public void ServerInputUpdatesPollingAndDeliversOneEvent()
    {
        var count = 0;
        void OnInput(InputEvent inputEvent)
        {
            if (inputEvent is InputEventKey { Keycode: Key.F13, Pressed: true }) count++;
        }
        Godot.Tree.Root.WindowInput += OnInput;
        try
        {
            using var down = new InputEventKey { Keycode = Key.F13, Pressed = true };
            Godot.Input.Send(down);
            Assert.That(global::Godot.Input.IsKeyPressed(Key.F13), Is.True);
            Assert.That(count, Is.EqualTo(1));
        }
        finally
        {
            using var up = new InputEventKey { Keycode = Key.F13, Pressed = false };
            Godot.Input.Send(up);
            Godot.Tree.Root.WindowInput -= OnInput;
        }
        Assert.That(global::Godot.Input.IsKeyPressed(Key.F13), Is.False);
    }

    [Test]
    public void InvalidTargetsAndWrongThreadFailBeforeEnteringGodot()
    {
        var detached = new Button();
        Assert.Throws<ArgumentException>(() => Godot.Input.Click(detached));
        detached.Free();
        Assert.Throws<ArgumentException>(() => Godot.Input.Click(detached));
        var error = Task.Run(() =>
        {
            try { Godot.Input.Click(Vector2.Zero); return null; }
            catch (Exception e) { return e; }
        }).GetAwaiter().GetResult();
        Assert.That(error, Is.TypeOf<InvalidOperationException>());
    }
}
