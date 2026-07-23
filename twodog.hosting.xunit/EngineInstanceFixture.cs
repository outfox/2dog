using System.Runtime.ExceptionServices;
using twodog.Hosting.Runtime;

namespace twodog.Hosting.Xunit;

/// <summary>
/// One engine instance per xUnit collection - collections with their own
/// fixture subclass run IN PARALLEL, each against an isolated engine.
///
/// Usage: subclass per collection (override <see cref="Tag"/> at minimum),
/// register via ICollectionFixture&lt;MyFixture&gt; WITHOUT
/// DisableParallelization, write tests as <see cref="IEngineScenario"/>
/// implementations and execute them with <see cref="Run{TScenario}"/>.
/// Scenarios run inside the instance's ALC on its engine thread; only the
/// returned string crosses back - assert on that.
///
/// In-process limits apply (CWD, env vars, crash blast radius, handlers are
/// process-global); tests needing full isolation belong in separate processes.
/// </summary>
public class EngineInstanceFixture : IDisposable
{
    private readonly EngineHost _host = new();
    private readonly EngineWorkQueue _queue = new();
    private readonly EngineInstance _instance;
    private readonly string _projectDir;

    /// <summary>Unique per fixture subclass; names the instance, thread, and user:// dir.</summary>
    protected virtual string Tag => GetType().Name;

    /// <summary>Source project to copy per instance; null generates a minimal headless project.</summary>
    protected virtual string? SourceProjectDir => null;

    protected virtual string[] EngineArgs => ["--headless"];
    protected virtual string Variant => "debug";
    protected virtual TimeSpan BootTimeout => TimeSpan.FromSeconds(120);
    protected virtual TimeSpan ScenarioTimeout => TimeSpan.FromSeconds(60);

    public EngineInstanceFixture()
    {
        _projectDir = ScratchProject.Create(Tag, SourceProjectDir);
        try
        {
            _instance = _host.Start(new InstanceOptions
            {
                Tag = Tag,
                ProjectDir = _projectDir,
                Args = EngineArgs,
                // The test assembly (where the fixture subclass lives) roots the
                // instance ALC, so scenarios and their Godot deps resolve there.
                ProgramAssemblyPath = GetType().Assembly.Location,
                ProgramTypeName = $"{typeof(ResidentProgram).FullName}, {typeof(ResidentProgram).Assembly.GetName().Name}",
                Variant = Variant,
                State = _queue,
            });
            if (Task.WaitAny([_instance.Booted], BootTimeout) == -1)
                throw new TimeoutException($"Engine instance '{Tag}' did not boot within {BootTimeout}.");
            _instance.Booted.GetAwaiter().GetResult(); // propagate boot failure with its real stack
        }
        catch (Exception boot)
        {
            // xUnit never disposes a fixture whose ctor threw - clean up here
            // or leak the host, the instance thread, and the scratch dir.
            try
            {
                _host.Dispose();
                CleanupScratch();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException(boot, cleanup);
            }
            ExceptionDispatchInfo.Throw(boot);
            throw; // unreachable
        }
    }

    /// <summary>Executes a scenario on this instance's engine thread and returns
    /// its report. On timeout the item is canceled if it has not started; an
    /// already-running scenario cannot be aborted and may still finish against
    /// the engine - treat the instance as suspect after a timeout.</summary>
    public string Run<TScenario>(string? argument = null) where TScenario : IEngineScenario
    {
        var type = typeof(TScenario);
        var item = _queue.Submit($"{type.FullName}, {type.Assembly.GetName().Name}", argument);
        // WaitAny: a faulted task must surface via GetResult below, not as AggregateException here.
        if (Task.WaitAny([item.Task], ScenarioTimeout) == -1)
        {
            item.TryCancel();
            throw new TimeoutException($"Scenario {type.Name} timed out after {ScenarioTimeout} on instance '{Tag}'.");
        }
        return item.Task.GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        try
        {
            _host.Dispose();
            // Make instance failures loud: xUnit reports fixture dispose exceptions.
            if (_instance.Completion.IsFaulted) _instance.Completion.GetAwaiter().GetResult();
        }
        finally
        {
            CleanupScratch();
        }
        GC.SuppressFinalize(this);
    }

    private void CleanupScratch()
    {
        // Best effort - a straggler (e.g. a timed-out scenario) may still hold
        // files - but never silent: leftover scratch dirs accumulate under %TEMP%.
        try
        {
            Directory.Delete(_projectDir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(
                $"[2dog.hosting] warning: could not delete scratch project '{_projectDir}' for instance '{Tag}': {e.Message}");
        }
    }
}
