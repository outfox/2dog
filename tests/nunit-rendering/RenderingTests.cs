using System.Threading;
using Godot;
using NUnit.Framework;
using twodog.Testing;
using twodog.Testing.NUnit;
using GodotEngine = Godot.Engine;

namespace twodog.nunit.tests.Rendering;

/// <summary>Accepts only the Vulkan loader's warning about a machine without registered layers.</summary>
public abstract class SoftwareRenderingTestFixture : GodotRenderingTestFixture
{
    // CI's GPU-less Windows VMs register no Vulkan layers; the loader warns about that when Godot creates its instance.
    private const string NoLayersWarning =
        "windows_read_data_files_in_registry: Registry lookup failed to get layer manifest files.";

    private GodotError[] _startupErrors = [];

    protected override FixtureBase CreateFixture()
    {
        var fixture = base.CreateFixture();
        _startupErrors = fixture.Errors.Drain()
            .Where(e => e.Type != GodotErrorType.Warning || !e.Text.Contains(NoLayersWarning, StringComparison.Ordinal))
            .ToArray();
        return fixture;
    }

    [SetUp]
    public void FailOnOtherStartupErrors()
    {
        var errors = _startupErrors;
        _startupErrors = [];
        if (errors.Length > 0)
            throw new AssertionException($"Godot reported {errors.Length} error(s) during startup:\n{string.Join("\n", errors)}");
    }
}

public class RenderingTests : SoftwareRenderingTestFixture
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

public class RenderingRestartTests : SoftwareRenderingTestFixture
{
    [Test]
    public void ASecondRenderingFixtureCanStart() => Assert.That(EngineFixture.Tree.Root.Visible, Is.True);
}
