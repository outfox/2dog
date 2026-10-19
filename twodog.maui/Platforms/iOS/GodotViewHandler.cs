using Godot;
using Microsoft.Maui.Handlers;
using twodog.Ios;
using UIKit;

namespace twodog.Maui;

/// <summary>
/// Hosts Godot's view controller (2dog.ios) as a child of the page's view controller. Godot's display link runs
/// every frame on the main thread, which MAUI shares, so Dispatch runs between frames. Detaching removes the view
/// and pauses rendering; the engine lives on for the next attach (one engine per process).
/// </summary>
internal sealed class GodotViewHandler : ViewHandler<GodotView, UIView>
{
    private UIViewController? _godot;
    private bool _attached;

    public GodotViewHandler() : base(ViewMapper) { }
    protected override UIView CreatePlatformView() => new() { BackgroundColor = UIColor.Black, ClipsToBounds = true };

    protected override void ConnectHandler(UIView platformView)
    {
        base.ConnectHandler(platformView);
        VirtualView.Loaded += Loaded;
        VirtualView.Unloaded += Unloaded;
        GodotHost.Started += OnStarted;
        GodotHost.QuitRequested += OnQuitRequested;
        if (VirtualView.IsLoaded) Attach();
    }

    protected override void DisconnectHandler(UIView platformView)
    {
        VirtualView.Loaded -= Loaded;
        VirtualView.Unloaded -= Unloaded;
        GodotHost.Started -= OnStarted;
        GodotHost.QuitRequested -= OnQuitRequested;
        Detach();
        base.DisconnectHandler(platformView);
    }

    private void Loaded(object? sender, EventArgs args) => Attach();
    private void Unloaded(object? sender, EventArgs args) => Detach();

    private void Attach()
    {
        if (_attached) return;
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(VirtualView.Project);
            var parent = FindViewController(PlatformView)
                ?? throw new InvalidOperationException("The MAUI page's view controller is not available.");
            // The app's delegate registered the plugins initializer with IosHost before any page existed.
            _godot = GodotHost.ViewController ?? GodotHost.Start(VirtualView.Project, packPath: null, args: VirtualView.Arguments);
            if (_godot.ParentViewController is not null && _godot.ParentViewController != parent)
                throw new InvalidOperationException("Only one GodotView may be attached at a time.");
            parent.AddChildViewController(_godot);
            _godot.View!.Frame = PlatformView.Bounds;
            _godot.View.AutoresizingMask = UIViewAutoresizing.FlexibleDimensions;
            PlatformView.AddSubview(_godot.View);
            _godot.DidMoveToParentViewController(parent);
            _attached = true;
            if (GodotHost.IsRunning) OnStarted();
        }
        catch (Exception exception)
        {
            Detach();
            VirtualView.NotifyFailure(exception);
        }
    }

    private void Detach()
    {
        VirtualView.ActivePresentationMode = null;
        if (!_attached || _godot is null)
        {
            _attached = false;
            return;
        }
        // Godot stops its display link when the view disappears and resumes it when the view reappears.
        _godot.WillMoveToParentViewController(null);
        _godot.View?.RemoveFromSuperview();
        _godot.RemoveFromParentViewController();
        _attached = false;
    }

    private void OnStarted()
    {
        if (!_attached) return;
        VirtualView.ActivePresentationMode = GodotPresentationMode.Gpu;
        VirtualView.NotifyStarted();
    }

    private void OnQuitRequested()
    {
        VirtualView.ActivePresentationMode = null;
        VirtualView.NotifyQuit();
    }

    public void Dispatch(Action<SceneTree> action)
    {
        if (!_attached || !GodotHost.IsRunning) throw new InvalidOperationException("Godot has not started.");
        GodotHost.Dispatch(action);
    }

    private static UIViewController? FindViewController(UIView view)
    {
        for (UIResponder? responder = view; responder is not null; responder = responder.NextResponder)
            if (responder is UIViewController controller) return controller;
        return null;
    }
}
