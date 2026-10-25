using Godot;
using Engine = twodog.Engine;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var engine = new Engine("EditorLifecycleSmoke", AppContext.BaseDirectory, "--headless", "--editor")
        {
            CaptureErrors = true,
        };
        engine.Start();
        Node? watcher = null;
        for (int frame = 0; frame < 120 && watcher is null; frame++)
        {
            if (!engine.Update()) throw new Exception("Editor quit before its hot-reload watcher was ready.");
            watcher = engine.Tree.Root.FindChild("HotReloadAssemblyWatcher", recursive: true, owned: false);
            Thread.Sleep(10);
        }
        if (watcher is null) throw new Exception("Editor did not create its hot-reload watcher.");
        var timer = watcher.GetChildren().OfType<Godot.Timer>().Single();
        watcher.Call("RestartTimer");
        if (timer.IsStopped()) throw new Exception("The attached hot-reload timer did not start.");

        // Child nodes leave first during shutdown. A focus notification can arrive
        // while the watcher is still attached but its timer has already left.
        watcher.RemoveChild(timer);
        try
        {
            watcher.Call("RestartTimer");
            watcher.Notification((int)Node.NotificationWMWindowFocusIn);
            RequireNoErrors(engine);
        }
        finally { watcher.AddChild(timer); }

        var parent = watcher.GetParent();
        parent.RemoveChild(watcher);
        try
        {
            watcher.Call("RestartTimer");
            watcher.Notification((int)Node.NotificationWMWindowFocusIn);
            RequireNoErrors(engine);
        }
        finally { parent.AddChild(watcher); }
        watcher.Call("RestartTimer");
        if (timer.IsStopped()) throw new Exception("The reattached hot-reload timer did not resume.");

        // Let the first editor scan finish before testing normal teardown.
        var filesystem = EditorInterface.Singleton.GetResourceFilesystem();
        for (int frame = 0; filesystem.IsScanning(); frame++)
        {
            if (frame == 1000) throw new Exception("Editor filesystem scan did not finish.");
            if (!engine.Update()) throw new Exception("Editor quit during its filesystem scan.");
            Thread.Sleep(10);
        }
        engine.Dispose();
        engine.Completion.GetAwaiter().GetResult();
        RequireNoErrors(engine);
        Console.WriteLine("NATIVE_EDITOR_LIFECYCLE_SMOKE_PASSED");
    }

    private static void RequireNoErrors(Engine engine)
    {
        var errors = engine.Errors.Drain();
        if (errors.Length != 0) throw new Exception(string.Join("\n", errors.Select(error => error.ToString())));
    }
}
