using Avalonia;

internal static class Program
{
    // Godot's display server needs the same STA main thread godot.exe uses; Avalonia's
    // Win32 backend expects STA as well.
    [STAThread]
    private static void Main(string[] args) => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .StartWithClassicDesktopLifetime(args);
}
