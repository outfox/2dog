using System.Threading.Channels;

namespace twodog.Repl;

// Input owns this queue, never the engine thread. A session's submissions commit in order.
internal sealed class ReplWorkQueue : IDisposable
{
    private readonly object gate = new();
    private readonly Channel<Func<CancellationToken, Task>> work = Channel.CreateUnbounded<Func<CancellationToken, Task>>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private CancellationTokenSource pending;
    private readonly CancellationToken lifetime;
    public Task Completion { get; }

    public ReplWorkQueue(CancellationToken lifetime)
    {
        this.lifetime = lifetime;
        pending = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        Completion = Task.Run(ConsumeAsync);
    }

    public Task Enqueue(Func<CancellationToken, Task> action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token;
        lock (gate) token = pending.Token;
        if (!work.Writer.TryWrite(async _ =>
        {
            try { token.ThrowIfCancellationRequested(); await action(token).ConfigureAwait(false); done.TrySetResult(); }
            catch (OperationCanceledException) { done.TrySetCanceled(token); }
            catch (Exception error) { done.TrySetException(error); }
        })) done.TrySetException(new InvalidOperationException("The session is closed."));
        return done.Task;
    }

    // Cancels both running and already queued submissions; later input gets a fresh token.
    public void Cancel()
    {
        CancellationTokenSource cancelled;
        lock (gate)
        {
            cancelled = pending;
            pending = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
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
        pending.Dispose();
    }
}
