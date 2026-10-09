using System.Collections.Concurrent;

namespace twodog.Repl;

// Compilation and terminal I/O run in the background. Only this queue enters the live engine.
internal sealed class EngineDispatcher : IDisposable
{
    private readonly ConcurrentQueue<Action> pending = new();
    private readonly CancellationTokenSource stopped = new();
    private readonly CancellationToken stoppedToken;
    private int disposed;

    public EngineDispatcher() => stoppedToken = stopped.Token;

    public Task<T> InvokeAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        if (stoppedToken.IsCancellationRequested) return Task.FromCanceled<T>(stoppedToken);
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = stoppedToken.Register(() => completion.TrySetCanceled(stoppedToken));
        pending.Enqueue(async () =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (completion.Task.IsCompleted) return;
                completion.TrySetResult(await action());
            }
            catch (OperationCanceledException e) { completion.TrySetCanceled(e.CancellationToken); }
            catch (Exception e) { completion.TrySetException(e); }
            finally { registration.Dispose(); }
        });
        return completion.Task;
    }

    public void Pump()
    {
        // Bound each drain so producers cannot starve Godot's next frame.
        for (var i = 0; i < 16 && pending.TryDequeue(out var action); i++) action();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stopped.Cancel();
        while (pending.TryDequeue(out _)) { }
        stopped.Dispose();
    }
}
