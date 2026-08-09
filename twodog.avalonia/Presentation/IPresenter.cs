using System;

namespace twodog.Presentation;

/// <summary>
/// Brings the engine's rendered viewport into Avalonia for one attached control. Created on
/// attach, disposed on detach; all calls happen on the UI thread, which is also the pump thread.
/// </summary>
internal interface IPresenter : IDisposable
{
    /// <summary>The mode this presenter implements (what Auto resolved to).</summary>
    GodotPresentationMode Mode { get; }

    /// <summary>Permanently unable to present; the session may swap in a fallback.</summary>
    bool Failed { get; }

    /// <summary>Called once per engine frame, after <c>GodotInstance.Iteration()</c>.</summary>
    void PresentFrame();

    /// <summary>Called when the control's size or scaling changed (the session has already
    /// resized the engine window).</summary>
    void Resize();
}
