// Demonstrates the 2dog.gdextension host package: the same host-code shape as
// 2dog.engine's README example, running on the GDExtension bindings with a
// non-mono libgodot. This is what "swap the package reference" buys.

using Godot;
using Godot.NativeInterop;
using twodog;
using Engine = twodog.Engine; // same alias 2dog.engine users need (Godot.Engine exists there too)

var repoRoot = AppContext.BaseDirectory;
while (!File.Exists(Path.Combine(repoRoot, "2dog.sln")))
    repoRoot = Path.GetDirectoryName(repoRoot)!;
var projectDir = Path.Combine(repoRoot, "spikes", "gdext.spike", "project");

var failures = 0;

void Check(bool ok, string what)
{
    Console.WriteLine($"[host-demo] {(ok ? "ok " : "FAIL")} {what}");
    if (!ok) failures++;
}

ClassRegistry.Register<Spinner>();

using (var engine = new Engine("gdext.host.demo", projectDir, "--headless"))
{
    using var godot = engine.Start();
    Check(godot.IsStarted, "engine started through the 2dog.gdextension host API");

    var tree = engine.Tree;
    Check(tree.Root is not null, "Engine.Tree resolves the SceneTree");

    var spinner = new Spinner();
    spinner.Name = "spinner";
    tree.Root!.AddChild(spinner);

    while (spinner.Frames < 60 && !godot.Iteration())
    {
    }
    Check(spinner.Frames >= 60, $"_Process pumped through GodotInstance.Iteration ({spinner.Frames} frames)");
    Check(spinner.Rotation != 0f, $"typed property writes took effect (rotation = {spinner.Rotation:F3})");

    GD.Print((Variant)"[host-demo] engine-side print through GD");

    // The async experience end-to-end: await an engine signal, then a Task
    // continuation that must marshal back through the host loop's pump.
    var hostThread = System.Environment.CurrentManagedThreadId;
    var resumedOn = 0;
    var story = AsyncStory();
    var guard = 0;
    while (!story.IsCompleted && guard++ < 600 && !godot.Iteration())
    {
    }
    Check(story.IsCompleted, "async story completed through the host loop");
    Check(resumedOn == hostThread, $"Task.Delay resumed on the host thread (thread {resumedOn})");

    async Task AsyncStory()
    {
        var timer = tree.CreateTimer(0.03, true, false, false)!;
        await tree.ToSignal(timer, "timeout");   // engine-driven resume
        timer.Dispose();
        await Task.Delay(15);                    // timer-thread completion, pumped back
        resumedOn = System.Environment.CurrentManagedThreadId;
    }

    tree.Root.RemoveChild(spinner);
    spinner.Free();
}

Check(true, "clean engine dispose");
Console.WriteLine(failures == 0
    ? "[host-demo] PASS - 2dog.gdextension host layer works end-to-end."
    : $"[host-demo] FAIL - {failures} checks failed.");
return failures == 0 ? 0 : 1;

/// <summary>A user node exactly as a migrated 2dog.engine user would write it.</summary>
public partial class Spinner : Node2D
{
    public int Frames;

    public override void _Process(double delta)
    {
        Frames++;
        Rotation += (float)delta;
    }
}
