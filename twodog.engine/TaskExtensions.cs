using System;
using System.Threading;
using System.Threading.Tasks;

namespace twodog;

/// <summary>Reports failures from intentionally unawaited asynchronous operations.</summary>
public static class TaskExtensions
{
    /// <summary>
    /// Observes a task without awaiting it and writes any failure to standard error
    /// (console errors in browser hosts) as soon as the task faults. Successful
    /// completion and cancellation produce no output. Works with <see cref="Task{TResult}"/> too.
    /// </summary>
    /// <param name="task">The operation whose failure should be reported.</param>
    public static void Forget(this Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (task.IsCompletedSuccessfully || task.IsCanceled) return;
        if (task.IsFaulted)
        {
            WebExceptionDiagnostics.ReportTaskFailure(task.Exception!);
            return;
        }

        // Do not capture Godot's frame scheduler or a host synchronization
        // context: reporting must still work after that engine/view stops.
        _ = task.ContinueWith(static completed =>
                WebExceptionDiagnostics.ReportTaskFailure(completed.Exception!),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Consumes a value task without awaiting it and reports failures as soon as
    /// it faults. Do not await or otherwise consume the value task afterward.
    /// </summary>
    /// <param name="task">The operation whose failure should be reported.</param>
    public static void Forget(this ValueTask task) => task.AsTask().Forget();

    /// <summary>
    /// Consumes a value task without awaiting it and reports failures as soon as
    /// it faults. The result is discarded. Do not consume the value task afterward.
    /// </summary>
    /// <typeparam name="TResult">The discarded result type.</typeparam>
    /// <param name="task">The operation whose failure should be reported.</param>
    public static void Forget<TResult>(this ValueTask<TResult> task) => task.AsTask().Forget();
}
