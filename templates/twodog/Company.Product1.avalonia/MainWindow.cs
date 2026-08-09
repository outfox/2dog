using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using twodog;

// Composites the game's viewport inside this window through 2dog.avalonia's GodotControl.
// Unlike native window embedding, Avalonia controls can render on top of the running game.
// (In the 2dog repository, demos/showcase/showcase.avalonia is the XAML variant of this
// window; the two are kept in sync by hand.)
internal sealed class MainWindow : Window
{
    private readonly string[] _extraArgs;
    private readonly GodotControl _godotView;
    private readonly Button _pauseButton;
    private GodotSession? _session;

    public MainWindow(string[] extraArgs)
    {
        _extraArgs = extraArgs;

        Title = "Company.Product1 — Avalonia host";
        Width = 1152;
        Height = 720;
        MinWidth = 480;
        MinHeight = 320;

        _godotView = new GodotControl();
        _pauseButton = new Button
        {
            Content = "Pause",
            IsEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _pauseButton.Click += OnPauseClicked;

        // The overlay floats over the running game - the point of this host.
        var overlay = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#B0202028")),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(16),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new StackPanel { Spacing = 8, MinWidth = 160, Children = { _pauseButton } },
        };

        Content = new Panel { Children = { _godotView, overlay } };
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_session is not null) return;

        // Command-line arguments are forwarded to Godot (--quit-after, --verbose, ...).
        _session = new GodotSession(new GodotSessionOptions
        {
            Project = "Company.Product1",
            ExtraArgs = _extraArgs,
        });
        _session.QuitRequested += (_, _) => Close();
        _godotView.Session = _session;
        _session.Start();
        _pauseButton.IsEnabled = true;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel) return;

        // Tear down before the window dies: the engine must be gone before the process
        // exits (2dog unloads libgodot during process exit on Windows).
        _session?.Dispose();
        _session = null;
    }

    private void OnPauseClicked(object? sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        _session.IsPaused = !_session.IsPaused;
        _pauseButton.Content = _session.IsPaused ? "Resume" : "Pause";
    }
}
