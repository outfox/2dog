namespace twodog.Hosting;

/// <summary>Options for one engine instance started via <see cref="EngineHost"/>.</summary>
public sealed record InstanceOptions
{
    /// <summary>Short unique name; used for thread/ALC naming and diagnostics.</summary>
    public required string Tag { get; init; }

    /// <summary>Directory containing project.godot (instances sharing a project
    /// contend on .godot/ and user:// - give each its own copy). Prefer an
    /// absolute path: CWD is process-global and engine boots move it; relative
    /// paths are resolved once, at Start, against the then-current CWD.</summary>
    public string ProjectDir { get; init; } = ".";

    /// <summary>Extra engine args (e.g. "--headless").</summary>
    public string[] Args { get; init; } = [];

    /// <summary>Assembly whose deps.json roots the instance ALC (the app/test
    /// assembly referencing twodog.bindings). Set implicitly by Start&lt;T&gt;.</summary>
    public string? ProgramAssemblyPath { get; init; }

    /// <summary>Program type: "Full.Name" (searched in the program assembly) or
    /// "Full.Name, AssemblySimpleName" for a type in one of its dependencies.</summary>
    public string? ProgramTypeName { get; init; }

    /// <summary>Native variant when <see cref="NativeSourcePath"/> is not set: 'release', 'debug', or 'editor'.</summary>
    public string Variant { get; init; } = "debug";

    /// <summary>Explicit libgodot to use as the pool's copy source (skips variant resolution).</summary>
    public string? NativeSourcePath { get; init; }

    /// <summary>Extra Godot-free assemblies (simple names) resolved from the
    /// default ALC instead of per-instance, so their types keep one identity.</summary>
    public string[] SharedAssemblies { get; init; } = [];

    /// <summary>Receives messages the program sends via <see cref="IInstanceContext.Post"/>.</summary>
    public Action<string>? OnMessage { get; init; }

    /// <summary>Arbitrary CoreLib-only state exposed as <see cref="IInstanceContext.State"/>.</summary>
    public object? State { get; init; }

    /// <summary>How long Dispose waits for the program to honor QuitRequested
    /// before throwing TimeoutException.</summary>
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(60);
}
