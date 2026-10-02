using System;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace twodog;

// Use managed stderr, not Godot APIs: these callbacks can run on the finalizer
// thread, before Godot starts, or after its native instance has been destroyed.
internal static class WebExceptionDiagnostics
{
    private static readonly object Sync = new();
    private static bool _installed;
    private static bool _logFirstChanceExceptions;

    [ThreadStatic]
    private static bool _reporting;

    internal static void Install()
    {
        lock (Sync)
        {
            if (_installed) return;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            _installed = true;
        }
    }

    internal static bool LogFirstChanceExceptions
    {
        get { lock (Sync) return _logFirstChanceExceptions; }
        set
        {
            lock (Sync)
            {
                if (_logFirstChanceExceptions == value) return;
                if (value)
                    AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
                else
                    AppDomain.CurrentDomain.FirstChanceException -= OnFirstChanceException;
                _logFirstChanceExceptions = value;
            }
        }
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        Report(e.IsTerminating ? "Unhandled managed exception (runtime terminating)" : "Unhandled managed exception",
            e.ExceptionObject);

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) =>
        // Reporting must not change the host's exception escalation policy.
        Report("Unobserved task exception", e.Exception);

    private static void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs e) =>
        Report("First-chance managed exception", e.Exception, captureCurrentStack: true);

    private static void Report(string kind, object exception, bool captureCurrentStack = false)
    {
        // Formatting or writing an exception can itself throw. In first-chance
        // mode that re-enters this callback before the catch below executes.
        if (_reporting) return;
        _reporting = true;
        try
        {
            Console.Error.WriteLine($"2dog: {kind}\n{exception}");
            // Mono raises FirstChanceException before attaching the throw-site
            // stack to the exception. Capture it while that stack is still live.
            if (captureCurrentStack && exception is Exception managed && string.IsNullOrEmpty(managed.StackTrace))
                Console.Error.WriteLine(System.Environment.StackTrace);
        }
        catch
        {
            // A diagnostic must never replace the original failure.
        }
        finally
        {
            _reporting = false;
        }
    }
}
