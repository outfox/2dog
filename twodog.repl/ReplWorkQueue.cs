using System.Threading.Channels;

namespace twodog.Repl;

// Input owns this queue, never the engine thread. A session's submissions commit in order.
internal sealed class ReplWorkQueue : IDisposable
{
    private readonly object gate = new();
    private readonly Channel<Func<CancellationToken, Task>> work = Channel.CreateUnbounded<Func<CancellationToken, Task>>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private CancellationTokenSource cancellation;
    private readonly CancellationToken lifetime;
    private int pending;
    public Task Completion { get; }
    /// <summary>Submissions accepted but not yet finished, including the running one.</summary>
    public int Pending => Volatile.Read(ref pending);

    public ReplWorkQueue(CancellationToken lifetime)
    {
        this.lifetime = lifetime;
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        Completion = Task.Run(ConsumeAsync);
    }

    /// <param name="skipped">Runs instead of <paramref name="action"/> when cancellation comes first.</param>
    public Task Enqueue(Func<CancellationToken, Task> action, Action? skipped = null)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token;
        lock (gate) token = cancellation.Token;
        Interlocked.Increment(ref pending);
        if (!work.Writer.TryWrite(async _ =>
        {
            try
            {
                if (token.IsCancellationRequested) skipped?.Invoke();
                token.ThrowIfCancellationRequested();
                await action(token).ConfigureAwait(false);
                done.TrySetResult();
            }
            catch (OperationCanceledException) { done.TrySetCanceled(token); }
            catch (Exception error) { done.TrySetException(error); }
            finally { Interlocked.Decrement(ref pending); }
        }))
        {
            Interlocked.Decrement(ref pending);
            done.TrySetException(new InvalidOperationException("The session is closed."));
        }
        return done.Task;
    }

    // Cancels both running and already queued submissions; later input gets a fresh token.
    public void Cancel()
    {
        CancellationTokenSource cancelled;
        lock (gate)
        {
            cancelled = cancellation;
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        }
        try { cancelled.Cancel(); }
        finally { cancelled.Dispose(); }
    }

    public void Complete() => work.Writer.TryComplete();
    private async Task ConsumeAsync()
    {
        await foreach (var action in work.Reader.ReadAllAsync()) await action(lifetime).ConfigureAwait(false);
    }

    public void Dispose()
    {
        Complete();
        cancellation.Dispose();
    }
}
