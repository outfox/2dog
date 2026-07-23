using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace twodog.Hosting;

/// <summary>
/// CoreLib-only work channel between the host and a resident engine program:
/// the host submits scenario type names, the program executes them on the
/// engine thread and completes each item with a report string. Lives in
/// twodog.hosting so both sides see the same type identity.
/// </summary>
public sealed class EngineWorkQueue
{
    private readonly ConcurrentQueue<EngineWorkItem> _items = new();

    /// <summary>Host side: enqueue a scenario ("Full.Name, AssemblySimpleName") and await its report.</summary>
    public Task<string> Submit(string scenarioTypeName, string? argument = null)
    {
        var item = new EngineWorkItem(scenarioTypeName, argument);
        _items.Enqueue(item);
        return item.Completion.Task;
    }

    /// <summary>Instance side: drain pending work (typically once per pumped frame).</summary>
    public bool TryTake([NotNullWhen(true)] out EngineWorkItem? item) => _items.TryDequeue(out item);
}

public sealed class EngineWorkItem(string typeName, string? argument)
{
    public string TypeName { get; } = typeName;
    public string? Argument { get; } = argument;
    public TaskCompletionSource<string> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
