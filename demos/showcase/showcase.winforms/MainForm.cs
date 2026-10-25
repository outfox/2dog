using System.Globalization;
using System.Runtime.InteropServices;
using Godot;
using Button = System.Windows.Forms.Button;
using Color = System.Drawing.Color;
using Engine = twodog.Engine;
using Font = System.Drawing.Font;
using Label = System.Windows.Forms.Label;
using Panel = System.Windows.Forms.Panel;

// Embeds the showcase inside a WinForms window via Godot's `--wid` embedding (the same mechanism the
// Godot editor uses for its embedded game window): Godot creates its main window as a borderless popup
// owned by this form, and the host drives its geometry and pumps frames from the UI thread.
// This is the repo's instance of the WinForms host the 2dog tool scaffolds
// (templates/twodog/Company.Product1.winforms) - keep the two in sync.
namespace showcase;

internal sealed class MainForm : Form
{
    private static readonly Color Backdrop = Color.FromArgb(0x1E, 0x26, 0x33);
    private static readonly Color Surface = Color.FromArgb(0x12, 0x18, 0x21);
    private static readonly Color Accent = Color.FromArgb(0xA8, 0xE6, 0x6A);
    private static readonly Color Muted = Color.FromArgb(0x9A, 0xA7, 0xB8);
    private static readonly Color Starting = Color.FromArgb(0xF2, 0xB8, 0x4B);
    private static readonly Color Running = Color.FromArgb(0x4A, 0xDE, 0x80);

    private readonly string[] _extraArgs;
    private readonly Panel _gamePanel;
    private readonly Button _pauseButton;
    private readonly TrackBar _speed;
    private readonly Label _speedValue;
    private readonly Label _status;

    private Engine? _engine;
    private GodotInstance? _instance;
    private nint _godotHwnd;
    private bool _paused;
    private Node3D[] _whiteCubes = [];
    private static readonly Vector3 WhiteSpinAxis = new Vector3(1, 1, 0).Normalized();

    public MainForm(string[] extraArgs)
    {
        _extraArgs = extraArgs;

        // Sizes below are 96-DPI logical units; the form scales them to the system DPI.
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "2dog showcase — WinForms host";
        ClientSize = new Size(1152, 704);
        MinimumSize = new Size(640, 360);
        BackColor = Backdrop;
        ForeColor = Color.White;

        _pauseButton = new Button
        {
            Text = "Pause", Enabled = false, Size = new Size(96, 32), Margin = new Padding(16, 0, 16, 0),
            FlatStyle = FlatStyle.Flat, ForeColor = Color.FromArgb(0x0B, 0x12, 0x20), Cursor = Cursors.Hand,
            Font = new Font(Font, FontStyle.Bold), Anchor = AnchorStyles.Left,
        };
        _pauseButton.FlatAppearance.BorderSize = 0;
        _pauseButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(0x74, 0xB0, 0xFF);
        _pauseButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(0x3F, 0x86, 0xE8);
        _pauseButton.Click += OnPauseClicked;

        _speed = new TrackBar
        {
            Minimum = 0, Maximum = 300, Value = 100, TickStyle = TickStyle.None, AutoSize = false,
            Size = new Size(160, 30), BackColor = Surface, Enabled = false, Anchor = AnchorStyles.Left,
        };
        _speed.ValueChanged += OnSpeedChanged;
        _speedValue = HeaderLabel("1.00×", Color.White);
        _speedValue.MinimumSize = new Size(44, 0);
        _status = HeaderLabel("", Starting);

        // The toolbar sits above the game: Godot's popup always draws above the form's client area,
        // so form controls must not overlap the embed rectangle.
        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Top, Height = 56, BackColor = Surface, Padding = new Padding(12, 0, 4, 0),
            ColumnCount = 8, RowCount = 1,
        };
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        for (var column = 0; column < 8; column++)
            toolbar.ColumnStyles.Add(column == 3 ? new ColumnStyle(SizeType.Percent, 100) : new ColumnStyle());
        var title = HeaderLabel("2dog showcase", Color.White);
        title.Font = new Font(Font.FontFamily, 11F, FontStyle.Bold);
        toolbar.Controls.Add(title, 0, 0);
        toolbar.Controls.Add(HeaderLabel("WinForms host", Muted), 1, 0);
        toolbar.Controls.Add(_status, 2, 0);
        toolbar.Controls.Add(HeaderLabel("Game speed", Color.White), 4, 0);
        toolbar.Controls.Add(_speed, 5, 0);
        toolbar.Controls.Add(_speedValue, 6, 0);
        var actions = new FlowLayoutPanel
        {
            AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left, Margin = Padding.Empty,
            Controls = { _pauseButton },
        };
        toolbar.Controls.Add(actions, 7, 0);

        _gamePanel = new Panel { Dock = DockStyle.Fill, BackColor = Backdrop };

        Controls.Add(_gamePanel);
        Controls.Add(toolbar);
        SetStatus("● Starting", Starting, running: false);
        ResumeLayout(false);
        PerformLayout();
    }

    private static Label HeaderLabel(string text, Color color) => new()
    {
        Text = text, ForeColor = color, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 0, 8, 0),
    };

    private void SetStatus(string text, Color color, bool running)
    {
        _status.Text = text;
        _status.ForeColor = color;
        _pauseButton.Enabled = _speed.Enabled = running;
        _pauseButton.BackColor = running ? Accent : Color.FromArgb(0x2A, 0x36, 0x47);
    }

    // Windows draws a light title bar for Win32 windows unless asked otherwise.
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var dark = 1;
        _ = DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
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

        // The blue cubes spin themselves via SpinningCube._Process (Godot side); the white
        // ones are plain MeshInstance3Ds this host drives per frame, like the desktop host.
        _whiteCubes = _engine.Tree.CurrentScene
            .GetNode<Node3D>("Flair/WhiteCubes")
            .GetChildren().OfType<Node3D>().ToArray();

        SetStatus("● Running", Running, running: true);
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

    // Classic WinForms game loop: iterate whenever the message queue is empty. Update() also runs
    // DisplayServer::process_events(), which dispatches this thread's queued messages without
    // Application's preprocessing; harmless, since the loop yields as soon as anything is queued.
    private void OnApplicationIdle(object? sender, EventArgs e)
    {
        while (_instance is not null && _engine is { } engine && !PeekMessage(out _, 0, 0, 0, 0))
        {
            // Update() returns false when the engine wants to quit (SceneTree.Quit(), --quit-after N, ...).
            if (!engine.Update())
            {
                ShutdownEngine();
                Close();
                return;
            }
            // Skipped while paused: the root's delta keeps ticking when the tree is paused.
            if (_paused) continue;
            var delta = (float)engine.Tree.Root.GetProcessDeltaTime();
            foreach (var cube in _whiteCubes)
                cube.Rotate(WhiteSpinAxis, 1.8f * delta);
        }
    }

    // The UI thread is the pump thread, so game state is safe to touch from event handlers,
    // including the scene tree via _engine.Tree.
    private void OnPauseClicked(object? sender, EventArgs e)
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
        _pauseButton.Text = _paused ? "Resume" : "Pause";
        SetStatus(_paused ? "● Paused" : "● Running", _paused ? Starting : Running, running: true);
    }

    private void OnSpeedChanged(object? sender, EventArgs e)
    {
        var scale = _speed.Value / 100.0;
        _speedValue.Text = $"{scale:0.00}×";
        if (_engine is not null)
            Godot.Engine.TimeScale = scale;
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
        SetStatus("● Stopped", Muted, running: false);
        _whiteCubes = [];
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
