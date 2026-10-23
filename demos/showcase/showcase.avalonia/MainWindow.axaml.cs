using Avalonia.Controls;
using Avalonia.Interactivity;
using Godot;
using twodog;
using GodotEngine = Godot.Engine;
using Window = Avalonia.Controls.Window;

namespace showcase;

// The repository's instance of the Avalonia host. The scaffolded template
// (templates/twodog/Company.Product1.avalonia) is the code-only variant of this window
// (templates avoid XAML compilation on purpose) - keep the session lifecycle in sync.
public partial class MainWindow : Window
{
    private readonly string[] _extraArgs;
    private GodotSession? _session;
    private GodotPresentationMode? _reportedMode;

    // Parameterless overload for the XAML runtime loader and previewer.
    public MainWindow() : this([]) { }

    public MainWindow(string[] args)
    {
        _extraArgs = args;
        InitializeComponent();
        SetStatus("Starting", "#F2B84B");
        PauseButton.Click += OnPauseClicked;
        SpeedSlider.ValueChanged += OnSpeedChanged;
    }

    private void SetStatus(string text, string color)
    {
        var brush = Avalonia.Media.Brush.Parse(color);
        StatusLabel.Text = text;
        StatusLabel.Foreground = brush;
        StatusDot.Fill = brush;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        // The previewer instantiates this window through the parameterless constructor;
        // design tooling must load only the XAML UI, never boot the engine.
        if (Design.IsDesignMode) return;
        if (_session is not null) return;

        // Args are forwarded verbatim so CLI flags like --verbose or --quit-after work.
        _session = new GodotSession(new GodotSessionOptions
        {
            Project = "showcase",
            ExtraArgs = _extraArgs,
        });
        _session.QuitRequested += (_, _) => Close();
        // Async GPU setup can still fail after ActiveMode changes; only report a ready presenter.
        void ReportReadyMode()
        {
            if (_session is not { IsPresentationReady: true, ActiveMode: { } mode } || mode == _reportedMode) return;
            _reportedMode = mode;
            ModeLabel.Text = mode == GodotPresentationMode.Gpu ? "GPU presentation" : "CPU readback presentation";
            Console.WriteLine($"2DOG_AVALONIA_MODE={mode}");
        }
        _session.ActiveModeChanged += (_, _) => ReportReadyMode();
        _session.FrameAdvanced += (_, _) => ReportReadyMode();
        GodotView.Session = _session;
        _session.Start();

        // Host-created HUD on the Godot side: shows the render target following the control
        // through resizes, and the canvas scale the project's stretch settings derive from
        // it (display/window/stretch: canvas_items, expand). It borrows the style of the
        // scene's caption and sits in the bottom-right corner, which the scene leaves free.
        var scene = _session.Engine.Tree.CurrentScene;
        var caption = scene.GetNode<Godot.Label>("CenterContainer/GDExtensionProbe");
        var hud = new Godot.Label
        {
            LabelSettings = caption.LabelSettings,
            GrowHorizontal = Godot.Control.GrowDirection.End,
            GrowVertical = Godot.Control.GrowDirection.End,
        };
        hud.AddThemeStyleboxOverride("normal", caption.GetThemeStylebox("normal"));
        scene.AddChild(hud);
        hud.SetAnchorsAndOffsetsPreset(Godot.Control.LayoutPreset.BottomLeft,
            Godot.Control.LayoutPresetMode.KeepSize, margin: 16);
        Vector2I hudSize = default;
        _session.FrameAdvanced += (_, _) =>
        {
            var size = DisplayServer.WindowGetSize();
            if (size == hudSize) return;
            hudSize = size;
            var baseSize = _session.Engine.Tree.Root.ContentScaleSize;
            var canvasScale = Math.Min((float)size.X / baseSize.X, (float)size.Y / baseSize.Y);
            hud.Text = $"Render target: {size.X}x{size.Y} @ canvas scale {canvasScale:0.##}x";
        };

        // The blue cubes spin themselves via SpinningCube._Process (Godot side); the white
        // ones are plain MeshInstance3Ds this host drives per frame, like the desktop host.
        // Skipped while paused: the root's delta keeps ticking when the tree is paused.
        var root = _session.Engine.Tree.Root;
        var whiteCubes = _session.Engine.Tree.CurrentScene
            .GetNode<Node3D>("Flair/WhiteCubes")
            .GetChildren().OfType<Node3D>().ToArray();
        var whiteSpinAxis = new Vector3(1, 1, 0).Normalized();
        _session.FrameAdvanced += (_, _) =>
        {
            if (_session.IsPaused) return;
            var delta = (float)root.GetProcessDeltaTime();
            foreach (var cube in whiteCubes)
                cube.Rotate(whiteSpinAxis, 1.8f * delta);
        };

        PauseButton.IsEnabled = SpeedSlider.IsEnabled = true;
        SetStatus("Running", "#4ADE80");
        // The UI thread is the pump thread, so engine state is safe to touch from
        // event handlers, including the scene tree via _session.Engine.Tree.
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel) return;

        // Tear down before the window dies: the engine must be gone before the
        // ProcessExit libgodot unload runs (see twodog.Engine).
        _session?.Dispose();
        _session = null;
    }

    private void OnPauseClicked(object? sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        _session.IsPaused = !_session.IsPaused;
        PauseButton.Content = _session.IsPaused ? "Resume" : "Pause";
        if (_session.IsPaused)
            SetStatus("Paused", "#F2B84B");
        else
            SetStatus("Running", "#4ADE80");
    }

    private void OnSpeedChanged(object? sender, RoutedEventArgs e)
    {
        SpeedLabel.Text = $"{SpeedSlider.Value:0.00}×";
        if (_session?.IsStarted == true)
            GodotEngine.TimeScale = SpeedSlider.Value;
    }
}
