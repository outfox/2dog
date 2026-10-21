namespace twodog;

/// <summary>How a <see cref="QtGodotSession"/> brings the engine's frames into Qt Quick.</summary>
public enum QtPresentationMode
{
    /// <summary>Zero-copy GPU sharing where the natives and Qt's graphics backend support it, otherwise CPU
    /// readback.</summary>
    Auto,

    /// <summary>Require GPU sharing. Where it is unsupported nothing is presented and
    /// <see cref="QtGodotSession.ActiveMode"/> stays null.</summary>
    Gpu,

    /// <summary>Always copy frames through the CPU.</summary>
    Cpu,
}
