using System;
using Avalonia.Controls;
using Avalonia.Threading;
using Godot;
using twodog.Presentation;
using Dispatcher = Avalonia.Threading.Dispatcher;

namespace twodog;

/// <summary>
/// Owns the embedded Godot engine and pumps its main loop on the Avalonia UI thread.
/// A session outlives any <see cref="GodotControl"/> showing it: controls attach and detach
/// freely, while the engine (one per process) stays with the session until Dispose().
/// </summary>
public sealed class GodotSession : IDisposable
{
    private readonly GodotSessionOptions _options;
    private readonly Action<TimeSpan> _onAnimationFrame;
    private Engine? _engine;
    private GodotInstance? _instance;
    private GodotControl? _control;
    private IPresenter? _presenter;
    private GodotInputForwarder? _forwarder;
    private DispatcherTimer? _detachedTimer;
    private bool _gpuSupported;
    private bool _pumpScheduled;
    private bool _pumpStopped;
    private bool _disposed;
    private bool _pausedByUser;
    private bool _pausedByDetach;

    public GodotSession(GodotSessionOptions options)
    {
        _options = options;
        _onAnimationFrame = OnAnimationFrame;
    }

    /// <summary>The engine; valid after <see cref="Start"/>.</summary>
    public Engine Engine => _engine ?? throw new InvalidOperationException(
        $"{nameof(GodotSession)}: Start() must be called first.");

    /// <summary>The running instance; valid after <see cref="Start"/>.</summary>
    public GodotInstance Instance => _instance ?? throw new InvalidOperationException(
        $"{nameof(GodotSession)}: Start() must be called first.");

    public bool IsStarted => _instance is not null;
    public bool IsPaused => _pausedByUser;

    /// <summary>What <see cref="GodotPresentationMode.Auto"/> resolved to for the attached control.</summary>
    public GodotPresentationMode? ActiveMode => _presenter?.Mode;

    public event EventHandler? Started;

    /// <summary>The engine asked to quit. The host should close its window and Dispose().</summary>
    public event EventHandler? QuitRequested;

    public event EventHandler? Stopped;

    /// <summary>Starts the engine and the frame pump. UI thread only.</summary>
    public void Start()
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_instance is not null)
            throw new InvalidOperationException($"{nameof(GodotSession)}: already started.");
        if (_options.PresentationMode == GodotPresentationMode.Gpu && !OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                $"{nameof(GodotPresentationMode.Gpu)} presentation is currently Windows-only; use Auto or Cpu.");

        // The engine's own window stays unmapped; this control is its only face.
        _engine = new Engine(_options.Project, _options.Path, ["--hidden-window", .. _options.ExtraArgs]);
        _instance = _engine.Start();
        _gpuSupported = OperatingSystem.IsWindows() && GpuPresenter.IsSupported();
        Started?.Invoke(this, EventArgs.Empty);

        ReconcilePresenter();
        SyncEngineWindowSize();
        _presenter?.Resize();
        UpdatePumpSource();
        if (_control is null && _options.PauseWhenDetached) SetPaused(detached: true);
    }

    public void Pause() => SetPaused(user: true);

    public void Resume() => SetPaused(user: false);

    // Both pause reasons compose into one engine-facing state; the engine is only
    // notified on the combined transition.
    private void SetPaused(bool? user = null, bool? detached = null)
    {
        var wasPaused = _pausedByUser || _pausedByDetach;
        _pausedByUser = user ?? _pausedByUser;
        _pausedByDetach = detached ?? _pausedByDetach;
        var isPaused = _pausedByUser || _pausedByDetach;
        if (isPaused == wasPaused) return;
        if (isPaused) Instance.Pause();
        else Instance.Resume();
    }

    internal void Attach(GodotControl control)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_control is not null && !ReferenceEquals(_control, control))
            throw new InvalidOperationException(
                $"{nameof(GodotSession)}: another {nameof(GodotControl)} is already attached.");

        _control = control;
        _forwarder = new GodotInputForwarder(control, this);
        _presenter = CreatePresenter(control);
        if (_pausedByDetach) SetPaused(detached: false);
        UpdatePumpSource();
    }

    internal void Detach(GodotControl control)
    {
        if (!ReferenceEquals(_control, control)) return;
        _presenter?.Dispose();
        _presenter = null;
        _forwarder?.Dispose();
        _forwarder = null;
        _control = null;
        if (!_disposed)
        {
            if (_options.PauseWhenDetached && _instance is not null) SetPaused(detached: true);
            UpdatePumpSource();
        }
    }

    internal void NotifyControlResized()
    {
        SyncEngineWindowSize();
        _presenter?.Resize();
    }

    // The engine's window follows the control's pixel size; the root viewport (and with it
    // both presentation paths) follows the window.
    private void SyncEngineWindowSize()
    {
        if (_instance is null || _control is null) return;
        var scale = TopLevel.GetTopLevel(_control)?.RenderScaling ?? 1.0;
        var width = (int)Math.Round(_control.Bounds.Width * scale);
        var height = (int)Math.Round(_control.Bounds.Height * scale);
        if (width <= 0 || height <= 0) return;

        var target = new Vector2I(width, height);
        if (DisplayServer.WindowGetSize() != target) DisplayServer.WindowSetSize(target);
    }

    private IPresenter CreatePresenter(GodotControl control)
    {
        // Zero-copy GPU compositing is Windows-only for now; Gpu mode elsewhere is rejected
        // in Start(). Under Auto, support is unknown before Start(); begin with CPU and let
        // ReconcilePresenter upgrade once the engine is up.
        if (OperatingSystem.IsWindows())
        {
            if (_options.PresentationMode == GodotPresentationMode.Gpu)
                return new GpuPresenter(control, this);
            if (_options.PresentationMode == GodotPresentationMode.Auto && _gpuSupported)
                return new GpuPresenter(control, this);
        }
        return new CpuPresenter(control, this);
    }

    // Auto mode reacts to what the engine turned out to support: upgrade CPU to zero-copy
    // after Start(), and fall back to CPU when GPU initialization failed.
    private void ReconcilePresenter()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (_options.PresentationMode != GodotPresentationMode.Auto || _control is null || _presenter is null) return;

        if (_gpuSupported && _presenter is CpuPresenter)
        {
            _presenter.Dispose();
            _presenter = new GpuPresenter(_control, this);
        }
        else if (_presenter.Failed)
        {
            _presenter.Dispose();
            _presenter = new CpuPresenter(_control, this);
        }
    }

    // The pump has two sources, both on the UI thread: the compositor's animation frames
    // while a control is on screen (paces the engine to vsync - the engine's own window is
    // hidden and never blocks in present), and a timer while detached.
    private void UpdatePumpSource()
    {
        if (_instance is null || _pumpStopped || _disposed) return;

        if (_control is { } control && TopLevel.GetTopLevel(control) is { } topLevel)
        {
            _detachedTimer?.Stop();
            _detachedTimer = null;
            if (!_pumpScheduled)
            {
                _pumpScheduled = true;
                topLevel.RequestAnimationFrame(_onAnimationFrame);
            }
        }
        else if (_detachedTimer is null)
        {
            var fps = Math.Clamp(_options.DetachedFramesPerSecond, 1, 240);
            _detachedTimer = new DispatcherTimer(
                TimeSpan.FromSeconds(1 / fps), DispatcherPriority.Background, (_, _) => PumpFrame());
            _detachedTimer.Start();
        }
    }

    private void OnAnimationFrame(TimeSpan _)
    {
        _pumpScheduled = false;
        PumpFrame();
        // Re-registration (or the switch to the detached timer) is UpdatePumpSource's call;
        // its guards cover the stopped/disposed/detached cases.
        UpdatePumpSource();
    }

    private void PumpFrame()
    {
        if (_instance is null || _pumpStopped || _disposed) return;

        if (_instance.Iteration())
        {
            StopPump();
            QuitRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        ReconcilePresenter();
        _presenter?.PresentFrame();
        _forwarder?.SyncCursor();
    }

    private void StopPump()
    {
        _pumpStopped = true;
        _detachedTimer?.Stop();
        _detachedTimer = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopPump();
        if (_control is { } control) Detach(control);

        // Dispose before process exit: on Windows the engine must be gone before the
        // ProcessExit libgodot unload runs (see Engine's static constructor).
        _instance?.Dispose();
        _instance = null;
        _engine?.Dispose();
        _engine = null;
        Stopped?.Invoke(this, EventArgs.Empty);
    }
}
