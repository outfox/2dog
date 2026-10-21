using System.ComponentModel;

namespace Company.Product1;

/// <summary>The overlay's state, created by Main.qml. Qt Bridge exposes public types to QML; property changes
/// reach QML bindings through INotifyPropertyChanged.</summary>
public class GameControls : INotifyPropertyChanged
{
    public GameControls()
    {
        Program.Session.Started += (_, _) => SetStatus("Running", "#4ADE80", running: true);
        Program.Session.Stopped += (_, _) => SetStatus("Stopped", "#9AA7B8", running: false);
        if (Program.Session.IsStarted) SetStatus("Running", "#4ADE80", running: true);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Status { get; private set; } = "Starting";

    public string StatusColor { get; private set; } = "#F2B84B";

    public bool Running { get; private set; }

    public bool Paused { get; private set; }

    public void TogglePause()
    {
        if (!Program.Session.IsStarted) return;
        Paused = Program.Session.IsPaused = !Program.Session.IsPaused;
        Changed(nameof(Paused));
        if (Paused) SetStatus("Paused", "#F2B84B", running: true);
        else SetStatus("Running", "#4ADE80", running: true);
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
