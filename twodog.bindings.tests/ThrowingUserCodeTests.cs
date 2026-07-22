using Godot;
using Godot.NativeInterop;

namespace twodog.bindings.tests;

public class ThrowingVirtualsNode : Node
{
    public int ProcessCalls;
    public bool ReadyCalled;

    public override void _Ready()
    {
        ReadyCalled = true;
        throw new InvalidOperationException("boom in _Ready");
    }

    public override void _Process(double delta)
    {
        ProcessCalls++;
        throw new InvalidOperationException("boom in _Process");
    }
}

public class ThrowingCtorNode : Node
{
    public ThrowingCtorNode() => throw new InvalidOperationException("boom in ctor");
}

/// <summary>
/// Ordinary user bugs (throwing overrides/ctors) must be logged at the
/// [UnmanagedCallersOnly] boundary, never unwind into the engine's native
/// frame (which turns them into a process-killing FailFast).
/// </summary>
[Collection(nameof(GodotBindingsCollection))]
public class ThrowingUserCodeTests(GodotBindingsFixture godot)
{
    /// <summary>Captures Console.Error so provoked boundary logs stay out of clean runs.</summary>
    private sealed class StderrCapture : IDisposable
    {
        private readonly TextWriter _prev = Console.Error;
        private readonly StringWriter _captured = new();

        public StderrCapture() => Console.SetError(_captured);

        public string Text => _captured.ToString();

        public void Dispose() => Console.SetError(_prev);
    }

    [Fact]
    public void ThrowingReadyAndProcess_AreLogged_EngineSurvives()
    {
        ClassRegistry.Register<ThrowingVirtualsNode>();
        var root = ((SceneTree)Godot.Engine.GetMainLoop()!).Root!;
        var node = new ThrowingVirtualsNode();
        try
        {
            using var capture = new StderrCapture();
            root.AddChild(node); // _Ready throws inside CallVirtualWithData
            Assert.True(node.ReadyCalled);

            godot.PumpFrames(3); // _Process throws every frame
            Assert.True(node.ProcessCalls >= 3, $"expected >=3 _Process calls, got {node.ProcessCalls}");

            Assert.Contains("unhandled exception in virtual override", capture.Text);
            Assert.Contains("boom in _Ready", capture.Text);
            Assert.Contains("boom in _Process", capture.Text);

            root.RemoveChild(node);
            Assert.True(node.IsValid); // engine and object both survived
        }
        finally
        {
            node.Free();
        }
    }

    [Fact]
    public void ThrowingCtor_EngineInstantiationFails_EngineSurvives()
    {
        ClassRegistry.Register<ThrowingCtorNode>();
        using var capture = new StderrCapture();
        using var mute = new EngineOutputMute(); // engine reports the null instance

        // classdb_construct_object3 -> our create_instance -> throwing ctor.
        var ptr = InstanceBindings.ConstructRaw("ThrowingCtorNode");
        Assert.Equal(0, ptr);
        Assert.Contains("unhandled exception constructing", capture.Text);
        Assert.Contains("boom in ctor", capture.Text);

        // The engine is still fully functional afterwards.
        var probe = new Node();
        Assert.True(probe.IsValid);
        probe.Free();
    }
}
