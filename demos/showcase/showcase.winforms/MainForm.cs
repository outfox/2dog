using System.Globalization;
using System.Runtime.InteropServices;
using Godot;
using Button = System.Windows.Forms.Button;
using Color = System.Drawing.Color;
using Engine = twodog.Engine;
using Panel = System.Windows.Forms.Panel;

// Embeds the showcase inside a WinForms window via Godot's `--wid` embedding (the same mechanism the
// Godot editor uses for its embedded game window): Godot creates its main window as a borderless popup
// owned by this form, and the host drives its geometry and pumps frames from the UI thread.
// This is the repo's instance of the WinForms host the 2dog tool scaffolds
// (templates/twodog/Company.Product1.winforms) - keep the two in sync.
namespace showcase;

internal sealed class MainForm : Form
{
    private readonly string[] _extraArgs;
    private readonly Panel _gamePanel;
    private readonly Button _pauseButton;

    private Engine? _engine;
    private GodotInstance? _instance;
    private nint _godotHwnd;
    private bool _paused;

    public MainForm(string[] extraArgs)
    {
        _extraArgs = extraArgs;

        Text = "2dog showcase — WinForms host";
        ClientSize = new Size(1152, 688);
        MinimumSize = new Size(480, 320);

        _pauseButton = new Button { Text = "Pause", AutoSize = true, Enabled = false };
        _pauseButton.Click += OnPauseClicked;

        var buttonStrip = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
        buttonStrip.Controls.Add(_pauseButton);

        // The panel only defines the embed rectangle; Godot's popup always draws above the form's
        // client area, so form controls must not overlap it.
        _gamePanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };

        Controls.Add(_gamePanel);
        Controls.Add(buttonStrip);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // `--wid` wants a top-level owner window, so pass the form's handle; geometry is fixed up
        // below because `--position` goes through Godot's virtual multi-monitor origin math.
        _engine = new Engine("showcase", args:
        [
            "--wid", Handle.ToInt64().ToString(CultureInfo.InvariantCulture),
            "--resolution", $"{_gamePanel.ClientSize.Width}x{_gamePanel.ClientSize.Height}",
            "--position", "0,0",
            .. _extraArgs,
        ]);
        _instance = _engine.Start();
        _godotHwnd = (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        SyncGodotBounds();

        // The owned popup lives in screen coordinates and does not track the form on its own.
        _gamePanel.Resize += (_, _) => SyncGodotBounds();
        LocationChanged += (_, _) => SyncGodotBounds();

        _pauseButton.Enabled = true;
        Application.Idle += OnApplicationIdle;
    }

    // Godot refuses window_set_size for embedded windows, so the host drives the popup with raw
    // SetWindowPos, exactly like the editor's embedded game window; Godot's WndProc adopts the size.
    private void SyncGodotBounds()
    {
        if (_godotHwnd == 0)
            return;
        var origin = _gamePanel.PointToScreen(Point.Empty);
        SetWindowPos(_godotHwnd, 0, origin.X, origin.Y,
            _gamePanel.ClientSize.Width, _gamePanel.ClientSize.Height, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    // Classic WinForms game loop: iterate whenever the message queue is empty. Iteration() also runs
    // DisplayServer::process_events(), which dispatches this thread's queued messages without
    // Application's preprocessing; harmless, since the loop yields as soon as anything is queued.
    private void OnApplicationIdle(object? sender, EventArgs e)
    {
        while (_instance is { } instance && !PeekMessage(out _, 0, 0, 0, 0))
        {
            // Iteration() returns true when the engine wants to quit (SceneTree.Quit(), --quit-after N, ...).
            if (instance.Iteration())
            {
                ShutdownEngine();
                Close();
                return;
            }
        }
    }

    // The UI thread is the pump thread, so game state is safe to touch from event handlers,
    // including the scene tree via _engine.Tree.
    private void OnPauseClicked(object? sender, EventArgs e)
    {
        if (_paused)
            _instance!.Resume();
        else
            _instance!.Pause();
        _paused = !_paused;
        _pauseButton.Text = _paused ? "Resume" : "Pause";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Tear down while the owner HWND still exists: Godot self-closes its window when the owner
        // disappears, and the engine must be gone before the ProcessExit libgodot unload runs.
        ShutdownEngine();
        base.OnFormClosing(e);
    }

    private void ShutdownEngine()
    {
        Application.Idle -= OnApplicationIdle;
        _pauseButton.Enabled = false;
        _instance?.Dispose();
        _instance = null;
        _engine?.Dispose();
        _engine = null;
        _godotHwnd = 0;
    }

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint Handle;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public Point Location;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out NativeMessage message, nint hWnd, uint filterMin, uint filterMax, uint flags);
}