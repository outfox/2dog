namespace twodog.Hosting;

/// <summary>
/// Entry point of an engine instance, implemented by app/test code and executed
/// INSIDE the instance's AssemblyLoadContext on its dedicated thread. Inside,
/// the programming model is exactly single-instance twodog: create an
/// <c>Engine</c> with <c>NativePath = ctx.NativePath</c>, start it, pump it.
/// Must have a parameterless constructor. Only CoreLib types may cross this
/// boundary - Godot types never leave the ALC.
/// </summary>
public interface IEngineProgram
{
    int Run(IInstanceContext ctx);
}

/// <summary>Host-provided context for one instance. CoreLib-only surface.</summary>
public interface IInstanceContext
{
    string Tag { get; }
    string ProjectDir { get; }
    string[] Args { get; }

    /// <summary>This instance's native libgodot copy (pass as Engine.NativePath).</summary>
    string NativePath { get; }

    /// <summary>Cooperative shutdown flag; poll it from the pump loop.</summary>
    bool QuitRequested { get; }

    /// <summary>Arbitrary host-supplied state (CoreLib types only), e.g. an <see cref="EngineWorkQueue"/>.</summary>
    object? State { get; }

    /// <summary>Signal that the engine booted successfully: completes
    /// <c>EngineInstance.Booted</c> and lets the next instance start booting.</summary>
    void SignalBooted();

    /// <summary>Send a message to the host (InstanceOptions.OnMessage).</summary>
    void Post(string message);
}
