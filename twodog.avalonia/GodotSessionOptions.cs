using System;
using System.Collections.Generic;

namespace twodog;

/// <summary>How a <see cref="GodotSession"/> presents the engine's viewport in Avalonia.</summary>
public enum GodotPresentationMode
{
    /// <summary>Zero-copy GPU compositing when the natives support it, otherwise CPU readback.</summary>
    Auto,

    /// <summary>Require zero-copy GPU compositing; attaching fails where it is unsupported.</summary>
    Gpu,

    /// <summary>Always use the CPU readback path.</summary>
    Cpu,
}

/// <summary>Configuration for a <see cref="GodotSession"/>.</summary>
public sealed class GodotSessionOptions
{
    /// <summary>Label passed as Godot's first argument (see <see cref="Engine"/>).</summary>
    public required string Project { get; init; }

    /// <summary>
    /// Project directory or pack. When null, published builds load the exe-adjacent pack and
    /// source builds resolve the project directory from assembly metadata (Engine behavior).
    /// </summary>
    public string? Path { get; init; }

    /// <summary>Additional Godot command-line arguments, passed through verbatim.</summary>
    public IReadOnlyList<string> ExtraArgs { get; init; } = [];

    public GodotPresentationMode PresentationMode { get; init; } = GodotPresentationMode.Auto;

    /// <summary>Pause the engine while no <see cref="GodotControl"/> is attached.</summary>
    public bool PauseWhenDetached { get; init; }

    /// <summary>
    /// Upper bound for the engine frame rate. The compositor's animation frames drive the
    /// pump, but not every backend paces them to the display (Avalonia's Wayland backend
    /// free-runs), so the session skips pump ticks that arrive faster than this cap
    /// (nonblocking - Godot's own <c>Engine.MaxFps</c> limiter would sleep on the UI
    /// thread). 0 (the default) means auto: the highest refresh rate among the connected
    /// screens. <see cref="double.PositiveInfinity"/> means uncapped.
    /// NaN and negative values are rejected.
    /// </summary>
    public double MaxFramesPerSecond
    {
        get;
        init => field = double.IsNaN(value) || value < 0
            ? throw new ArgumentException($"{nameof(MaxFramesPerSecond)} must be 0 (auto) or positive.", nameof(value))
            : value;
    }

    /// <summary>
    /// Pump rate while no control is attached (the compositor's animation frames drive the
    /// pump only while a control is on screen). Clamped to 1-240; NaN is rejected.
    /// </summary>
    public double DetachedFramesPerSecond
    {
        get;
        init => field = double.IsNaN(value)
            ? throw new ArgumentException($"{nameof(DetachedFramesPerSecond)} must not be NaN.", nameof(value))
            : Math.Clamp(value, 1, 240);
    } = 30;
}
