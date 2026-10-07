using Godot;
using twodog.Testing;
using twodog.Testing.Xunit;

namespace twodog.tests.Windowed;

/// <summary>
/// The stock windowed fixture, started the way consumers' rendering suites start it: on the 2dog test thread.
/// On macOS, AppKit aborts the process when a window server connection is set up off the main thread, so a
/// regression here shows as a crashed test run rather than a failed assertion.
/// </summary>
[Collection<RenderingCollection>]
public class WindowedFixtureTests(Fixture godot)
{
    [Fact]
    public void StartsWithAWindowAndRendersFrames()
    {
        Assert.NotEqual("headless", DisplayServer.GetName());
        Assert.True(godot.Tree.Root.Size is { X: > 0, Y: > 0 });

        for (var frame = 0; frame < 10; frame++)
            godot.Engine.Iteration();

        Assert.True(Godot.Engine.GetFramesDrawn() > 0);
    }
}
