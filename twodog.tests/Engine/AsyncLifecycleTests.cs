using twodog.Testing;
using GodotEngine = Godot.Engine;

namespace twodog.tests.EngineTests;

[CollectionDefinition(DisableParallelization = true)]
public class AsyncLifecycleCollection : ICollectionFixture<AsyncLifecycleFixture>;

public class AsyncLifecycleFixture : FixtureBase, IAsyncLifetime
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    public AsyncLifecycleFixture() : base("--headless", "--max-fps", "30") { }

    public async ValueTask InitializeAsync()
    {
        await Task.Yield();
        Assert.Equal(_owner, Environment.CurrentManagedThreadId);
        Engine.Iteration();
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Yield();
        Assert.Equal(_owner, Environment.CurrentManagedThreadId);
        Dispose();
    }
}

[Collection<AsyncLifecycleCollection>]
public class AsyncLifecycleTests(AsyncLifecycleFixture godot) : IAsyncLifetime
{
    private readonly int _owner = Environment.CurrentManagedThreadId;

    public async ValueTask InitializeAsync()
    {
        await Task.Yield();
        Assert.Equal(_owner, Environment.CurrentManagedThreadId);
        godot.Engine.Iteration();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task AsyncLifecycleAndCustomArgumentsWorkForEachTest(int _)
    {
        await Task.Yield();
        Assert.Equal(_owner, Environment.CurrentManagedThreadId);
        Assert.Equal(30, GodotEngine.MaxFps);
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Yield();
        Assert.Equal(_owner, Environment.CurrentManagedThreadId);
        godot.Engine.Iteration();
    }
}
