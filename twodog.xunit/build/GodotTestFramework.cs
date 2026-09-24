using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using twodog.Testing;
using Xunit.Sdk;
using Xunit.v3;

namespace twodog.Testing.Xunit;

/// <summary>
/// xUnit's framework, except that each test collection using a 2dog fixture runs on a thread of its own: the engine
/// starts there, and the tests, their continuations, and the fixture's disposal all stay on it, as Godot requires.
/// On Windows the thread is STA, which Godot's windowing needs for drag-and-drop and common controls.
/// Registered for the whole test assembly unless TwoDogGodotTestThread is false.
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
/// </summary>
public static class GodotTestThread
{
    /// <summary>Runs <paramref name="work"/> on a new thread and completes with its result once it finishes.</summary>
    public static Task<T> Run<T>(Func<ValueTask<T>> work)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => Loop(work, result)) { Name = "2dog test thread", IsBackground = true };
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return result.Task;
    }

    private static void Loop<T>(Func<ValueTask<T>> work, TaskCompletionSource<T> result)
    {
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
