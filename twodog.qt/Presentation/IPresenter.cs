using System;
using Godot;

namespace twodog.Presentation;

/// <summary>Brings engine frames into one GodotView. Created and called on Qt's GUI thread, the pump thread.</summary>
internal interface IPresenter : IDisposable
{
    QtPresentationMode Mode { get; }

    /// <summary>Permanently unable to present; the session swaps in the fallback.</summary>
    bool Failed { get; }

    /// <summary>Called after each rendered engine frame with the root viewport's texture.</summary>
    void Present(Rid viewportTexture);
}
