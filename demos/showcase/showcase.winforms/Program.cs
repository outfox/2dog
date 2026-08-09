namespace showcase;

internal static class Program
{
    // Godot's display server needs the same STA main thread godot.exe uses; WinForms requires it anyway.
    [STAThread]
    private static void Main(string[] args)
    {
        // Applies visual styles and the csproj's SystemAware DPI mode before Godot starts, so the
        // engine's own SetProcessDpiAwareness call becomes a no-op on an already-configured process.
        ApplicationConfiguration.Initialize();

        // Command-line arguments are forwarded to Godot (--quit-after, --verbose, ...).
        Application.Run(new MainForm(args));
    }
}