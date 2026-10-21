using Microsoft.Maui.Controls.Shapes;
using twodog.Maui;

namespace Company.Product1.Maui;

// The game fills the page and a floating MAUI card controls it. Input outside the card reaches Godot.
public sealed class MainPage : ContentPage
{
    private static readonly Color Accent = Color.FromArgb("#A8E66A");
    private static readonly Color Muted = Color.FromArgb("#9AA7B8");

    private readonly GodotView _game = new() { Project = "TPLRAWNAME" };
    private readonly Ellipse _statusDot = new()
    {
        WidthRequest = 8, HeightRequest = 8, VerticalOptions = LayoutOptions.Center,
    };
    private readonly Label _status = new() { FontSize = 12, VerticalOptions = LayoutOptions.Center };
    private readonly Label _error = new() { FontSize = 12, TextColor = Colors.IndianRed, IsVisible = false };
    private readonly Button _pause = new()
    {
        Text = "Pause", FontAttributes = FontAttributes.Bold, HeightRequest = 40, CornerRadius = 8,
        BackgroundColor = Accent, TextColor = Color.FromArgb("#0B1220"),
    };
    private bool _paused;

    public MainPage()
    {
        BackgroundColor = Color.FromArgb("#202B36");
        SetStatus("Starting", Color.FromArgb("#F2B84B"), running: false);
        _game.Started += (_, _) => SetStatus("Running", Color.FromArgb("#4ADE80"), running: true);
        _game.QuitRequested += (_, _) => SetStatus("Stopped", Muted, running: false);
        _game.Failed += (_, exception) =>
        {
            SetStatus("Failed", Colors.IndianRed, running: false);
            _error.Text = exception.Message;
            _error.IsVisible = true;
        };
        _pause.Clicked += (_, _) =>
        {
            _paused = !_paused;
            var paused = _paused;
            // Dispatch runs on Godot's thread; never touch the scene tree from MAUI handlers directly.
            _game.Dispatch(tree => tree.Paused = paused);
            _pause.Text = paused ? "Resume" : "Pause";
            SetStatus(paused ? "Paused" : "Running", Color.FromArgb(paused ? "#F2B84B" : "#4ADE80"), running: true);
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
                            Text = "TPLRAWNAME", FontSize = 17, FontAttributes = FontAttributes.Bold,
                            TextColor = Colors.White, LineBreakMode = LineBreakMode.TailTruncation,
                        },
                        new Label { Text = "Godot inside .NET MAUI", FontSize = 12, TextColor = Muted },
                    },
                },
            },
        };
        header.Add(new Border
        {
            StrokeThickness = 0, Padding = new Thickness(10, 4), BackgroundColor = Color.FromArgb("#1AFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = 12 }, VerticalOptions = LayoutOptions.Center,
            Content = new HorizontalStackLayout { Spacing = 6, Children = { _statusDot, _status } },
        }, 1);

        var card = new Border
        {
            WidthRequest = 260, Margin = 16, Padding = 16,
            HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Start,
            BackgroundColor = Color.FromArgb("#E6121821"), Stroke = Color.FromArgb("#2EFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Shadow = new Shadow { Brush = Brush.Black, Opacity = 0.35f, Radius = 24, Offset = new Point(0, 8) },
            Content = new VerticalStackLayout { Spacing = 14, Children = { header, _pause, _error } },
        };

        // The game runs edge to edge; the overlay keeps the card clear of system bars and cutouts.
        SafeAreaEdges = SafeAreaEdges.None;
        var overlay = new Grid
        {
            SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.Container),
            InputTransparent = true, CascadeInputTransparent = false, Children = { card },
        };
        Content = new Grid { Children = { _game, overlay } };
    }

    private void SetStatus(string text, Color color, bool running)
    {
        _status.Text = text;
        _status.TextColor = color;
        _statusDot.Fill = color;
        _pause.IsEnabled = running;
        _pause.Opacity = running ? 1 : 0.45;
    }
}
