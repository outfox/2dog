using Godot;

// Web-only "Enter VR" button: shown when the browser reports immersive-vr support.
// Dormant elsewhere - desktop builds register no "WebXR" interface instance.
namespace showcase;

[GlobalClass]
public partial class WebXR : Button
{
    private WebXRInterface _webxr;

    public override void _Ready()
    {
        _webxr = XRServer.FindInterface("WebXR") as WebXRInterface;
        if (_webxr is null)
        {
            return;
        }

        _webxr.SessionSupported += OnSessionSupported;
        _webxr.SessionStarted += OnSessionStarted;
        _webxr.SessionEnded += OnSessionEnded;
        _webxr.SessionFailed += OnSessionFailed;
        Pressed += OnPressed;

        _webxr.IsSessionSupported("immersive-vr");
    }

    public override void _ExitTree()
    {
        if (_webxr is null)
        {
            return;
        }

        _webxr.SessionSupported -= OnSessionSupported;
        _webxr.SessionStarted -= OnSessionStarted;
        _webxr.SessionEnded -= OnSessionEnded;
        _webxr.SessionFailed -= OnSessionFailed;
        _webxr = null;
    }

    private void OnSessionSupported(string sessionMode, bool supported)
    {
        GD.Print($"WebXR session mode '{sessionMode}' supported: {supported}");
        if (sessionMode == "immersive-vr")
        {
            Visible = supported;
        }
    }

    // A Godot button press originates from the browser's input event, so it satisfies
    // the user-gesture requirement for starting an XR session.
    private void OnPressed()
    {
        _webxr.SessionMode = "immersive-vr";
        _webxr.RequestedReferenceSpaceTypes = "bounded-floor, local-floor, local";
        _webxr.RequiredFeatures = "local-floor";
        _webxr.OptionalFeatures = "bounded-floor";

        if (!_webxr.Initialize())
        {
            OS.Alert("Failed to initialize WebXR.");
        }
    }

    private void OnSessionStarted()
    {
        GD.Print($"WebXR session started (reference space: {_webxr.ReferenceSpaceType})");
        Visible = false;
        GetViewport().UseXR = true;
    }

    private void OnSessionEnded()
    {
        GD.Print("WebXR session ended");
        Visible = true;
        GetViewport().UseXR = false;
    }

    private void OnSessionFailed(string message)
    {
        OS.Alert($"WebXR session failed: {message}");
    }
}
