using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

internal static class Program
{
    // Godot's display server needs the same STA main thread godot.exe uses; XAML expects it too.
    [STAThread]
    private static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // Command-line arguments are forwarded to Godot (--quit-after, --verbose, ...).
        Application.Start(p =>
        {
            // Async continuations must land back on this thread; without a synchronization
            // context they would run on the thread pool.
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App(args);
        });
    }
}
