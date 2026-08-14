using System.Globalization;
using System.Runtime.InteropServices;
using Godot;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Button = Microsoft.UI.Xaml.Controls.Button;
using Engine = twodog.Engine;
using Grid = Microsoft.UI.Xaml.Controls.Grid;
using Orientation = Microsoft.UI.Xaml.Controls.Orientation;
using Window = Microsoft.UI.Xaml.Window;

// Embeds the game inside a WinUI 3 window via Godot's `--wid` embedding (the same mechanism the
// Godot editor uses for its embedded game window): Godot creates its main window as a borderless popup
// owned by this window, and the host drives its geometry and pumps frames from the UI thread.
internal sealed class MainWindow : Window
{
    private readonly string[] _extraArgs;
    private readonly Grid _gamePanel;
    private readonly Button _pauseButton;
    private readonly nint _hwnd;

    private Engine? _engine;
    private GodotInstance? _instance;
    private nint _godotHwnd;
    private bool _paused;

    public MainWindow(string[] extraArgs)
    {
        _extraArgs = extraArgs;

        Title = "Company.Product1 — WinUI 3 host";
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        AppWindow.ResizeClient(new SizeInt32(1152, 688));

        _pauseButton = new Button { Content = "Pause", IsEnabled = false };
        _pauseButton.Click += OnPauseClicked;

        var buttonStrip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Padding = new Thickness(4),
            Spacing = 4,
        };
        buttonStrip.Children.Add(_pauseButton);

        // The panel only defines the embed rectangle; Godot's popup always draws above the window's
        // client area, so XAML controls must not overlap it.
        _gamePanel = new Grid { Background = new SolidColorBrush(Microsoft.UI.Colors.Black) };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(buttonStrip, 0);
        Grid.SetRow(_gamePanel, 1);
        root.Children.Add(buttonStrip);
        root.Children.Add(_gamePanel);
        Content = root;

        // The engine starts once layout has given the panel its size.
        _gamePanel.Loaded += OnPanelLoaded;
        // Tear down while the owner HWND still exists: Godot self-closes its window when the owner
        // disappears, and the engine must be gone before the ProcessExit libgodot unload runs.
        AppWindow.Closing += (_, _) => ShutdownEngine();
        Closed += (_, _) => ShutdownEngine();
    }

    private void OnPanelLoaded(object sender, RoutedEventArgs e)
    {
        _gamePanel.Loaded -= OnPanelLoaded;

        // `--wid` wants a top-level owner window, so pass the window's handle; geometry is fixed up
        // below because `--position` goes through Godot's virtual multi-monitor origin math. XAML
        // lengths are DIPs while Godot works in pixels, hence the rasterization scale.
        var scale = _gamePanel.XamlRoot.RasterizationScale;
        _engine = new Engine("Company.Product1", args:
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

        _pauseButton.IsEnabled = true;
        PumpFrame();
    }

    // WinUI has no idle event; a self-reposting low-priority dispatcher callback plays that role:
    // one engine frame per pass, so normal-priority input and layout always interleave. Iteration()
    // also runs DisplayServer::process_events(), which dispatches this thread's queued messages
    // without XAML's preprocessing; harmless, exactly like a classic WinForms idle loop.
    private void PumpFrame()
    {
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (_instance is not { } instance)
                return;
            // Iteration() returns true when the engine wants to quit (SceneTree.Quit(), --quit-after N, ...).
            if (instance.Iteration())
            {
                ShutdownEngine();
                Close();
                return;
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
    }

    private void ShutdownEngine()
    {
        _pauseButton.IsEnabled = false;
        _instance?.Dispose();
        _instance = null;
        _engine?.Dispose();
        _engine = null;
        _godotHwnd = 0;
    }

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

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
