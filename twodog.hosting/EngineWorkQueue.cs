using System.Diagnostics.CodeAnalysis;

namespace twodog.Hosting;

/// <summary>
/// CoreLib-only work channel between the host and a resident engine program:
/// the host submits scenario type names, the program executes them on the
/// engine thread and completes each item with a report string. Lives in
/// twodog.hosting so both sides see the same type identity.
/// Closing the queue (instance shutdown) fails pending items and rejects new
/// ones, so no submission can be lost in a shutdown race.
/// </summary>
public sealed class EngineWorkQueue
{
    private readonly Queue<EngineWorkItem> _items = new();
    private readonly Lock _gate = new();
    private Exception? _closedReason;

    /// <summary>Host side: enqueue a scenario ("Full.Name, AssemblySimpleName").
    /// After <see cref="Close"/> the returned item is already faulted.</summary>
    public EngineWorkItem Submit(string scenarioTypeName, string? argument = null)
    {
        var item = new EngineWorkItem(scenarioTypeName, argument);
        lock (_gate)
        {
            if (_closedReason is { } reason)
            {
                item.Fail(reason);
                return item;
            }
            _items.Enqueue(item);
        }
        return item;
    }

    /// <summary>Instance side: next runnable item (canceled items are dropped),
    /// typically polled once per pumped frame. The item is already transitioned
    /// to running - complete it with <see cref="EngineWorkItem.Complete"/> or
    /// <see cref="EngineWorkItem.Fail"/>.</summary>
    public bool TryTake([NotNullWhen(true)] out EngineWorkItem? item)
    {
        lock (_gate)
        {
            while (_items.Count > 0)
            {
                var candidate = _items.Dequeue();
                if (!candidate.TryBeginExecution()) continue; // canceled while queued
                item = candidate;
                return true;
            }
        }
        item = null;
        return false;
    }

    /// <summary>Instance side, on shutdown: rejects future submissions and fails
    /// everything still queued with <paramref name="reason"/>.</summary>
    public void Close(Exception reason)
    {
        List<EngineWorkItem> pending;
        lock (_gate)
        {
            if (_closedReason is not null) return;
            _closedReason = reason;
            pending = [.. _items];
            _items.Clear();
        }
        foreach (var item in pending) item.Fail(reason);
    }
}

/// <summary>One submitted scenario. States: pending -> running (instance took it)
/// or pending -> canceled (host gave up waiting before it started). A scenario
/// that is already running cannot be aborted - only left behind.</summary>
public sealed class EngineWorkItem
{
    private const int Pending = 0, Running = 1, Canceled = 2;

    private readonly TaskCompletionSource<string> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _state;

    internal EngineWorkItem(string typeName, string? argument)
    {
        TypeName = typeName;
        Argument = argument;
    }

    public string TypeName { get; }
    public string? Argument { get; }

    /// <summary>The scenario's report; faulted on scenario/shutdown failure, canceled via <see cref="TryCancel"/>.</summary>
    public Task<string> Task => _completion.Task;

    /// <summary>Host side: prevents execution if the item has not started yet.</summary>
    public bool TryCancel()
    {
        if (Interlocked.CompareExchange(ref _state, Canceled, Pending) != Pending) return false;
        _completion.TrySetCanceled();
        return true;
    }

    internal bool TryBeginExecution() => Interlocked.CompareExchange(ref _state, Running, Pending) == Pending;

    public void Complete(string result) => _completion.TrySetResult(result);

    /// <summary>Sanitized: instance-ALC exception types never cross to the host.</summary>
    public void Fail(Exception reason) => _completion.TrySetException(ExceptionSanitizer.Sanitize(reason));
}
