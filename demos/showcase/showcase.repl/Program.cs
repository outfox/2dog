using twodog.Repl;

internal static class Program
{
    // Godot owns the process main thread (STA on Windows), also while the prompt is reading.
    [STAThread]
    private static int Main(string[] args) => ReplHost.Run(args,
        godotArgs => new twodog.Engine("showcase", args: godotArgs), System.Reflection.Assembly.Load("showcase"));
}
