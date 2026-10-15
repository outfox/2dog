using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using twodog;

// Composites the game's viewport inside this window through 2dog.avalonia's GodotControl.
// Unlike native window embedding, Avalonia controls can render on top of the running game.
// (In the 2dog repository, demos/showcase/showcase.avalonia is the XAML variant of this
// window; the two are kept in sync by hand.)
internal sealed class MainWindow : Window
{
    private static readonly IBrush Muted = Brush.Parse("#9AA7B8");

    private readonly string[] _extraArgs;
    private readonly GodotControl _godotView;
    private readonly Button _pauseButton;
    private readonly Ellipse _statusDot = new() { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private GodotSession? _session;

    public MainWindow(string[] extraArgs)
    {
        _extraArgs = extraArgs;

        Title = "TPLRAWNAME — Avalonia host";
        Width = 1152;
        Height = 720;
        MinWidth = 480;
        MinHeight = 320;
        Background = Brush.Parse("#1E2633");
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _godotView = new GodotControl();
        _pauseButton = new Button
        {
            Content = "Pause",
            IsEnabled = false,
            Height = 40,
            CornerRadius = new CornerRadius(8),
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        // Fluent's lightweight styling keys: an accent button whatever the OS accent color is.
        foreach (var (key, color) in new[]
                 {
                     ("ButtonBackground", "#579EFF"), ("ButtonBackgroundPointerOver", "#74B0FF"),
                     ("ButtonBackgroundPressed", "#3F86E8"), ("ButtonBackgroundDisabled", "#579EFF"),
                     ("ButtonForeground", "#0B1220"), ("ButtonForegroundPointerOver", "#0B1220"),
                     ("ButtonForegroundPressed", "#0B1220"), ("ButtonForegroundDisabled", "#0B1220"),
                 })
            _pauseButton.Resources[key] = Brush.Parse(color);
        _pauseButton.Click += OnPauseClicked;

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock
                {
                    Text = "TPLRAWNAME", FontSize = 17, FontWeight = FontWeight.Bold, Foreground = Brushes.White,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
                new TextBlock { Text = "Avalonia controls over Godot", FontSize = 12, Foreground = Muted },
            },
        });
        var chip = new Border
        {
            Padding = new Thickness(10, 4),
            CornerRadius = new CornerRadius(12),
            Background = Brush.Parse("#1AFFFFFF"),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6, Children = { _statusDot, _status },
            },
        };
        Grid.SetColumn(chip, 1);
        header.Children.Add(chip);

        // The overlay floats over the running game - the point of this host.
        var overlay = new Border
        {
            Width = 280,
            Padding = new Thickness(16),
            Margin = new Thickness(16),
            Background = Brush.Parse("#E6121821"),
            BorderBrush = Brush.Parse("#2EFFFFFF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            BoxShadow = BoxShadows.Parse("0 8 24 0 #59000000"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new StackPanel { Spacing = 14, Children = { header, _pauseButton } },
        };

        Content = new Panel { Children = { _godotView, overlay } };
        SetStatus("Starting", "#F2B84B");
    }

    private void SetStatus(string text, string color)
    {
        var brush = Brush.Parse(color);
        _status.Text = text;
        _status.Foreground = brush;
        _statusDot.Fill = brush;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_session is not null) return;

        // Command-line arguments are forwarded to Godot (--quit-after, --verbose, ...).
        _session = new GodotSession(new GodotSessionOptions
        {
            Project = "TPLRAWNAME",
            ExtraArgs = _extraArgs,
        });
        _session.QuitRequested += (_, _) => Close();
        _godotView.Session = _session;
        _session.Start();
        _pauseButton.IsEnabled = true;
        SetStatus("Running", "#4ADE80");
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
        if (_session.IsPaused)
            SetStatus("Paused", "#F2B84B");
        else
            SetStatus("Running", "#4ADE80");
    }
}
