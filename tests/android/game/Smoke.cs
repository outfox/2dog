using Godot;

public partial class Smoke : Node
{
    [Signal]
    public delegate void ProbeEventHandler(int value);

    private int _frames;
    private int _signalValue;
    private bool _rendered;

    public override void _Ready()
    {
        if (Name != "Smoke" || GetParent() != GetTree().Root)
            throw new System.InvalidOperationException("Smoke script is not bound to the scene root.");
        Probe += value => _signalValue = value;
        EmitSignal(SignalName.Probe, 42);
        var probe = new Node { Name = "RuntimeProbe" };
        AddChild(probe);
        if (GetNode<Node>("RuntimeProbe") != probe)
            throw new System.InvalidOperationException("Native node lookup did not preserve the managed instance.");
        probe.QueueFree();
        if (!HasNode("RuntimeProbe") || !probe.IsQueuedForDeletion())
            throw new System.InvalidOperationException("QueueFree did not defer node disposal until the end of the frame.");
        AddChild(new ColorRect { Color = new Color(0.1f, 0.8f, 0.3f), Size = new Vector2(256, 256) });
        RenderingServer.FramePostDraw += OnFrameDrawn;
        GD.Print("2DOG_ANDROID_CSHARP_SMOKE_STARTED");
    }

    private void OnFrameDrawn() => _rendered = true;

    public override void _Process(double delta)
    {
        if (++_frames != 32) return;
        if (_signalValue != 42 || !_rendered || HasNode("RuntimeProbe"))
            throw new System.InvalidOperationException("Android signal, rendering, or deferred native disposal failed.");
        using var image = GetViewport().GetTexture().GetImage();
        var pixel = image.GetPixel(128, 128);
        if (pixel.G < 0.7f || pixel.R > 0.2f || pixel.B > 0.4f)
            throw new System.InvalidOperationException($"Android smoke rectangle was not rendered: {pixel}.");
        RenderingServer.FramePostDraw -= OnFrameDrawn;
        GD.Print("2DOG_ANDROID_CSHARP_SMOKE_PASSED");
    }

    public override void _ExitTree() => RenderingServer.FramePostDraw -= OnFrameDrawn;
}
