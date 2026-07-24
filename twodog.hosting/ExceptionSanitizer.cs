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

    private static bool IsDefaultAlcChain(Exception? e)
    {
        for (var depth = 0; e is not null && depth < 32; depth++)
        {
            var alc = AssemblyLoadContext.GetLoadContext(e.GetType().Assembly);
            if (alc is not null && alc != AssemblyLoadContext.Default) return false;
            if (e is AggregateException aggregate && !aggregate.InnerExceptions.All(IsDefaultAlcChain)) return false;
            e = e.InnerException;
        }
        return true;
    }
}
