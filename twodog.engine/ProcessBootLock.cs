using System;
using System.Threading;

namespace twodog;

/// <summary>
/// Serializes engine boots (they mutate CWD and env vars) across the whole process. A static lock would be
/// per-ALC under multi-instance hosts; a named local mutex keyed by process id has OS-level identity.
/// </summary>
internal static class ProcessBootLock
{
    /// <summary>Stable, documented name: tests contend on it deliberately.</summary>
    internal static string Name => $@"Local\2dog-engine-boot-{Environment.ProcessId}";

    /// <summary>
    /// Acquires the boot lock or throws <see cref="TimeoutException"/> (message contains "boot lock"). Dispose the
    /// holder on the acquiring thread: mutex ownership is thread-affine.
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
