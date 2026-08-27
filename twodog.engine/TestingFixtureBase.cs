using Godot;
using System;

namespace twodog.Testing;

/// <summary>Base class for test fixtures that own one Godot engine instance.</summary>
public abstract class FixtureBase : IDisposable
{
    /// <summary>Starts Godot with the supplied command-line arguments.</summary>
    protected FixtureBase(params string[] cmdLineArgs)
    {
        Console.WriteLine("Initializing Godot...");
        Console.WriteLine("cwd: " + System.Environment.CurrentDirectory);

        var projectPath = Engine.ResolveProjectDir();

        // Load game types into the default context before Godot can load a second copy.
        global::twodog.fixture.AssemblyPreloader.PreloadGameAssemblies(projectPath);

        Console.WriteLine("Godot project: " + projectPath);
        Engine = new Engine("twodog.tests", projectPath, WithLogFile(cmdLineArgs));
        GodotInstance = Engine.Start();
        Console.WriteLine("Godot initialized successfully.");
    }

    private static int _fixtureCount;

    /// <summary>
    /// Test hosts swallow the engine's native stdout/stderr, so a CI failure leaves no engine-side
    /// trace. With TWODOG_GODOT_LOG_DIR set, each fixture instance writes Godot's verbose log to
    /// its own file there (one engine instance per fixture; several fixtures per process).
    /// </summary>
    private static string[] WithLogFile(string[] cmdLineArgs)
    {
        var logDir = System.Environment.GetEnvironmentVariable("TWODOG_GODOT_LOG_DIR");
        if (string.IsNullOrEmpty(logDir)) return cmdLineArgs;

        System.IO.Directory.CreateDirectory(logDir);
        var index = System.Threading.Interlocked.Increment(ref _fixtureCount);
        var logFile = System.IO.Path.Combine(logDir,
            $"godot-{System.Environment.ProcessId}-{index}.log");
        Console.WriteLine("Godot log: " + logFile);
        return [.. cmdLineArgs, "--verbose", "--log-file", logFile];
    }

    /// <summary>The engine owned by this fixture.</summary>
    public Engine Engine { get; }

    /// <summary>The running native instance.</summary>
    public GodotInstance GodotInstance { get; }

    /// <summary>The active scene tree.</summary>
    public SceneTree Tree => Engine.Tree;

    /// <summary>Disposes the Godot instance and its owning engine.</summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        Console.WriteLine("Shutting down Godot...");
        GodotInstance.Dispose();
        Engine.Dispose();
        Console.WriteLine("Godot shut down successfully.");
    }
}
