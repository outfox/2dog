using Avalonia;

namespace showcase;

internal static class Program
{
    // Godot's display server expects the same STA main thread godot.exe uses;
    // Avalonia's Win32 backend wants STA as well.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .UseWaylandWithFallback()
        .WithInterFont()
        .LogToTrace();
}
