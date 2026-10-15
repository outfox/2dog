using Godot;
using Microsoft.Maui.Controls;

namespace twodog.Maui;

/// <summary>Windows presentation policy. Android always uses its native GPU surface.</summary>
public enum GodotPresentationMode
{
    /// <summary>Prefer GPU sharing, with CPU readback when it is unavailable.</summary>
    Auto,
    /// <summary>Require GPU sharing; report an error when it is unavailable.</summary>
    Gpu,
    /// <summary>Use CPU readback on Windows.</summary>
    Cpu,
}

/// <summary>An embedded Godot view. One active view is supported per process.</summary>
public sealed class GodotView : View
{
    /// <summary>Game assembly name. Set before the view is attached.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>Godot project path on Windows, or null for the packaged game.</summary>
    public string? ProjectPath { get; set; }

    /// <summary>Additional engine arguments. Set before attachment.</summary>
    public string[] Arguments { get; set; } = [];

    /// <summary>Windows presentation policy, selected before attachment.</summary>
    public GodotPresentationMode PresentationMode { get; set; } = GodotPresentationMode.Auto;

    /// <summary>Active presentation mode; null before startup or after stopping.</summary>
    public GodotPresentationMode? ActivePresentationMode
    {
        get => _activeMode;
        internal set
        {
            if (_activeMode == value) return;
            _activeMode = value;
            Dispatcher.Dispatch(() => PresentationModeChanged?.Invoke(this, EventArgs.Empty));
        }
    }
    private GodotPresentationMode? _activeMode;

    /// <summary>Raised on the MAUI UI thread when presentation starts, falls back, or stops.</summary>
    public event EventHandler? PresentationModeChanged;

    /// <summary>Raised on the MAUI UI thread once the game can receive commands.</summary>
    public event EventHandler? Started;

    /// <summary>Raised on the MAUI UI thread when Godot asks to quit.</summary>
    public event EventHandler? QuitRequested;

    /// <summary>Raised on the MAUI UI thread when engine startup or GPU presentation fails.</summary>
    public event EventHandler<Exception>? Failed;

    /// <summary>Queue a game command on Godot's owning thread (the render thread on Android).</summary>
    public void Dispatch(Action<SceneTree> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (Handler is not GodotViewHandler handler)
            throw new InvalidOperationException("The Godot view is not attached.");
        handler.Dispatch(action);
    }

    internal void NotifyStarted() => Dispatcher.Dispatch(() => Started?.Invoke(this, EventArgs.Empty));
    internal void NotifyQuit() => Dispatcher.Dispatch(() => QuitRequested?.Invoke(this, EventArgs.Empty));
    internal void NotifyFailure(Exception exception) => Dispatcher.Dispatch(() => Failed?.Invoke(this, exception));
}
