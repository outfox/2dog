using System.Collections.Concurrent;

namespace Godot;

/// <summary>
/// Marshals async continuations (await Task.Delay, ConfigureAwait(true) work,
/// async event handlers) back to the engine's main thread. Callbacks queue on
/// Post and run when the host pumps the context - once per engine iteration.
///
/// SignalAwaiter does NOT need this (its continuations run inline in the
/// engine's signal callback); this context serves Task-based awaits inside
/// async gameplay code.
/// </summary>
public sealed class GodotSynchronizationContext : SynchronizationContext
{
    private readonly ConcurrentQueue<(SendOrPostCallback callback, object? state, ManualResetEventSlim? done)> _queue = new();
    private readonly int _mainThreadId = System.Environment.CurrentManagedThreadId;

    /// <summary>The context installed by the host (null until Install).</summary>
    public static GodotSynchronizationContext? Installed { get; private set; }

    /// <summary>
    /// Creates and installs the context on the CURRENT thread (call from the
    /// thread that pumps the engine). Idempotent per process.
    /// </summary>
    public static GodotSynchronizationContext Install()
    {
        Installed ??= new GodotSynchronizationContext();
        SetSynchronizationContext(Installed);
        return Installed;
    }

    /// <summary>Runs all queued callbacks. Call from the main thread, once per frame.</summary>
    public static void PumpAll() => Installed?.Pump();

    public void Pump()
    {
        while (_queue.TryDequeue(out var entry))
        {
            try
            {
                entry.callback(entry.state);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"twodog.bindings: unhandled exception in posted callback: {e}");
            }
            finally
            {
                entry.done?.Set();
            }
        }
    }

    public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state, null));

    public override void Send(SendOrPostCallback d, object? state)
    {
        if (System.Environment.CurrentManagedThreadId == _mainThreadId)
        {
            d(state);
            return;
        }
        using var done = new ManualResetEventSlim();
        _queue.Enqueue((d, state, done));
        done.Wait();
    }

    public override SynchronizationContext CreateCopy() => this;
}
