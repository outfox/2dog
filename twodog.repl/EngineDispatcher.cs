namespace twodog.Repl;

// Compilation and terminal I/O run in the background. Only this queue enters the live engine.
internal sealed class EngineDispatcher : IDisposable
{
    private readonly object gate = new();
    private readonly Queue<Func<Task>> pending = new();
    private readonly List<Task> active = new();
    private readonly CancellationTokenSource stopped = new();
    private readonly CancellationToken stoppedToken;
    private bool disposed;

    public EngineDispatcher() => stoppedToken = stopped.Token;

    public Task<T> InvokeAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
        => InvokeAsync(_ => action(), cancellationToken);

    public Task<T> InvokeAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (disposed) return Task.FromCanceled<T>(new CancellationToken(canceled: true));
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppedToken, cancellationToken);
            var token = cancellation.Token;
            // Cancel queued work immediately, but never report a running action as finished
            // before its continuations and finally blocks have actually returned.
            var state = 0;
            var registration = token.Register(() =>
            {
                if (Interlocked.CompareExchange(ref state, 2, 0) == 0) completion.TrySetCanceled(token);
            });
            pending.Enqueue(async () =>
            {
                try
                {
                    if (Interlocked.CompareExchange(ref state, 1, 0) != 0) return;
                    completion.TrySetResult(await action(token));
                }
                catch (OperationCanceledException e) { completion.TrySetCanceled(e.CancellationToken); }
                catch (Exception e) { completion.TrySetException(e); }
                finally
                {
                    registration.Dispose();
                    cancellation.Dispose();
                }
            });
            return completion.Task;
        }
    }

    private bool TryDequeue(out Func<Task>? action)
    {
        lock (gate) return pending.TryDequeue(out action);
    }

    public void Pump()
    {
        // Bound each drain so producers cannot starve Godot's next frame.
        for (var i = 0; i < 16 && TryDequeue(out var action); i++) active.Add(action!());
        active.RemoveAll(task => task.IsCompleted);
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
        }
        try { stopped.Cancel(); }
        finally
        {
            // Cancelled queue entries still own their registrations and linked token sources.
            while (TryDequeue(out var action)) active.Add(action!());
            // Godot has stopped iterating, so drain owner-thread await continuations explicitly.
            // Keep the engine alive until cooperative actions finish, including asynchronous cleanup.
            while (active.Any(task => !task.IsCompleted))
            {
                (SynchronizationContext.Current as Godot.GodotSynchronizationContext)?.ExecutePendingContinuations();
                Thread.Sleep(1);
            }
            active.Clear();
            stopped.Dispose();
        }
    }
}
