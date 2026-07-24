using System.Reflection;

namespace twodog.Hosting;

/// <summary>
/// Handle to one running engine instance. The program executes on a dedicated
/// background thread inside the instance's ALC; observe it via
/// <see cref="Booted"/> and <see cref="Completion"/>.
/// </summary>
public sealed class EngineInstance : IDisposable
{
    // Serializes boots across all instances: held from thread start until the
    // program signals boot (or exits). Insurance against the engine's CWD dance
    // and loader-lock pressure; pumping runs fully parallel.
    private static readonly SemaphoreSlim BootGate = new(1, 1);

    private readonly TaskCompletionSource<int> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _booted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly InstanceContext _ctx;
    private readonly Thread _thread;
    private readonly TimeSpan _shutdownTimeout;
    private readonly TimeSpan _bootGateTimeout;
    private int _bootGateReleased;

    public string Tag { get; }

    /// <summary>This instance's pooled native copy.</summary>
    public string NativePath { get; }

    /// <summary>Completes when the program calls SignalBooted; faults if the
    /// program throws or exits without ever signaling boot.</summary>
    public Task Booted => _booted.Task;

    /// <summary>The program's Run() result; faulted on unhandled exceptions.</summary>
    public Task<int> Completion => _completion.Task;

    internal EngineInstance(InstanceOptions options, InstanceAlc alc, string programAssemblyPath,
                            string programTypeName, string nativePath)
    {
        Tag = options.Tag;
        NativePath = nativePath;
        _shutdownTimeout = options.ShutdownTimeout;
        _bootGateTimeout = options.BootGateTimeout;
        _ctx = new InstanceContext(options, nativePath, SignalBooted);
        _thread = new Thread(() => ThreadBody(alc, programAssemblyPath, programTypeName))
        {
            IsBackground = true,
            Name = $"2dog-{options.Tag}",
        };
        // Windowed engines need OLE (drag-drop, IME) on their thread, which
        // requires STA - same rule as [STAThread] on single-instance hosts.
        if (OperatingSystem.IsWindows()) _thread.SetApartmentState(ApartmentState.STA);
    }

    /// <summary>Called by EngineHost only after the instance is registered, so
    /// host disposal can never miss a started thread.</summary>
    internal void Begin() => _thread.Start();

    public void RequestQuit() => _ctx.RequestQuit();

    /// <summary>
    /// Requests quit and waits up to InstanceOptions.ShutdownTimeout for the
    /// program to finish; throws TimeoutException if it does not exit in time
    /// (program failures are not thrown here - observe <see cref="Completion"/>).
    /// When called from the instance's own thread (e.g. inside an OnMessage
    /// callback) it only requests quit - waiting would self-deadlock.
    /// Note: the native module, the ALC, and the pool slot stay resident until
    /// process exit regardless; Dispose releases the thread and the engine, not
    /// those (instance recycling is future work, see MULTI-INSTANCE-PLAN.md).
    /// </summary>
    public void Dispose()
    {
        RequestQuit();
        if (Thread.CurrentThread == _thread) return;
        // WaitAny (not Task.Wait) so a faulted Completion does not throw here.
        if (Task.WaitAny([_completion.Task], _shutdownTimeout) == -1)
            throw new TimeoutException(
                $"Engine instance '{Tag}' did not shut down within {_shutdownTimeout}. " +
                "Programs must poll IInstanceContext.QuitRequested from their pump loop.");
    }

    private void SignalBooted()
    {
        _booted.TrySetResult();
        ReleaseBootGate();
    }

    private void ReleaseBootGate()
    {
        if (Interlocked.Exchange(ref _bootGateReleased, 1) == 0) BootGate.Release();
    }

    private void ThreadBody(InstanceAlc alc, string programAssemblyPath, string programTypeName)
    {
        try
        {
            // Fail closed: proceeding without the gate would overlap boots
            // exactly when CWD and loader-lock serialization matter most.
            if (!BootGate.Wait(_bootGateTimeout))
            {
                _bootGateReleased = 1; // nothing acquired, nothing to release
                throw new TimeoutException(
                    $"Instance '{Tag}' timed out waiting for the boot gate - a previous instance is stuck booting.");
            }
            var program = (IEngineProgram)Activator.CreateInstance(ResolveType(alc, programAssemblyPath, programTypeName))!;
            var exit = program.Run(_ctx);
            _completion.TrySetResult(exit);
            // No-op if SignalBooted ran; otherwise the program exited without
            // ever booting and Booted must not report success.
            _booted.TrySetException(new InvalidOperationException(
                $"Program of instance '{Tag}' exited (code {exit}) without signaling boot."));
        }
        catch (Exception e)
        {
            // Instance-ALC exception types must not cross to the host.
            var sanitized = ExceptionSanitizer.Sanitize(e);
            _booted.TrySetException(sanitized);
            _completion.TrySetException(sanitized);
        }
        finally
        {
            ReleaseBootGate();
        }
    }

    /// <summary>Resolves "Full.Name" (program assembly) or "Full.Name, AssemblySimpleName"
    /// (any dependency) inside the instance ALC.</summary>
    private static Type ResolveType(InstanceAlc alc, string programAssemblyPath, string programTypeName)
    {
        var rootAssembly = alc.LoadFromAssemblyPath(programAssemblyPath);
        var parts = programTypeName.Split(',', 2);
        if (parts.Length == 2)
        {
            var assembly = alc.LoadFromAssemblyName(new AssemblyName(parts[1].Trim()));
            return assembly.GetType(parts[0].Trim(), throwOnError: true)!;
        }
        return rootAssembly.GetType(programTypeName, throwOnError: true)!;
    }

    private sealed class InstanceContext(InstanceOptions options, string nativePath, Action signalBooted) : IInstanceContext
    {
        private volatile bool _quit;

        public string Tag => options.Tag;
        public string ProjectDir => options.ProjectDir; // pre-normalized by EngineHost.Start
        public string[] Args => options.Args;
        public string NativePath => nativePath;
        public bool QuitRequested => _quit;
        public object? State => options.State;

        public void RequestQuit() => _quit = true;
        public void SignalBooted() => signalBooted();
        public void Post(string message) => options.OnMessage?.Invoke(message);
    }
}
