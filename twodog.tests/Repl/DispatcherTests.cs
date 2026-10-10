using twodog.Repl;

namespace twodog.tests.ReplTests;

public class DispatcherTests
{
    [Fact]
    public void DisposalCancelsRunningActionsAndDrainsOwnerThreadAsyncCleanup()
    {
        var previous = SynchronizationContext.Current;
        using var context = new Godot.GodotSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            using var dispatcher = new EngineDispatcher();
            var owner = Environment.CurrentManagedThreadId;
            var cleanupThread = 0;
            var started = false;
            var task = dispatcher.InvokeAsync(async token =>
            {
                started = true;
                try { await Task.Delay(Timeout.Infinite, token); }
                finally
                {
                    await Task.Yield();
                    cleanupThread = Environment.CurrentManagedThreadId;
                }
                return 1;
            }, TestContext.Current.CancellationToken);
            dispatcher.Pump();
            Assert.True(started);
            Assert.False(task.IsCompleted);
            dispatcher.Dispose();
            Assert.True(task.IsCanceled);
            Assert.Equal(owner, cleanupThread);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    [Fact]
    public void DisposalStillDrainsCleanupWhenAUserCancellationCallbackThrows()
    {
        var previous = SynchronizationContext.Current;
        using var context = new Godot.GodotSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            using var dispatcher = new EngineDispatcher();
            var cleanupFinished = false;
            var task = dispatcher.InvokeAsync(async token =>
            {
                using var registration = token.Register(() => throw new InvalidOperationException("callback failure"));
                try { await Task.Delay(Timeout.Infinite, token); }
                finally
                {
                    await Task.Yield();
                    cleanupFinished = true;
                }
                return 1;
            }, TestContext.Current.CancellationToken);
            dispatcher.Pump();
            var error = Assert.Throws<AggregateException>(() => dispatcher.Dispose());
            Assert.Contains(error.Flatten().InnerExceptions, e => e.Message == "callback failure");
            Assert.True(task.IsCanceled);
            Assert.True(cleanupFinished);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    [Fact]
    public void CancellationDoesNotCompleteARunningActionBeforeItsFinallyBlockReturns()
    {
        var previous = SynchronizationContext.Current;
        using var context = new Godot.GodotSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            using var dispatcher = new EngineDispatcher();
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var cleanupFinished = false;
            var task = dispatcher.InvokeAsync(async token =>
            {
                try { await Task.Delay(Timeout.Infinite, token); }
                finally
                {
                    await Task.Yield();
                    cleanupFinished = true;
                }
                return 1;
            }, stop.Token);
            dispatcher.Pump();
            stop.Cancel();
            Assert.False(task.IsCompleted);
            Assert.False(cleanupFinished);
            dispatcher.Dispose();
            Assert.True(task.IsCanceled);
            Assert.True(cleanupFinished);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
}
