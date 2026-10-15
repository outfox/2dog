using Godot;
using showcase;
using showcase.web;
using Engine = twodog.Engine;
using Environment = System.Environment;
using Thread = System.Threading.Thread;

namespace showcase.webmt;

/// <summary>
/// Multithreaded browser proof of concept: Godot runs on .NET's deputy worker while managed threads and Godot's
/// WorkerThreadPool run in parallel. Marks data-twodog-smoke=passed once every probe succeeds.
/// </summary>
internal static class Program
{
    private const int Parallelism = 4;

    private static int _frames;
    private static readonly List<(int Frame, TaskCompletionSource Done)> FrameWaiters = [];

    private static async Task<int> Main(string[] args)
    {
        Console.WriteLine($"2dog multithreaded web showcase starting on managed thread " +
                          $"{Environment.CurrentManagedThreadId}...");

        Engine.RegisterWebPluginsInitializer(TwoDogWebBoot.PluginsInitializer());
        var engine = new Engine("showcase.webmt", args: args);
        try
        {
            engine.Start();
            // Godot titles the page after application/config/name once it starts.
            DisplayServer.WindowSetTitle("2dog showcase — multithreaded web host");
            Require(OS.HasFeature("threads"), "the engine does not report the 'threads' feature");
            GD.Print("Hello from GodotSharp (multithreaded browser).");

            GodotApiSmoke.RunAll(engine.Tree);

            // Start() installed Godot's SynchronizationContext: awaits below resume on this thread inside Godot frames,
            // so they need the frame loop running.
            engine.Run(OnFrame);

            await ManagedThreadsRunConcurrently();
            GodotWorkerThreadPoolRunsOffMainThread();
            await GDExtensionRunsOnEveryThread();
            await FramesRunAlongsideBackgroundWork();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"2DOG_WASM_SMOKE_FAILED: {exception}");
            JavaScriptBridge.Eval("document.documentElement.setAttribute('data-twodog-smoke', 'failed')");
            await engine.DisposeAsync();
            throw;
        }

        JavaScriptBridge.Eval("document.documentElement.setAttribute('data-twodog-smoke', 'passed')");
        Console.WriteLine("2DOG_WASM_SMOKE_PASSED");
        return 0;
    }

    private static void OnFrame()
    {
        _frames++;
        for (var i = FrameWaiters.Count - 1; i >= 0; i--)
        {
            if (FrameWaiters[i].Frame > _frames) continue;
            FrameWaiters[i].Done.TrySetResult();
            FrameWaiters.RemoveAt(i);
        }
    }

    private static Task FramesElapsed(int count)
    {
        var done = new TaskCompletionSource();
        FrameWaiters.Add((_frames + count, done));
        return done.Task;
    }

    /// <summary>
    /// Dedicated threads meet at a barrier, which only completes when all of them run at once. Each also calls into
    /// Godot, which must see a thread other than its main thread.
    /// </summary>
    private static async Task ManagedThreadsRunConcurrently()
    {
        var callerManagedId = Environment.CurrentManagedThreadId;
        var godotMain = OS.GetMainThreadId();
        var managedIds = new int[Parallelism];
        var godotIds = new ulong[Parallelism];
        using var barrier = new Barrier(Parallelism);
        var done = new TaskCompletionSource();
        var remaining = Parallelism;

        for (var i = 0; i < Parallelism; i++)
        {
            var index = i;
            var thread = new Thread(() =>
            {
                try
                {
                    managedIds[index] = Environment.CurrentManagedThreadId;
                    godotIds[index] = OS.GetThreadCallerId();
                    if (!barrier.SignalAndWait(TimeSpan.FromSeconds(20)))
                        throw new TimeoutException($"{barrier.ParticipantsRemaining} of {Parallelism} threads never " +
                                                   "reached the barrier: the threads did not run concurrently");
                    if (Interlocked.Decrement(ref remaining) == 0) done.TrySetResult();
                }
                catch (Exception e)
                {
                    done.TrySetException(e);
                }
            }) { IsBackground = true, Name = $"twodog-mt-{index}" };
            thread.Start();
        }

        await done.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Require(Environment.CurrentManagedThreadId == callerManagedId, "the await resumed off Godot's main thread");
        Require(managedIds.Distinct().Count() == Parallelism && !managedIds.Contains(callerManagedId),
            $"managed thread ids [{string.Join(", ", managedIds)}] are not distinct from each other and the caller");
        Require(godotIds.Distinct().Count() == Parallelism && !godotIds.Contains(godotMain),
            $"Godot saw caller ids [{string.Join(", ", godotIds)}] (main {godotMain})");
        Console.WriteLine($"2DOG_WASM_MT: {Parallelism} managed threads [{string.Join(", ", managedIds)}] met at a " +
                          $"barrier; Godot saw them as [{string.Join(", ", godotIds)}], main {godotMain}");
    }

    /// <summary>A group task on Godot's WorkerThreadPool invokes managed code on Godot's own worker threads.</summary>
    private static void GodotWorkerThreadPoolRunsOffMainThread()
    {
        var godotMain = OS.GetMainThreadId();
        const int elements = 16;
        var callerIds = new ulong[elements];
        var group = WorkerThreadPool.AddGroupTask(
            Callable.From((int index) =>
            {
                callerIds[index] = OS.GetThreadCallerId();
                var sum = 0.0;
                for (var i = 0; i < 200_000; i++) sum += Math.Sqrt(i + index);
                GC.KeepAlive(sum);
            }), elements, -1, true, "twodog multithreaded web probe");
        WorkerThreadPool.WaitForGroupTaskCompletion(group);

        var distinct = callerIds.Distinct().ToArray();
        Require(callerIds.All(id => id != 0), "a WorkerThreadPool element did not run");
        Require(distinct.Any(id => id != godotMain), "every WorkerThreadPool element ran on Godot's main thread");
        Console.WriteLine($"2DOG_WASM_MT: WorkerThreadPool ran {elements} elements on {distinct.Length} thread(s) " +
                          $"[{string.Join(", ", distinct)}], main {godotMain}");
    }

    /// <summary>
    /// Wasm function tables are per thread, so every thread instantiates the GDExtension side module itself: Godot's
    /// pool threads were idle when it loaded and catch up when they wake, new managed threads at thread start.
    /// </summary>
    private static async Task GDExtensionRunsOnEveryThread()
    {
        var godotMain = OS.GetMainThreadId();
        const int elements = 16;
        var poolSums = new long[elements];
        var poolIds = new ulong[elements];
        var group = WorkerThreadPool.AddGroupTask(
            Callable.From((int index) =>
            {
                poolIds[index] = OS.GetThreadCallerId();
                poolSums[index] = ProbeAdd(index, 1);
            }), elements, -1, true, "twodog multithreaded GDExtension probe");
        WorkerThreadPool.WaitForGroupTaskCompletion(group);
        Require(poolSums.Select((sum, index) => sum == index + 1).All(ok => ok),
            $"TwoDogProbe.add on WorkerThreadPool threads returned [{string.Join(", ", poolSums)}]");

        var managed = await Task.WhenAll(Enumerable.Range(0, Parallelism).Select(index => Task.Factory.StartNew(
            () => (Id: OS.GetThreadCallerId(), Sum: ProbeAdd(index, 100)), TaskCreationOptions.LongRunning)));
        Require(managed.Select((result, index) => result.Sum == index + 100).All(ok => ok),
            $"TwoDogProbe.add on managed threads returned [{string.Join(", ", managed.Select(r => r.Sum))}]");

        var threads = poolIds.Concat(managed.Select(r => r.Id)).Distinct().Where(id => id != godotMain).ToArray();
        Require(threads.Length > 1, "the GDExtension probe never ran off Godot's main thread");
        Console.WriteLine($"2DOG_WASM_MT: GDExtension TwoDogProbe.add ran on {threads.Length} threads besides main " +
                          $"[{string.Join(", ", threads)}]");
    }

    private static long ProbeAdd(long a, long b)
    {
        using var instance = ClassDB.Instantiate("TwoDogProbe");
        using var sum = instance.AsGodotObject().Call("add", a, b);
        return sum.AsInt64();
    }

    /// <summary>
    /// A background thread keeps counting while Godot runs frames; it must advance across them, so the engine loop
    /// and managed work run in parallel.
    /// </summary>
    private static async Task FramesRunAlongsideBackgroundWork()
    {
        const int frames = 60;
        long counter = 0;
        var stop = 0;
        var worker = new Thread(() =>
        {
            while (Volatile.Read(ref stop) == 0) Interlocked.Increment(ref counter);
        }) { IsBackground = true, Name = "twodog-mt-counter" };
        worker.Start();

        await FramesElapsed(1);
        var before = Interlocked.Read(ref counter);
        var started = DateTime.UtcNow;
        await FramesElapsed(frames);
        var advanced = Interlocked.Read(ref counter) - before;
        Volatile.Write(ref stop, 1);

        Require(advanced > 0, "the background thread made no progress while frames ran");
        Console.WriteLine($"2DOG_WASM_MT: {frames} frames in {(DateTime.UtcNow - started).TotalSeconds:F1} s on " +
                          $"managed thread {Environment.CurrentManagedThreadId}; the background thread counted " +
                          $"{advanced:N0} meanwhile");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"multithreaded web smoke failed: {message}");
    }
}
