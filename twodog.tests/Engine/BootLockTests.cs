using twodog;

namespace twodog.tests.EngineTests;

// Contends on the process-wide boot lock, so nothing else may boot while these
// tests run. No fixture on purpose; like all Godot collections it disables
// parallelization.
[CollectionDefinition(nameof(BootLockCollection), DisableParallelization = true)]
public class BootLockCollection;

[Collection(nameof(BootLockCollection))]
public class BootLockTests
{
    [Fact]
    public void Start_TimesOutFailClosed_WhileAnotherBootHoldsTheLock()
    {
        // One named local mutex per process, shared by every load context's
        // copy of the engine assembly. A mutex is reentrant on its owning
        // thread, so the contending holder must be a different thread - like
        // a real concurrent boot.
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = new Thread(() =>
        {
            using var mutex = new Mutex(initiallyOwned: false, Engine.BootLockName);
            mutex.WaitOne();
            held.Set();
            release.Wait();
            mutex.ReleaseMutex();
        }) { IsBackground = true };
        holder.Start();
        held.Wait(TestContext.Current.CancellationToken);

        try
        {
            // The bogus project path is never touched: the lock times out
            // before any environment write or native call happens.
            using var engine = new Engine(
                "bootlock-timeout",
                Path.Combine(Path.GetTempPath(), "2dog-no-such-project"),
                "--headless")
            {
                BootLockTimeout = TimeSpan.FromMilliseconds(250),
            };

            var ex = Assert.Throws<TimeoutException>(() => engine.Start());
            Assert.Contains("boot lock", ex.Message);
        }
        finally
        {
            release.Set();
            holder.Join();
        }
    }
}
