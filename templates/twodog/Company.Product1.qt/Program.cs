using Qt.Quick;
using twodog;

namespace Company.Product1;

// Shows the game in a Qt Quick window through 2dog.qt's GodotView (Main.qml): QML items render on top of the running
// game. Qt Bridge for C# runs Main on its own thread while Qt's GUI thread runs the engine, so engine code goes in the
// session's events or in C# methods that QML calls (both run on that thread).
internal static class Program
{
    internal static QtGodotSession Session { get; private set; } = null!;

    private static void Main(string[] args)
    {
        // Command-line arguments are forwarded to Godot (--quit-after, --verbose, ...). Disposing waits for the engine
        // to shut down, which must happen before Main returns.
        using var session = Session = new QtGodotSession("TPLRAWNAME", args: args);
        Qml.LoadFromRootModule("Main");
        Qml.WaitForExit();
    }
}
