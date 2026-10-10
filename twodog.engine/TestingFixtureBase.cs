using Godot;
using System;
using System.Linq;
using System.Threading;

namespace twodog.Testing;

/// <summary>Base class for test fixtures that own one Godot engine instance.</summary>
public abstract class FixtureBase : IDisposable
{
    private readonly WindowsControlsContext? _controlsContext;
    private bool _disposed;
    // Switching headless/windowed modes across native restarts can recurse during
    // startup and crash the test host; require separate processes.
    private static int _displayMode;

    /// <summary>Starts Godot with the supplied command-line arguments.</summary>
    protected FixtureBase(params string[] cmdLineArgs)
    {
        var headless = cmdLineArgs.Contains("--headless")
            || cmdLineArgs.Zip(cmdLineArgs.Skip(1)).Any(pair => pair.First == "--display-driver" && pair.Second == "headless");
        var mode = headless ? 1 : 2;
        var previousMode = Volatile.Read(ref _displayMode);
        if (previousMode != 0 && previousMode != mode)
            throw new InvalidOperationException(
                "Use separate test projects for headless and rendering fixtures. " +
                "Switching display modes in one process is not supported by 2dog's test fixtures.");
        Console.WriteLine("Initializing Godot...");
        Console.WriteLine("cwd: " + System.Environment.CurrentDirectory);

        var projectPath = Engine.ResolveProjectDir();

        // Load game types into the default context before Godot can load a second copy.
        global::twodog.fixture.AssemblyPreloader.PreloadGameAssemblies(projectPath);

        Console.WriteLine("Godot project: " + projectPath);
        Engine = new Engine("twodog.tests", projectPath, WithLogFile(cmdLineArgs)) { CaptureErrors = true };
        // NUnit runs inside testhost.exe, whose manifest is outside the test project's control.
        // Activate common-controls on the owner thread instead of relying on the host executable.
        _controlsContext = OperatingSystem.IsWindows() && !headless
            ? new WindowsControlsContext() : null;
        var context = SynchronizationContext.Current;
        try { GodotInstance = Engine.Start(); }
        catch
        {
            if (OperatingSystem.IsWindows()) _controlsContext?.Dispose();
            throw;
        }
        finally
        {
            // The test runner pumps its own context. Keeping Godot's installed context here
            // strands the first await in an xUnit fixture's InitializeAsync, before tests can pump frames.
            SynchronizationContext.SetSynchronizationContext(context);
        }
        Volatile.Write(ref _displayMode, mode);
        Input = new TestInput(this);
        Console.WriteLine("Godot initialized successfully.");
    }

    private static int _fixtureCount;

    /// <summary>
    /// Test hosts swallow native stdout/stderr; with TWODOG_GODOT_LOG_DIR set, each fixture writes Godot's verbose
    /// log to its own file there.
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

    /// <summary>Input simulation shared by NUnit fixtures and xUnit collections. Use on the engine's owner thread.</summary>
    public TestInput Input { get; }

    /// <summary>
    /// Every error and warning Godot reported since startup and not yet drained. 2dog.xunit and 2dog.nunit fail tests that leave
    /// any behind; tests that expect one consume it with <see cref="GodotErrorLog.Expect"/>.
    /// </summary>
    public GodotErrorLog Errors => Engine.Errors;

    /// <summary>Checks remaining native reports after disposal. Enabled by xUnit's automatic error checks.</summary>
    public bool CheckErrorsOnDispose { get; set; }

    /// <summary>Disposes the owning engine.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        GC.SuppressFinalize(this);

        Console.WriteLine("Shutting down Godot...");
        try { Engine.Dispose(); }
        finally
        {
            if (Engine.Completion.IsCompleted)
            {
                if (OperatingSystem.IsWindows()) _controlsContext?.Dispose();
                _disposed = true;
            }
        }
        Console.WriteLine("Godot shut down successfully.");
        if (CheckErrorsOnDispose)
        {
            var errors = Errors.Drain();
            if (errors.Length > 0)
                throw new GodotErrorException("Godot reported errors during fixture cleanup or engine shutdown:", errors);
        }
    }
}
