using Microsoft.Maui.Controls.Shapes;
using twodog.Maui;

namespace showcase.Maui;

// The game fills the page and a floating MAUI card controls it. Input outside the card reaches Godot.
public sealed class MainPage : ContentPage
{
    private static readonly Color Accent = Color.FromArgb("#579EFF");
    private static readonly Color Muted = Color.FromArgb("#9AA7B8");
    private static readonly Color Starting = Color.FromArgb("#F2B84B");
    private static readonly Color Running = Color.FromArgb("#4ADE80");
    private static readonly Color Stopped = Color.FromArgb("#9AA7B8");
    private static readonly Color Error = Color.FromArgb("#F87171");

    private readonly GodotView _game = new() { Project = "showcase" };
    private readonly Ellipse _statusDot = new()
    {
        WidthRequest = 8, HeightRequest = 8, VerticalOptions = LayoutOptions.Center,
    };
    private readonly Label _status = new() { FontSize = 12, VerticalOptions = LayoutOptions.Center };
    private readonly Label _mode = new() { Text = "Waiting for Godot", FontSize = 12, TextColor = Muted };
    private readonly Label _fps = new() { FontSize = 12, TextColor = Muted, HorizontalOptions = LayoutOptions.End };
    private readonly Label _error = new() { FontSize = 12, TextColor = Error, IsVisible = false };
    private readonly Label _speedValue = new()
    {
        Text = "1.00×", FontSize = 13, TextColor = Colors.White, HorizontalOptions = LayoutOptions.End,
    };
    private readonly Slider _speed = new()
    {
        Minimum = 0, Maximum = 3, Value = 1,
        MinimumTrackColor = Accent, MaximumTrackColor = Color.FromArgb("#3A4656"), ThumbColor = Accent,
    };
    private readonly Button _pause = new()
    {
        Text = "Pause", FontSize = 14, FontAttributes = FontAttributes.Bold, HeightRequest = 40, CornerRadius = 8,
        BackgroundColor = Accent, TextColor = Color.FromArgb("#0B1220"),
    };
    private bool _running, _paused;

    public MainPage()
    {
        BackgroundColor = Color.FromArgb("#202B36");
        SetStatus("Starting", Starting, running: false);

        _game.Started += (_, _) =>
        {
            SetStatus("Running", Running, running: true);
            _game.Dispatch(tree =>
            {
                GodotApiSmoke.RunAll(tree);
                // The blue and red cubes spin themselves in Godot; the host drives the white ones.
                var cubes = tree.CurrentScene.GetNode<global::Godot.Node3D>("Flair/WhiteCubes")
                    .GetChildren().OfType<global::Godot.Node3D>().ToArray();
                var axis = new global::Godot.Vector3(1, 1, 0).Normalized();
                tree.ProcessFrame += () =>
                {
                    if (tree.Paused) return;
                    foreach (var cube in cubes) cube.Rotate(axis, 1.8f * (float)tree.Root.GetProcessDeltaTime());
                };
                global::Godot.GD.Print("2DOG_MAUI_SHOWCASE_SMOKE_PASSED");
            });
            Dispatcher.StartTimer(TimeSpan.FromMilliseconds(500), PollFps);
        };
        _game.PresentationModeChanged += (_, _) => _mode.Text = _game.ActivePresentationMode switch
        {
            GodotPresentationMode.Gpu => "GPU presentation",
            GodotPresentationMode.Cpu => "CPU readback presentation",
            _ => "Not presenting",
        };
        _game.Failed += (_, exception) =>
        {
            SetStatus("Failed", Error, running: false);
            _error.Text = exception.Message;
            _error.IsVisible = true;
            Console.Error.WriteLine($"2DOG_MAUI_SHOWCASE_SMOKE_FAILED: {exception}");
        };
        _game.QuitRequested += (_, _) => SetStatus("Stopped", Stopped, running: false);

        _pause.Clicked += (_, _) =>
        {
            _paused = !_paused;
            var paused = _paused;
            _game.Dispatch(tree => tree.Paused = paused);
            _pause.Text = paused ? "Resume" : "Pause";
            SetStatus(paused ? "Paused" : "Running", paused ? Starting : Running, running: true);
        };
        _speed.ValueChanged += (_, args) =>
        {
            _speedValue.Text = $"{args.NewValue:0.00}×";
            if (_running) _game.Dispatch(_ => global::Godot.Engine.TimeScale = args.NewValue);
        };

        // The game runs edge to edge; the overlay keeps the card clear of system bars and cutouts.
        SafeAreaEdges = SafeAreaEdges.None;
        var overlay = new Grid
        {
            SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.Container),
            InputTransparent = true, CascadeInputTransparent = false, Children = { CreateCard() },
        };
        Content = new Grid { Children = { _game, overlay } };
    }

    private View CreateCard()
    {
        var chip = new Border
        {
            StrokeThickness = 0, Padding = new Thickness(10, 4), BackgroundColor = Color.FromArgb("#1AFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = 12 }, VerticalOptions = LayoutOptions.Center,
            Content = new HorizontalStackLayout { Spacing = 6, Children = { _statusDot, _status } },
        };
        var header = new Grid
        {
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) },
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 2,
                    Children =
                    {
                        new Label
                        {
                            Text = "2dog showcase", FontSize = 17, FontAttributes = FontAttributes.Bold,
                            TextColor = Colors.White,
                        },
                        new Label { Text = "MAUI controls over Godot", FontSize = 12, TextColor = Muted },
                    },
                },
            },
        };
        header.Add(chip, 1);

        var speedHeader = new Grid
        {
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) },
            Children = { new Label { Text = "Game speed", FontSize = 13, TextColor = Colors.White } },
        };
        speedHeader.Add(_speedValue, 1);

        var footer = new Grid
        {
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, Children = { _mode },
        };
        footer.Add(_fps, 1);

        return new Border
        {
            WidthRequest = 280, Margin = 16, Padding = 16,
            HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Start,
            BackgroundColor = Color.FromArgb("#E6121821"), Stroke = Color.FromArgb("#2EFFFFFF"), StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Shadow = new Shadow { Brush = Brush.Black, Opacity = 0.35f, Radius = 24, Offset = new Point(0, 8) },
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Children =
                {
                    header,
                    new BoxView { HeightRequest = 1, Color = Color.FromArgb("#24FFFFFF") },
                    new VerticalStackLayout { Spacing = 0, Children = { speedHeader, _speed } },
                    _pause,
                    _error,
                    footer,
                },
            },
        };
    }

    private void SetStatus(string text, Color color, bool running)
    {
        _running = running;
        _status.Text = text;
        _status.TextColor = color;
        _statusDot.Fill = color;
        _pause.IsEnabled = _speed.IsEnabled = running;
        _pause.Opacity = _speed.Opacity = running ? 1 : 0.45;
        if (!running) _fps.Text = "";
    }

    private bool PollFps()
    {
        // The view clears its presentation mode as soon as the engine stops, before Stopped/Failed arrive.
        if (!_running || _game.ActivePresentationMode is null) return false;
        _game.Dispatch(_ =>
        {
            var fps = global::Godot.Engine.GetFramesPerSecond();
            Dispatcher.Dispatch(() => { if (_running) _fps.Text = $"{fps:0} fps"; });
        });
        return true;
    }
}
