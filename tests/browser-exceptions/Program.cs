using System.Runtime.CompilerServices;
using twodog;

// Separate processes are necessary: unhandled async-void failures terminate
// the runtime, and process-wide exception hooks must not contaminate xUnit.
WebExceptionDiagnostics.Install();
WebExceptionDiagnostics.Install();
switch (args.Single())
{
    case "forgotten":
        await ForgetFailure("forgotten-task", static task => task.Forget());
        break;
    case "forgotten-completed":
        Task.FromException(new AggregateException(
            new InvalidOperationException("completed-task"), new Exception("second-failure"))).Forget();
        break;
    case "forgotten-source":
        var source = new TaskCompletionSource();
        var context = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new NeverPumpedContext());
        source.Task.Forget();
        SynchronizationContext.SetSynchronizationContext(context);
        source.SetException(new InvalidOperationException("source-task", new Exception("inner-failure")));
        await Task.Delay(100);
        GC.KeepAlive(source);
        break;
    case "forgotten-quiet":
        Task.CompletedTask.Forget();
        Task.FromResult(1).Forget();
        Task.FromCanceled(new CancellationToken(true)).Forget();
        var success = new TaskCompletionSource();
        success.Task.Forget();
        success.SetResult();
        var cancellation = new TaskCompletionSource();
        cancellation.Task.Forget();
        cancellation.SetCanceled();
        await Task.Delay(100);
        break;
    case "forgotten-value-task":
        new ValueTask<int>(Task.FromException<int>(new InvalidOperationException("generic-value-task"))).Forget();
        ValueTask.CompletedTask.Forget();
        await ForgetFailure("value-task", static task => new ValueTask(task).Forget());
        break;
    case "unobserved":
        AbandonFaultedTask();
        for (var i = 0; i < 4; i++)
        {
            GC.Collect();
            if (!OperatingSystem.IsBrowser()) GC.WaitForPendingFinalizers();
            await Task.Delay(50);
        }
        break;
    case "unhandled-first-chance":
        WebExceptionDiagnostics.LogFirstChanceExceptions = true;
        goto case "unhandled";
    case "unhandled":
        ThrowAsyncVoid();
        await Task.Delay(5000);
        throw new Exception("Async-void failure did not terminate the runtime");
    case "first-chance":
        WebExceptionDiagnostics.LogFirstChanceExceptions = true;
        WebExceptionDiagnostics.LogFirstChanceExceptions = true;
        var task = FailAsync("retained-task");
        await Settled(task);
        WebExceptionDiagnostics.LogFirstChanceExceptions = false;
        WebExceptionDiagnostics.LogFirstChanceExceptions = false;
        try { throw new InvalidOperationException("caught-after-disable"); }
        catch (InvalidOperationException) { }
        GC.KeepAlive(task);
        break;
    case "caught":
        try { await FailAsync("awaited-and-caught"); }
        catch (InvalidOperationException) { }
        break;
    case "broken-stderr":
        Console.SetError(new ThrowingWriter());
        WebExceptionDiagnostics.LogFirstChanceExceptions = true;
        try { throw new InvalidOperationException("original-failure"); }
        catch (InvalidOperationException e) when (e.Message == "original-failure") { }
        WebExceptionDiagnostics.LogFirstChanceExceptions = false;
        Task.FromException(new Exception("forgotten-with-broken-stderr")).Forget();
        break;
    default:
        throw new ArgumentException("Unknown probe mode");
}
Console.WriteLine("PROBE_COMPLETE");

[MethodImpl(MethodImplOptions.NoInlining)]
static void AbandonFaultedTask() => _ = FailAsync("abandoned-task");

// FailAsync throws on a pool thread on desktop, so fixed delays race it. This completes after the throw and after
// continuations registered earlier, without observing the exception.
static Task Settled(Task task) => task.ContinueWith(static _ => { }, CancellationToken.None,
    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

// The task faults only once forget and the wait are both registered, so the wait ends after Forget's report.
static async Task ForgetFailure(string message, Action<Task> forget)
{
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var task = FailAsync(message, gate.Task);
    forget(task);
    var settled = Settled(task);
    gate.SetResult();
    await settled;
    GC.KeepAlive(task);
}

static async Task FailAsync(string message, Task? gate = null)
{
    if (gate is null) await Task.Yield();
    else await gate;
    throw new InvalidOperationException(message, new Exception("inner-failure"));
}

static async void ThrowAsyncVoid()
{
    await Task.Yield();
    throw new InvalidOperationException("async-void-failure", new Exception("inner-failure"));
}

sealed class ThrowingWriter : StringWriter
{
    public override void WriteLine(string? value) => throw new IOException("stderr-failure");
}

sealed class NeverPumpedContext : SynchronizationContext
{
    public override void Post(SendOrPostCallback callback, object? state) { }
}
