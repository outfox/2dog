using System;
using System.Threading;

namespace twodog;

/// <summary>
/// Serializes engine boots across the whole process. Boot mutates process-global
/// state (the CWD via Godot's --path chdir, the GODOT_PROJECT_ASSEMBLY_DIR /
/// GODOTSHARP_DIR environment variables read during instance creation), and
/// multi-instance hosts run each engine in an isolated AssemblyLoadContext with
/// its own copy of this assembly - a plain static lock would be per-context.
/// A named local mutex has OS-level identity, so every copy contends on the
/// same lock; the process id keeps unrelated processes unserialized.
/// </summary>
internal static class ProcessBootLock
{
    /// <summary>Stable, documented name: tests contend on it deliberately.</summary>
    internal static string Name => $@"Local\2dog-engine-boot-{Environment.ProcessId}";

    /// <summary>
    /// Acquires the process-wide boot lock, or throws <see cref="TimeoutException"/>
    /// (message contains "boot lock") without touching any engine state.
    /// </summary>
    internal static IDisposable Acquire(TimeSpan timeout)
    {
        var mutex = new Mutex(initiallyOwned: false, Name);
        try
        {
            bool acquired;
            try
            {
                acquired = mutex.WaitOne(timeout);
            }
            catch (AbandonedMutexException)
            {
                // A previous boot's thread died while holding the lock. The lock is
                // ours now, and the next boot rewrites the global state it guards.
                acquired = true;
            }

            if (!acquired)
                throw new TimeoutException(
                    $"{nameof(Engine)}: timed out after {timeout} waiting for the process-wide boot lock - " +
                    $"another engine boot in this process is stuck (see {nameof(Engine)}.{nameof(Engine.BootLockTimeout)}).");
        }
        catch
        {
            mutex.Dispose();
            throw;
        }

        return new Holder(mutex);
    }

    private sealed class Holder(Mutex mutex) : IDisposable
    {
        public void Dispose()
        {
            mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }
}
