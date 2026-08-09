using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using twodog;

namespace twodog.tests.AvaloniaTests;

public class HeadlessTestApp : Application
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<HeadlessTestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

// Control/session attachment lifecycle on the headless Avalonia platform. The engine is
// never started: attachment must work standing alone (a control can be placed before
// Start()), so these run engine-free and in parallel.
public class GodotControlLifecycleTests
{
    private static void Dispatch(Action action)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApp));
        session.Dispatch(action, CancellationToken.None).GetAwaiter().GetResult();
    }

    [Fact]
    public void Attach_ResolvesPresenter_WithoutEngine() => Dispatch(() =>
    {
        using var session = new GodotSession(new GodotSessionOptions { Project = "test" });
        var control = new GodotControl { Session = session };
        var window = new Window { Content = control };
        window.Show();

        Assert.Equal(GodotPresentationMode.Cpu, session.ActiveMode);
        window.Close();
    });

    [Fact]
    public void Detach_ClearsPresenter() => Dispatch(() =>
    {
        using var session = new GodotSession(new GodotSessionOptions { Project = "test" });
        var control = new GodotControl { Session = session };
        var window = new Window { Content = control };
        window.Show();

        window.Content = null;
        Assert.Null(session.ActiveMode);
        window.Close();
    });

    [Fact]
    public void SecondControl_CannotAttach() => Dispatch(() =>
    {
        using var session = new GodotSession(new GodotSessionOptions { Project = "test" });
        var control = new GodotControl { Session = session };
        var window = new Window { Content = control };
        window.Show();

        Assert.Throws<InvalidOperationException>(() => session.Attach(new GodotControl()));
        window.Close();
    });

    [Fact]
    public void Dispose_WithoutStart_RaisesStopped() => Dispatch(() =>
    {
        var session = new GodotSession(new GodotSessionOptions { Project = "test" });
        var stopped = false;
        session.Stopped += (_, _) => stopped = true;
        session.Dispose();
        Assert.True(stopped);
    });

    [Fact]
    public void GpuMode_AttachesOptimistically_BeforeStart() => Dispatch(() =>
    {
        // Support is only knowable once the engine runs; before Start() the GPU presenter
        // attaches optimistically on every platform and reports its mode.
        using var session = new GodotSession(new GodotSessionOptions
        {
            Project = "test",
            PresentationMode = GodotPresentationMode.Gpu,
        });
        var control = new GodotControl { Session = session };
        var window = new Window { Content = control };
        window.Show();

        Assert.Equal(GodotPresentationMode.Gpu, session.ActiveMode);
        window.Close();
    });
}
