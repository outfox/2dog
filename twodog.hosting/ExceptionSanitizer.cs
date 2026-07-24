using System.Runtime.Loader;

namespace twodog.Hosting;

/// <summary>
/// Thrown in place of an instance-ALC exception type crossing to the host: the
/// original type cannot be caught by identity outside its ALC and may retain
/// Godot wrappers. Carries the original exception's full text instead.
/// </summary>
public sealed class EngineInstanceException(string originalType, string details)
    : Exception($"[{originalType}] {details}")
{
    public string OriginalType { get; } = originalType;
}

/// <summary>Enforces the CoreLib-only boundary for failures: default-ALC-typed
/// exception chains pass through unchanged, anything else is flattened.</summary>
internal static class ExceptionSanitizer
{
    public static Exception Sanitize(Exception e) =>
        IsDefaultAlcChain(e) ? e : new EngineInstanceException(e.GetType().FullName ?? "unknown", e.ToString());

    /// <summary>Worklist walk with a shared node budget and a visited set:
    /// arbitrary program exception graphs may be deep, wide, or cyclic, and a
    /// StackOverflow here would take down the whole host process.</summary>
    private static bool IsDefaultAlcChain(Exception root)
    {
        var pending = new Stack<Exception>();
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        pending.Push(root);
        for (var budget = 256; pending.Count > 0 && budget > 0; budget--)
        {
            var e = pending.Pop();
            if (!visited.Add(e)) continue;
            var alc = AssemblyLoadContext.GetLoadContext(e.GetType().Assembly);
            if (alc is not null && alc != AssemblyLoadContext.Default) return false;
            if (e is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions) pending.Push(inner);
            }
            if (e.InnerException is { } chained) pending.Push(chained);
        }
        // Budget exhausted with only default-ALC nodes seen: pass through rather
        // than flatten a chain we could not disprove.
        return true;
    }
}
