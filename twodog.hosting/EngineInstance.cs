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

    public string Tag { get; }

    /// <summary>This instance's pooled native copy.</summary>
    public string NativePath { get; }

    /// <summary>Completes when the program calls SignalBooted (or faults if it dies first).</summary>
    public Task Booted => _booted.Task;

    /// <summary>The program's Run() result; faulted on unhandled exceptions.</summary>
    public Task<int> Completion => _completion.Task;

    internal EngineInstance(InstanceOptions options, InstanceAlc alc, string programAssemblyPath,
                            string programTypeName, string nativePath)
    {
        Tag = options.Tag;
        NativePath = nativePath;
        _ctx = new InstanceContext(options, nativePath, SignalBooted);

        var thread = new Thread(() => ThreadBody(alc, programAssemblyPath, programTypeName))
        {
            IsBackground = true,
            Name = $"2dog-{options.Tag}",
        };
        thread.Start();
    }

    public void RequestQuit() => _ctx.RequestQuit();

    /// <summary>Requests quit and waits for the program to finish. Failures are
    /// not thrown here - observe them via <see cref="Completion"/>.</summary>
    public void Dispose()
    {
        RequestQuit();
        try
        {
            _completion.Task.Wait();
        }
        catch
        {
            // Surfaced via Completion for callers who care.
        }
    }

    private int _bootGateReleased;

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
        if (!BootGate.Wait(TimeSpan.FromSeconds(120)))
            _bootGateReleased = 1; // timed out: nothing to release, boot unserialized
        try
        {
            var program = (IEngineProgram)Activator.CreateInstance(ResolveType(alc, programAssemblyPath, programTypeName))!;
            var exit = program.Run(_ctx);
            _completion.TrySetResult(exit);
            // Run-to-completion programs never SignalBooted; unblock awaiters.
            _booted.TrySetResult();
        }
        catch (Exception e)
        {
            _booted.TrySetException(e);
            _completion.TrySetException(e);
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
        public string ProjectDir { get; } = Path.GetFullPath(options.ProjectDir);
        public string[] Args => options.Args;
        public string NativePath => nativePath;
        public bool QuitRequested => _quit;
        public object? State => options.State;

        public void RequestQuit() => _quit = true;
        public void SignalBooted() => signalBooted();
        public void Post(string message) => options.OnMessage?.Invoke(message);
    }
}
