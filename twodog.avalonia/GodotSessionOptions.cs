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
    /// Pump rate while no control is attached (the compositor's animation frames drive the
    /// pump only while a control is on screen).
    /// </summary>
    public double DetachedFramesPerSecond { get; init; } = 30;
}
