using System.Globalization;
using System.Runtime.InteropServices;
using Godot;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Button = Microsoft.UI.Xaml.Controls.Button;
using Engine = twodog.Engine;
using Grid = Microsoft.UI.Xaml.Controls.Grid;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls.Primitives;
using Slider = Microsoft.UI.Xaml.Controls.Slider;
using VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment;
using Window = Microsoft.UI.Xaml.Window;

// Embeds the showcase inside a WinUI 3 window via Godot's `--wid` embedding (the same mechanism the
// Godot editor uses for its embedded game window): Godot creates its main window as a borderless popup
// owned by this window, and the host drives its geometry and pumps frames from the UI thread.
// This is the repo's instance of the WinUI host the 2dog tool scaffolds
// (templates/twodog/Company.Product1.winui) - keep the two in sync.
namespace showcase;

internal sealed class MainWindow : Window
{
    private static readonly Windows.UI.Color Accent = Rgb(0xA8, 0xE6, 0x6A);
    private static readonly Windows.UI.Color Muted = Rgb(0x9A, 0xA7, 0xB8);
    private static readonly Windows.UI.Color Starting = Rgb(0xF2, 0xB8, 0x4B);
    private static readonly Windows.UI.Color Running = Rgb(0x4A, 0xDE, 0x80);

    private readonly string[] _extraArgs;
    private readonly Grid _gamePanel;
    private readonly Button _pauseButton;
    private readonly Slider _speed;
    private readonly TextBlock _speedValue;
    private readonly TextBlock _status;
    private readonly nint _hwnd;

    private Engine? _engine;
    private GodotInstance? _instance;
    private nint _godotHwnd;
    private bool _paused;
    private bool _closeRequested;
    private Node3D[] _whiteCubes = [];
    private static readonly Vector3 WhiteSpinAxis = new Vector3(1, 1, 0).Normalized();

    public MainWindow(string[] extraArgs)
    {
        _extraArgs = extraArgs;

        Title = "2dog showcase — WinUI 3 host";
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        // ResizeClient takes physical pixels; scale the 96-DPI layout size to the window's monitor.
        var dpiScale = GetDpiForWindow(_hwnd) / 96.0;
        AppWindow.ResizeClient(new SizeInt32((int)(1152 * dpiScale), (int)(704 * dpiScale)));

        var dark = 1;
        _ = DwmSetWindowAttribute(_hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

        // Lightweight styling: these keys replace the OS accent color in the default templates.
        _pauseButton = new Button
        {
            Content = "Pause", IsEnabled = false, Width = 96, Height = 32, Margin = new Thickness(16, 0, 16, 0),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, CornerRadius = new CornerRadius(6),
        };
        SetBrushes(_pauseButton.Resources, Accent, "ButtonBackground", "ButtonBackgroundDisabled");
        SetBrushes(_pauseButton.Resources, Rgb(0x74, 0xB0, 0xFF), "ButtonBackgroundPointerOver");
        SetBrushes(_pauseButton.Resources, Rgb(0x3F, 0x86, 0xE8), "ButtonBackgroundPressed");
        SetBrushes(_pauseButton.Resources, Rgb(0x0B, 0x12, 0x20), "ButtonForeground", "ButtonForegroundPointerOver",
            "ButtonForegroundPressed", "ButtonForegroundDisabled");
        SetBrushes(_pauseButton.Resources, Microsoft.UI.Colors.Transparent, "ButtonBorderBrush",
            "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed", "ButtonBorderBrushDisabled");
        _pauseButton.Click += OnPauseClicked;

        _speed = new Slider
        {
            Minimum = 0, Maximum = 3, StepFrequency = 0.05, Value = 1, Width = 160, IsEnabled = false,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0),
        };
        SetBrushes(_speed.Resources, Accent, "SliderTrackValueFill", "SliderThumbBackground");
        SetBrushes(_speed.Resources, Rgb(0x74, 0xB0, 0xFF), "SliderTrackValueFillPointerOver",
            "SliderThumbBackgroundPointerOver");
        SetBrushes(_speed.Resources, Rgb(0x3F, 0x86, 0xE8), "SliderTrackValueFillPressed",
            "SliderThumbBackgroundPressed");
        AutomationProperties.SetName(_speed, "Game speed");
        _speed.ValueChanged += OnSpeedChanged;
        _speedValue = ToolbarText("1.00×", Microsoft.UI.Colors.White);
        _speedValue.MinWidth = 44;
        _status = ToolbarText("", Starting);

        var title = ToolbarText("2dog showcase", Microsoft.UI.Colors.White);
        title.FontSize = 15;
        title.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
        var toolbar = new Grid
        {
            Height = 56, Padding = new Thickness(16, 0, 16, 0), ColumnSpacing = 12,
            Background = new SolidColorBrush(Rgb(0x12, 0x18, 0x21)),
        };
        UIElement[] items =
        [
            title, ToolbarText("WinUI 3 host", Muted), _status, new Border(),
            ToolbarText("Game speed", Microsoft.UI.Colors.White), _speed, _speedValue, _pauseButton,
        ];
        for (var column = 0; column < items.Length; column++)
        {
            toolbar.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = column == 3 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto,
            });
            Grid.SetColumn((FrameworkElement)items[column], column);
            toolbar.Children.Add(items[column]);
        }

        // The toolbar sits above the game: Godot's popup always draws above the window's client
        // area, so XAML controls must not overlap the embed rectangle.
        _gamePanel = new Grid { Background = new SolidColorBrush(Rgb(0x1E, 0x26, 0x33)) };

        var root = new Grid { RequestedTheme = ElementTheme.Dark };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(toolbar, 0);
        Grid.SetRow(_gamePanel, 1);
        root.Children.Add(toolbar);
        root.Children.Add(_gamePanel);
        Content = root;
        SetStatus("● Starting", Starting, running: false);

        // The engine starts once layout has given the panel its size.
        _gamePanel.Loaded += OnPanelLoaded;
        // Tear down while the owner HWND still exists: Godot self-closes its window when the owner
        // disappears, and the engine must be gone before the ProcessExit libgodot unload runs.
        AppWindow.Closing += OnClosing;
        Closed += (_, _) => ShutdownEngine();
    }

    private void OnPanelLoaded(object sender, RoutedEventArgs e)
    {
        _gamePanel.Loaded -= OnPanelLoaded;

        // `--wid` wants a top-level owner window, so pass the window's handle; geometry is fixed up
        // below because `--position` goes through Godot's virtual multi-monitor origin math. XAML
        // lengths are DIPs while Godot works in pixels, hence the rasterization scale.
        var scale = _gamePanel.XamlRoot.RasterizationScale;
        _engine = new Engine("showcase", args:
        [
            "--wid", _hwnd.ToInt64().ToString(CultureInfo.InvariantCulture),
            "--resolution",
            $"{(int)Math.Round(_gamePanel.ActualWidth * scale)}x{(int)Math.Round(_gamePanel.ActualHeight * scale)}",
            "--position", "0,0",
            .. _extraArgs,
        ]);
        _instance = _engine.Start();
        _godotHwnd = (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        SyncGodotBounds();

        // The owned popup lives in screen coordinates and does not track the window on its own.
        _gamePanel.SizeChanged += (_, _) => SyncGodotBounds();
        _gamePanel.XamlRoot.Changed += (_, _) => SyncGodotBounds();
        AppWindow.Changed += (_, change) =>
        {
            if (change.DidPositionChange) SyncGodotBounds();
        };

        // The blue cubes spin themselves via SpinningCube._Process (Godot side); the white
        // ones are plain MeshInstance3Ds this host drives per frame, like the desktop host.
        _whiteCubes = _engine.Tree.CurrentScene
            .GetNode<Node3D>("Flair/WhiteCubes")
            .GetChildren().OfType<Node3D>().ToArray();

        SetStatus("● Running", Running, running: true);
        PumpFrame();
    }

    private static Windows.UI.Color Rgb(byte r, byte g, byte b) => Windows.UI.Color.FromArgb(0xFF, r, g, b);

    private static void SetBrushes(ResourceDictionary resources, Windows.UI.Color color, params string[] keys)
    {
        foreach (var key in keys)
            resources[key] = new SolidColorBrush(color);
    }

    private static TextBlock ToolbarText(string text, Windows.UI.Color color) => new()
    {
        Text = text, Foreground = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center,
    };

    private void SetStatus(string text, Windows.UI.Color color, bool running)
    {
        _status.Text = text;
        _status.Foreground = new SolidColorBrush(color);
        _pauseButton.IsEnabled = _speed.IsEnabled = running;
        _pauseButton.Opacity = running ? 1 : 0.45;
    }

    private void OnSpeedChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        _speedValue.Text = $"{e.NewValue:0.00}×";
        if (_engine is not null)
            Godot.Engine.TimeScale = e.NewValue;
    }

    // WinUI has no idle event; a self-reposting low-priority dispatcher callback plays that role:
    // one engine frame per pass, so normal-priority input and layout always interleave. Update()
    // also runs DisplayServer::process_events(), which dispatches this thread's queued messages
    // without XAML's preprocessing, like the WinForms host's idle loop; only closing needs care (OnClosing).
    private void PumpFrame()
    {
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (_instance is null || _engine is not { } engine)
                return;
            // Update() returns false when the engine wants to quit (SceneTree.Quit(), --quit-after N, ...);
            // a window close deferred by OnClosing ends here too.
            if (!engine.Update() || _closeRequested)
            {
                ShutdownEngine();
                Close();
                return;
            }
            // Skipped while paused: the root's delta keeps ticking when the tree is paused.
            if (!_paused)
            {
                var delta = (float)_engine!.Tree.Root.GetProcessDeltaTime();
                foreach (var cube in _whiteCubes)
                    cube.Rotate(WhiteSpinAxis, 1.8f * delta);
            }
            PumpFrame();
        });
    }

    // Godot refuses window_set_size for embedded windows, so the host drives the popup with raw
    // SetWindowPos, exactly like the editor's embedded game window; Godot's WndProc adopts the size.
    private void SyncGodotBounds()
    {
        if (_godotHwnd == 0)
            return;
        var scale = _gamePanel.XamlRoot.RasterizationScale;
        var origin = _gamePanel.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
        var client = default(NativePoint);
        ClientToScreen(_hwnd, ref client);
        SetWindowPos(_godotHwnd, 0,
            client.X + (int)Math.Round(origin.X * scale),
            client.Y + (int)Math.Round(origin.Y * scale),
            (int)Math.Round(_gamePanel.ActualWidth * scale),
            (int)Math.Round(_gamePanel.ActualHeight * scale),
            SWP_NOZORDER | SWP_NOACTIVATE);
    }

    // The UI thread is the pump thread, so game state is safe to touch from event handlers,
    // including the scene tree via _engine.Tree.
    private void OnPauseClicked(object sender, RoutedEventArgs e)
    {
        _paused = !_paused;
        // SceneTree.Paused is the actual gameplay pause. GodotInstance.Pause/Resume only raises
        // the mobile-style application-lifecycle notification, which the tree merely propagates
        // to nodes; sent as well so game code listening for it (autosaves etc.) stays informed.
        _engine!.Tree.Paused = _paused;
        if (_paused)
            _instance!.Pause();
        else
            _instance!.Resume();
        _pauseButton.Content = _paused ? "Resume" : "Pause";
        SetStatus(_paused ? "● Paused" : "● Running", _paused ? Starting : Running, running: true);
    }

    // Update() dispatches this thread's messages, so a close usually arrives inside an engine frame. A window
    // destroyed there never wakes XAML's own message loop, which would keep running without windows. Closing
    // resumes from the pump instead, between frames.
    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_engine is null)
            return;
        args.Cancel = true;
        _closeRequested = true;
    }

    private void ShutdownEngine()
    {
        SetStatus("● Stopped", Muted, running: false);
        _instance = null;
        _engine?.Dispose();
        _engine = null;
        _godotHwnd = 0;
    }

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(nint hWnd, ref NativePoint point);
}
