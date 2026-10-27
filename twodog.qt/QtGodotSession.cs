using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using Godot;
using twodog.Presentation;

namespace twodog;

/// <summary>
/// Runs the Godot engine inside a Qt Quick application built with Qt Bridge for C#. Create it in <c>Main</c>
/// before loading QML and dispose it after <c>Qml.WaitForExit()</c>; a <c>GodotView</c> item from the
/// <c>TwoDog</c> QML module shows the engine.
/// </summary>
/// <remarks>
/// Qt Bridge runs <c>Main</c> on its own thread while Qt owns the process main thread. The engine starts once the
/// first <c>GodotView</c> has a rendering window and is pumped on Qt's GUI thread, once per Qt Quick frame, so
/// <see cref="Started"/>, <see cref="FrameAdvanced"/> and <see cref="Stopped"/> run there, as do C# methods that
/// QML calls: both may use the Godot API directly. While no frames come (the window is minimized or hidden, or no
/// view is attached) the engine keeps running at a background rate without rendering, as long as Qt's event loop
/// runs; pause it with <see cref="IsPaused"/>. One session per process.
/// </remarks>
public sealed unsafe class QtGodotSession : IDisposable
{
    private static QtGodotSession? _current;

    // Engine window resizes reallocate the renderer's buffers: an interactive resize applies at most this often.
    private static readonly TimeSpan ResizeThrottle = TimeSpan.FromMilliseconds(125);

    private readonly string _project;
    private readonly string? _path;
    private readonly string[] _args;
    private readonly ManualResetEventSlim _stopped = new();
    private readonly QtInputForwarder _input = new();
    private Engine? _engine;
    private GodotInstance? _instance;
    private Rid _viewportTexture;
    private IPresenter? _presenter;
    private bool _gpuFailed;
    private QtPresentationMode? _notifiedMode;
    private nint _view;
    private int _cursor = -1;
    private bool _inFrame;
    private bool _rendering = true;
    private bool _paused;
    private bool _ended;
    private long _lastResize;
    private volatile bool _disposed;
    private ExceptionDispatchInfo? _failure;

    /// <summary>Registers the session with the TwoDog QML module; the engine starts with the first GodotView.</summary>
    /// <param name="project">Label passed as Godot's first argument (see <see cref="twodog.Engine"/>).</param>
    /// <param name="path">Project directory; null resolves the content like <see cref="twodog.Engine"/>.</param>
    /// <param name="args">Additional Godot command-line arguments.</param>
    public QtGodotSession(string project, string? path = null, params string[] args)
    {
        if (Interlocked.CompareExchange(ref _current, this, null) is not null)
            throw new InvalidOperationException($"{nameof(QtGodotSession)}: only one session may exist per process.");
        _project = project;
        _path = path;
        _args = args;
        var callbacks = new QtCallbacks
        {
            Attached = &OnAttached,
            Detached = &OnDetached,
            Frame = &OnFrame,
            Tick = &OnTick,
            Input = &OnInput,
            Shutdown = &OnShutdown,
        };
        Native.SetCallbacks(&callbacks);
    }

    /// <summary>How frames reach the GodotView; <see cref="QtPresentationMode.Auto"/> prefers GPU sharing.</summary>
    public QtPresentationMode PresentationMode { get; init; } = QtPresentationMode.Auto;

    /// <summary>What presents frames now (<see cref="QtPresentationMode.Gpu"/> or <see cref="QtPresentationMode.Cpu"/>),
    /// or null while nothing does.</summary>
    public QtPresentationMode? ActiveMode => _presenter?.Mode;

    /// <summary>The running engine. Valid on the GUI thread between <see cref="Started"/> and <see cref="Stopped"/>.</summary>
    public Engine Engine => _engine ?? throw new InvalidOperationException(
        $"{nameof(QtGodotSession)}: the engine is not running.");

    /// <summary>The running instance, borrowed from <see cref="Engine"/>. Same validity as <see cref="Engine"/>.</summary>
    public GodotInstance Instance => _instance ?? throw new InvalidOperationException(
        $"{nameof(QtGodotSession)}: the engine is not running.");

    /// <summary>Whether the engine is running.</summary>
    public bool IsStarted => _engine is not null;

    /// <summary>
    /// Gameplay pause: sets <c>SceneTree.Paused</c> and sends the application-lifecycle pause notification. Set it
    /// on the GUI thread, or before the engine starts, when it is applied at start.
    /// </summary>
    public bool IsPaused
    {
        get => _paused;
        set
        {
            if (_paused == value) return;
            _paused = value;
            if (_engine is not null) ApplyPause();
        }
    }

    /// <summary>The engine started. Raised on Qt's GUI thread.</summary>
    public event EventHandler? Started;

    /// <summary>Raised on Qt's GUI thread after every engine frame, the place for host-driven scene updates.</summary>
    public event EventHandler? FrameAdvanced;

    /// <summary><see cref="ActiveMode"/> changed. Raised on Qt's GUI thread.</summary>
    public event EventHandler? ActiveModeChanged;

    /// <summary>The engine shut down: the application is quitting or Godot quit. Raised on Qt's GUI thread.</summary>
    public event EventHandler? Stopped;

    /// <summary>
    /// Stops the engine on Qt's GUI thread and waits for it, which must happen before <c>Main</c> returns.
    /// Rethrows the first exception raised on the GUI thread by the engine or a handler.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            if (Native.IsGuiThread()) Teardown();
            else if (!_stopped.IsSet) Native.Post(&OnPostedTeardown, 0);
            // Once Qt is quitting, its aboutToQuit teardown runs instead of the posted one.
            _stopped.Wait();
        }
        _failure?.Throw();
    }

    private void Attach(nint view)
    {
        // shortcut: one GodotView shows the engine at a time; further views stay empty until the active one goes.
        if (_view != 0 || _disposed) return;
        _view = view;
        _cursor = -1;
        _lastResize = 0;
        // A new view may sit in a window whose backend imports textures where the last one did not.
        _gpuFailed = false;
    }

    private void Detach(nint view)
    {
        if (view != _view) return;
        if (_engine is not null && !_ended) _input.ReleaseAll();
        SetPresenter(null);
        _view = 0;
    }

    // Deferred to the view's first frame: the scene graph is initialized by then, so the engine can be steered to
    // the GPU Qt renders on.
    private bool TryStart(nint view)
    {
        var api = Native.ViewGraphicsApi(view, out var adapterLuid);
        if (api < 0) return false;
        // The engine window stays unmapped; the GodotView item is its only face. Injected before the user's
        // arguments so an explicit --gpu-luid parses later and wins.
        string[] affinity = PresentationMode != QtPresentationMode.Cpu && adapterLuid != 0
            ? ["--gpu-luid", adapterLuid.ToString("x")]
            : [];
        _engine = new Engine(_project, _path, ["--hidden-window", .. affinity, .. _args]);
        _instance = _engine.Start();
        // Qt Quick's render loop paces the pump; hidden-window vsync and low-processor mode would only delay it.
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        OS.LowProcessorUsageMode = false;
        _viewportTexture = RenderingServer.ViewportGetTexture(_engine.Tree.Root.GetViewportRid());
        if (_paused) ApplyPause();
        SyncSize();
        Started?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void ApplyPause()
    {
        // SceneTree.Paused is the gameplay pause; the lifecycle notification keeps game code informed.
        Engine.Tree.Paused = _paused;
        if (_paused) Instance.Pause();
        else Instance.Resume();
    }

    // False asks the view for no next frame: this frame is nested in an engine iteration, or the view is not the one
    // the engine presents into.
    private bool Frame(nint view)
    {
        if (view != _view || _inFrame || _ended || _disposed) return false;
        // Until the scene graph is initialized, frames keep coming so the engine can start.
        if (_engine is null && !TryStart(view)) return true;
        // Started handlers may have stopped the session.
        if (_engine is null) return false;
        if (Stopwatch.GetElapsedTime(_lastResize) >= ResizeThrottle) SyncSize();
        if (!Advance(rendering: true) || view != _view) return false;
        Present();
        var cursor = QtInputForwarder.ToQtCursor(DisplayServer.CursorGetShape(), Godot.Input.MouseMode);
        if (cursor != _cursor) Native.ViewSetCursor(_view, _cursor = cursor);
        return true;
    }

    // Without Qt Quick frames (window not exposed, no view) the engine still runs, like godot.exe when minimized:
    // game time advances and nothing is drawn.
    private void Tick()
    {
        // Engine teardown pumps Qt's messages too: no frame may start inside it.
        if (_engine is null || _inFrame || _ended || _disposed) return;
        if (_view != 0 && Native.ViewExposed(_view)) return;
        Advance(rendering: false);
    }

    // One engine frame. False when the engine quit.
    private bool Advance(bool rendering)
    {
        if (rendering != _rendering) RenderingServer.RenderLoopEnabled = _rendering = rendering;
        bool quit;
        // Godot's message pump can run Qt's events, and with them another frame request, inside Update().
        _inFrame = true;
        try
        {
            quit = !_engine!.Update();
        }
        finally
        {
            _inFrame = false;
        }
        if (quit)
        {
            Stop();
            Native.Quit(0);
            return false;
        }
        FrameAdvanced?.Invoke(this, EventArgs.Empty);
        return _engine is not null;
    }

    // The engine window follows the item's pixel size and the root viewport follows the window. Set through the
    // Window node: the hidden window never receives the WM resize that would update the node otherwise.
    private void SyncSize()
    {
        Native.ViewPixelSize(_view, out var width, out var height);
        if (width <= 0 || height <= 0) return;
        var size = new Vector2I(width, height);
        var root = Engine.Tree.Root;
        if (root.Size == size) return;
        root.Size = size;
        _lastResize = Stopwatch.GetTimestamp();
    }

    private void Present()
    {
        if (_presenter is null) SetPresenter(CreatePresenter(_view));
        _presenter?.Present(_viewportTexture);
        if (_presenter is not { Failed: true }) return;
        // Only GPU sharing fails. Sticky for this view: retrying every frame would flicker between presenters.
        _gpuFailed = true;
        SetPresenter(CreatePresenter(_view));
        _presenter?.Present(_viewportTexture);
    }

    private IPresenter? CreatePresenter(nint view)
    {
        if (PresentationMode == QtPresentationMode.Cpu) return new CpuPresenter(view);
        if (!_gpuFailed && GpuPresenter.TryCreate(view) is { } gpu) return gpu;
        _gpuFailed = true;
        return PresentationMode == QtPresentationMode.Auto ? new CpuPresenter(view) : null;
    }

    private void SetPresenter(IPresenter? presenter)
    {
        if (ReferenceEquals(presenter, _presenter)) return;
        _presenter?.Dispose();
        _presenter = presenter;
        if (ActiveMode == _notifiedMode) return;
        _notifiedMode = ActiveMode;
        ActiveModeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Stop()
    {
        // The engine runs once per session: a quit ends it even while frames keep coming until Qt exits.
        _ended = true;
        if (_engine is null) return;
        _input.ReleaseAll();
        // Engine-side resources die with the engine.
        SetPresenter(null);
        // Disposed on the GUI thread that started it, before Main returns and process exit unloads libgodot.
        _instance = null;
        _engine.Dispose();
        _engine = null;
        Stopped?.Invoke(this, EventArgs.Empty);
    }

    private void Teardown()
    {
        if (_stopped.IsSet) return;
        try
        {
            Stop();
        }
        catch (Exception ex)
        {
            _failure ??= ExceptionDispatchInfo.Capture(ex);
        }
        finally
        {
            Native.SetCallbacks(null);
            _view = 0;
            _current = null;
            _stopped.Set();
        }
    }

    // Exceptions must not cross the native boundary: the first one ends the application and Dispose rethrows it.
    private static void Guard(Action<QtGodotSession> action) => Guard(s =>
    {
        action(s);
        return true;
    });

    private static bool Guard(Func<QtGodotSession, bool> action)
    {
        if (_current is not { } session) return false;
        try
        {
            return action(session);
        }
        catch (Exception ex)
        {
            session._failure ??= ExceptionDispatchInfo.Capture(ex);
            session.Teardown();
            Native.Quit(1);
            return false;
        }
    }

    [UnmanagedCallersOnly]
    private static void OnAttached(nint view) => Guard(s => s.Attach(view));

    [UnmanagedCallersOnly]
    private static void OnDetached(nint view) => Guard(s => s.Detach(view));

    [UnmanagedCallersOnly]
    private static byte OnFrame(nint view) => Guard(s => s.Frame(view)) ? (byte)1 : (byte)0;

    [UnmanagedCallersOnly]
    private static void OnTick() => Guard(s => s.Tick());

    [UnmanagedCallersOnly]
    private static void OnInput(nint view, QtInput* input)
    {
        var copy = *input;
        Guard(s =>
        {
            if (view == s._view && s._engine is not null && !s._ended) s._input.Handle(copy);
        });
    }

    [UnmanagedCallersOnly]
    private static void OnShutdown() => Guard(s => s.Teardown());

    [UnmanagedCallersOnly]
    private static void OnPostedTeardown(nint _) => Guard(s => s.Teardown());
}
