using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using twodog;
using GodotEngine = Godot.Engine;

namespace showcase;

// The repository's instance of the Avalonia host. The scaffolded template
// (templates/twodog/Company.Product1.avalonia) is the code-only variant of this window
// (templates avoid XAML compilation on purpose) - keep the session lifecycle in sync.
public partial class MainWindow : Window
{
    private readonly string[] _extraArgs;
    private GodotSession? _session;
    private DispatcherTimer? _fpsTimer;

    // Parameterless overload for the XAML runtime loader and previewer.
    public MainWindow() : this([]) { }

    public MainWindow(string[] args)
    {
        _extraArgs = args;
        InitializeComponent();
        PauseButton.Click += OnPauseClicked;
        SpeedSlider.ValueChanged += OnSpeedChanged;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_session is not null) return;

        // Args are forwarded verbatim so CLI flags like --verbose or --quit-after work.
        _session = new GodotSession(new GodotSessionOptions
        {
            Project = "showcase",
            ExtraArgs = _extraArgs,
        });
        _session.QuitRequested += (_, _) => Close();
        GodotView.Session = _session;
        _session.Start();

        PauseButton.IsEnabled = true;
        // The UI thread is the pump thread, so engine state is safe to touch from
        // event handlers and timers, including the scene tree via _session.Engine.Tree.
        _fpsTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background,
            (_, _) => FpsLabel.Text = $"{GodotEngine.GetFramesPerSecond():0} fps ({_session?.ActiveMode})");
        _fpsTimer.Start();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Tear down before the window dies: the engine must be gone before the
        // ProcessExit libgodot unload runs (see twodog.Engine).
        _fpsTimer?.Stop();
        _session?.Dispose();
        _session = null;
        base.OnClosing(e);
    }

    private void OnPauseClicked(object? sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        if (_session.IsPaused) _session.Resume();
        else _session.Pause();
        PauseButton.Content = _session.IsPaused ? "Resume" : "Pause";
    }

    private void OnSpeedChanged(object? sender, RoutedEventArgs e)
    {
        if (_session?.IsStarted == true)
            GodotEngine.TimeScale = SpeedSlider.Value;
    }
}
