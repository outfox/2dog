using System.ComponentModel;
using twodog;
using GodotEngine = Godot.Engine;

namespace showcase;

/// <summary>The overlay's state, created by Main.qml. QML calls run on Qt's GUI thread, the engine's pump thread.</summary>
public class ShowcaseControls : INotifyPropertyChanged
{
    private static QtGodotSession Session => Program.Session;

    public ShowcaseControls()
    {
        Session.Started += (_, _) => SetStatus("Running", "#4ADE80", running: true);
        Session.Stopped += (_, _) => SetStatus("Stopped", "#9AA7B8", running: false);
        Session.FrameAdvanced += (_, _) => UpdateFps();
        Session.ActiveModeChanged += (_, _) => UpdateMode();
        if (Session.IsStarted) SetStatus("Running", "#4ADE80", running: true);
        UpdateMode();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Status { get; private set; } = "Starting";

    public string StatusColor { get; private set; } = "#F2B84B";

    public bool Running { get; private set; }

    public bool Paused { get; private set; }

    public string Fps { get; private set; } = "";

    public string Mode { get; private set; } = "Waiting for Godot";

    public void TogglePause()
    {
        if (!Session.IsStarted) return;
        Paused = Session.IsPaused = !Session.IsPaused;
        Changed(nameof(Paused));
        if (Paused) SetStatus("Paused", "#F2B84B", running: true);
        else SetStatus("Running", "#4ADE80", running: true);
    }

    public void SetSpeed(double speed)
    {
        if (Session.IsStarted) GodotEngine.TimeScale = speed;
    }

    private void UpdateFps()
    {
        var fps = $"{GodotEngine.GetFramesPerSecond():0} fps";
        if (fps == Fps) return;
        Fps = fps;
        Changed(nameof(Fps));
    }

    private void UpdateMode()
    {
        Mode = Session.ActiveMode switch
        {
            QtPresentationMode.Gpu => "GPU presentation",
            QtPresentationMode.Cpu => "CPU readback presentation",
            _ => "Waiting for Godot",
        };
        Changed(nameof(Mode));
        if (Session.ActiveMode is { } mode) Console.WriteLine($"2DOG_QT_MODE={mode}");
    }

    private void SetStatus(string status, string color, bool running)
    {
        (Status, StatusColor, Running) = (status, color, running);
        Changed(nameof(Status));
        Changed(nameof(StatusColor));
        Changed(nameof(Running));
    }

    private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
