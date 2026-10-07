using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using twodog.Testing;
using Xunit.Sdk;
using Xunit.v3;

namespace twodog.Testing.Xunit;

/// <summary>
/// xUnit's framework, except that each test collection using a 2dog fixture runs on a <see cref="GodotTestThread"/>:
/// the engine starts there, and the tests, their continuations, and the fixture's disposal all stay on it, as Godot
/// requires. Registered for the whole test assembly unless TwoDogGodotTestThread is false.
/// </summary>
public class GodotTestFramework : XunitTestFramework
{
    private readonly string? _configFileName;

    // xUnit creates frameworks through a parameterless constructor or one taking the config file name.
    public GodotTestFramework() { }

    public GodotTestFramework(string? configFileName) : base(configFileName) => _configFileName = configFileName;

    protected override ITestFrameworkExecutor CreateExecutor(Assembly assembly)
        => new GodotTestFrameworkExecutor(new XunitTestAssembly(assembly, _configFileName, assembly.GetName().Version));
}

/// <summary>Runs tests through <see cref="GodotTestAssemblyRunner"/>.</summary>
public class GodotTestFrameworkExecutor(IXunitTestAssembly testAssembly) : XunitTestFrameworkExecutor(testAssembly)
{
    public override async ValueTask RunTestCases(IReadOnlyCollection<IXunitTestCase> testCases, IMessageSink executionMessageSink,
        ITestFrameworkExecutionOptions executionOptions, CancellationToken cancellationToken)
    {
        // Mirrors XunitTestFrameworkExecutor.RunTestCases, which hard-codes its own assembly runner.
        SetEnvironment("XUNIT_ASSERT_EQUIVALENT_MAX_DEPTH", executionOptions.AssertEquivalentMaxDepth());
        SetEnvironment("XUNIT_PRINT_MAX_ENUMERABLE_LENGTH", executionOptions.PrintMaxEnumerableLength());
        SetEnvironment("XUNIT_PRINT_MAX_OBJECT_DEPTH", executionOptions.PrintMaxObjectDepth());
        SetEnvironment("XUNIT_PRINT_MAX_OBJECT_MEMBER_COUNT", executionOptions.PrintMaxObjectMemberCount());
        SetEnvironment("XUNIT_PRINT_MAX_STRING_LENGTH", executionOptions.PrintMaxStringLength());

        await GodotTestAssemblyRunner.Instance.Run(TestAssembly, testCases, executionMessageSink, executionOptions, cancellationToken);
    }

    private static void SetEnvironment(string name, int? value)
    {
        if (value.HasValue) Environment.SetEnvironmentVariable(name, value.Value.ToString(CultureInfo.InvariantCulture));
    }
}

/// <summary>Runs collections that use a 2dog fixture on a <see cref="GodotTestThread"/>, and all others as usual.</summary>
public class GodotTestAssemblyRunner : XunitTestAssemblyRunner
{
    /// <summary>The runner instance used by <see cref="GodotTestFrameworkExecutor"/>.</summary>
    public static new GodotTestAssemblyRunner Instance { get; } = new();

    protected GodotTestAssemblyRunner() { }

    protected override ValueTask<RunSummary> RunTestCollection(XunitTestAssemblyRunnerContext ctxt,
        IXunitTestCollection testCollection, IReadOnlyCollection<IXunitTestCase> testCases)
        => UsesGodot(testCollection, testCases)
            ? new ValueTask<RunSummary>(GodotTestThread.Run(() => base.RunTestCollection(ctxt, testCollection, testCases)))
            : base.RunTestCollection(ctxt, testCollection, testCases);

    private static bool UsesGodot(IXunitTestCollection collection, IReadOnlyCollection<IXunitTestCase> testCases)
        => collection.CollectionFixtureTypes.Concat(testCases.SelectMany(c => c.TestClass.ClassFixtureTypes))
            .Any(type => type.IsAssignableTo(typeof(FixtureBase)));
}

/// <summary>
/// A thread that runs async work to completion, returning every continuation to itself. While idle it also runs
/// the continuations Godot queued on its own synchronization context, which Godot otherwise only runs during frames.
/// <para>
/// On macOS, AppKit only works on the process main thread, so while the test assembly's entry point runs through
/// <see cref="RunMain"/>, all work runs there, one run at a time. Elsewhere each run gets a new thread, which on
/// Windows is STA, as Godot's windowing needs for drag-and-drop and common controls.
/// </para>
/// </summary>
public static class GodotTestThread
{
    private const string ThreadName = "2dog test thread";

    // While RunMain lends the process main thread (macOS only), the work queued for it.
    private static BlockingCollection<Action>? _mainThread;

    /// <summary>
    /// Runs a test assembly's entry point. On macOS, <paramref name="main"/> runs on another thread while the calling
    /// (main) thread becomes the 2dog test thread and runs the work given to <see cref="Run{T}"/> until
    /// <paramref name="main"/> returns. Elsewhere this just calls <paramref name="main"/>.
    /// </summary>
    /// <remarks>
    /// 2dog.xunit makes this the test assembly's entry point around the one xUnit generates. A project with its own
    /// entry point should call this from it, passing what it would otherwise run.
    /// </remarks>
    public static int RunMain(Func<int> main)
    {
        if (!OperatingSystem.IsMacOS()) return main();

        var queue = new BlockingCollection<Action>();
        var exitCode = 0;
        ExceptionDispatchInfo? error = null;
        var runner = new Thread(() =>
        {
            try
            {
                exitCode = main();
            }
            catch (Exception exception)
            {
                error = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                queue.CompleteAdding();
            }
        }) { Name = "xUnit runner" };

        Thread.CurrentThread.Name = ThreadName;
        _mainThread = queue;
        try
        {
            runner.Start();
            foreach (var work in queue.GetConsumingEnumerable()) work();
            runner.Join();
        }
        finally
        {
            _mainThread = null;
        }

        error?.Throw();
        return exitCode;
    }

    /// <summary>
    /// Runs <paramref name="work"/> on the 2dog test thread (the main thread while <see cref="RunMain"/> lends it,
    /// otherwise a new thread) and completes with its result once it finishes.
    /// </summary>
    public static Task<T> Run<T>(Func<ValueTask<T>> work)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_mainThread is { } mainThread)
        {
            // Flows the caller's async-locals (xUnit's TestContext among them), as Thread.Start does below.
            var caller = ExecutionContext.Capture();
            mainThread.Add(caller is null
                ? () => Loop(work, result)
                : () => ExecutionContext.Run(caller, _ => Loop(work, result), null));
            return result.Task;
        }

        var thread = new Thread(() => Loop(work, result)) { Name = ThreadName, IsBackground = true };
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return result.Task;
    }

    private static void Loop<T>(Func<ValueTask<T>> work, TaskCompletionSource<T> result)
    {
        var previous = SynchronizationContext.Current;
        var context = new QueueContext();
        SynchronizationContext.SetSynchronizationContext(context);
        context.Post(async _ =>
        {
            try
            {
                result.TrySetResult(await work());
            }
            catch (Exception error)
            {
                result.TrySetException(error);
            }
        }, null);

        while (!result.Task.IsCompleted)
        {
            // Polls briefly, since posts to Godot's context do not wake this loop.
            if (context.TryTake(out var item, millisecondsTimeout: 1))
                Invoke(item, result);
            if (SynchronizationContext.Current is Godot.GodotSynchronizationContext godot)
                godot.ExecutePendingContinuations();
        }

        // The main thread outlives the run; leave neither this context nor Godot's installed on it.
        SynchronizationContext.SetSynchronizationContext(previous);
    }

    private static void Invoke<T>((SendOrPostCallback Callback, object? State) item, TaskCompletionSource<T> result)
    {
        try
        {
            item.Callback(item.State);
        }
        catch (Exception error)
        {
            result.TrySetException(error);
        }
    }

    private sealed class QueueContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback, object?)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public bool TryTake(out (SendOrPostCallback, object?) item, int millisecondsTimeout)
            => _queue.TryTake(out item, millisecondsTimeout);

        public override SynchronizationContext CreateCopy() => this;
    }
}
