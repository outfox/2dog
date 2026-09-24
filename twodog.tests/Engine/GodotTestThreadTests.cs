using twodog.Testing;
using twodog.Testing.Xunit;

namespace twodog.tests.EngineTests;

[Collection<HeadlessCollection>]
public class GodotTestThreadTests(HeadlessFixture godot)
{
    [Fact]
    public void RunsOnTheEngineThread()
    {
        Assert.Equal("2dog test thread", Thread.CurrentThread.Name);
        if (OperatingSystem.IsWindows()) Assert.Equal(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());

        // Iteration throws unless called on the thread that started the engine.
        godot.Engine.Iteration();
    }

    [Fact]
    public async Task ContinuationsReturnToTheEngineThread()
    {
        var thread = Environment.CurrentManagedThreadId;

        await Task.Yield();
        Assert.Equal(thread, Environment.CurrentManagedThreadId);
        await Task.Delay(10);
        Assert.Equal(thread, Environment.CurrentManagedThreadId);

        godot.Engine.Iteration();
    }
}

public class PlainTestThreadTests
{
    [Fact]
    public void TestsWithoutAFixture_KeepXunitsThreads()
    {
        Assert.NotEqual("2dog test thread", Thread.CurrentThread.Name);
    }
}
