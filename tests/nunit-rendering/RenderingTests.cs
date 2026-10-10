using System.Threading;
using Godot;
using NUnit.Framework;
using twodog.Testing.NUnit;
using GodotEngine = Godot.Engine;

namespace twodog.nunit.tests.Rendering;

public class RenderingTests : GodotRenderingTestFixture
{
    [Test]
    public async Task RendersFramesAndPreservesTheOwnerThread()
    {
        var owner = System.Environment.CurrentManagedThreadId;
        if (OperatingSystem.IsWindows()) Assert.That(Thread.CurrentThread.GetApartmentState(), Is.EqualTo(ApartmentState.STA));
        Assert.That(DisplayServer.GetName(), Is.Not.EqualTo("headless"));
        Assert.That(EngineFixture.Tree.Root.Size.X, Is.GreaterThan(0));
        var before = GodotEngine.GetFramesDrawn();
        await Task.Yield();
        await EngineFixture.WaitUntilAsync(() => GodotEngine.GetFramesDrawn() > before);
        Assert.That(System.Environment.CurrentManagedThreadId, Is.EqualTo(owner));
    }

}

public class RenderingRestartTests : GodotRenderingTestFixture
{
    [Test]
    public void ASecondRenderingFixtureCanStart() => Assert.That(EngineFixture.Tree.Root.Visible, Is.True);
}
