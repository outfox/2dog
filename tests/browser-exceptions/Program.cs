using System.Runtime.CompilerServices;
using twodog;

// Separate processes are necessary: unhandled async-void failures terminate
// the runtime, and process-wide exception hooks must not contaminate xUnit.
WebExceptionDiagnostics.Install();
WebExceptionDiagnostics.Install();
switch (args.Single())
{
    case "forgotten":
        var forgotten = FailAsync("forgotten-task");
        forgotten.Forget();
        await Task.Delay(100);
        GC.KeepAlive(forgotten);
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
        new ValueTask(FailAsync("value-task")).Forget();
        new ValueTask<int>(Task.FromException<int>(new InvalidOperationException("generic-value-task"))).Forget();
        ValueTask.CompletedTask.Forget();
        await Task.Delay(100);
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
        await Task.Delay(50);
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

static async Task FailAsync(string message)
{
    await Task.Yield();
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
