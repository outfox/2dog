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
/// </summary>
public class EngineInstanceFixture : IDisposable
{
    private readonly EngineHost _host = new();
    private readonly EngineWorkQueue _queue = new();
    private readonly EngineInstance _instance;

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
        _instance = _host.Start(new InstanceOptions
        {
            Tag = Tag,
            ProjectDir = ScratchProject.Create(Tag, SourceProjectDir),
            Args = EngineArgs,
            // The test assembly (where the fixture subclass lives) roots the
            // instance ALC, so scenarios and their Godot deps resolve there.
            ProgramAssemblyPath = GetType().Assembly.Location,
            ProgramTypeName = $"{typeof(ResidentProgram).FullName}, {typeof(ResidentProgram).Assembly.GetName().Name}",
            Variant = Variant,
            State = _queue,
        });
        if (!_instance.Booted.Wait(BootTimeout))
            throw new TimeoutException($"Engine instance '{Tag}' did not boot within {BootTimeout}.");
    }

    /// <summary>Executes a scenario on this instance's engine thread and returns its report.</summary>
    public string Run<TScenario>(string? argument = null) where TScenario : IEngineScenario
    {
        var type = typeof(TScenario);
        var task = _queue.Submit($"{type.FullName}, {type.Assembly.GetName().Name}", argument);
        if (!task.Wait(ScenarioTimeout))
            throw new TimeoutException($"Scenario {type.Name} timed out after {ScenarioTimeout} on instance '{Tag}'.");
        return task.Result;
    }

    public void Dispose()
    {
        _host.Dispose();
        // Make instance failures loud: xUnit reports fixture dispose exceptions.
        if (_instance.Completion.IsFaulted) _instance.Completion.GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }
}
