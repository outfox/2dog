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
        Engine = new Engine("twodog.tests", projectPath, cmdLineArgs);
        GodotInstance = Engine.Start();
        Console.WriteLine("Godot initialized successfully.");
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
