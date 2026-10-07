using System.Runtime.InteropServices;
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
        // AppKit aborts a windowed engine anywhere but on the process main thread.
        if (OperatingSystem.IsMacOS()) Assert.Equal(1, pthread_main_np());

        // Iteration throws unless called on the thread that started the engine.
        godot.Engine.Iteration();
    }

    [Fact]
    public async Task ContinuationsReturnToTheEngineThread()
    {
        var thread = Environment.CurrentManagedThreadId;

        await Task.Yield();
        Assert.Equal(thread, Environment.CurrentManagedThreadId);
        await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Equal(thread, Environment.CurrentManagedThreadId);

        godot.Engine.Iteration();
    }

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern int pthread_main_np();
}

public class PlainTestThreadTests
{
    [Fact]
    public void TestsWithoutAFixture_KeepXunitsThreads()
    {
        Assert.NotEqual("2dog test thread", Thread.CurrentThread.Name);
    }
}
