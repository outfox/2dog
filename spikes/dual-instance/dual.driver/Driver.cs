// Per-instance driver, loaded into its own AssemblyLoadContext by dual.host.
// Every twodog/Godot static (proc table, method binds, StringNames,
// InstanceBindings, Engine._godotInstancePtr) is scoped to this ALC, so each
// loaded copy of this assembly drives an independent libgodot module.
//
// Contract with the host: only CoreLib types cross the boundary (invoked via
// reflection); all findings come back in the report string, exceptions included.

using System.Runtime.Loader;
using System.Text;
using Godot;
using Godot.NativeInterop;
using Engine = twodog.Engine;
using Environment = System.Environment;

namespace DualSpike;

public static class Driver
{
    public static string Run(string tag, string nativeDllPath, string projectDir, int frames, int churnPerFrame,
                             Action? bootBarrier, Action? exitBarrier)
    {
        var report = new StringBuilder();
        var checks = 0;
        var failures = 0;

        void Log(string line)
        {
            var msg = $"[{tag} tid={Environment.CurrentManagedThreadId}] {line}";
            Console.WriteLine(msg);
            lock (report) report.AppendLine(msg);
        }

        void Check(bool ok, string what)
        {
            checks++;
            if (!ok) failures++;
            Log($"{(ok ? "ok " : "FAIL")} {what}");
        }

        try
        {
            var alc = AssemblyLoadContext.GetLoadContext(typeof(Driver).Assembly);
            Log($"alc={alc?.Name} cwd={Directory.GetCurrentDirectory()}");

            var engine = new Engine(tag, projectDir, "--headless") { NativePath = nativeDllPath };
            using var godot = engine.Start();
            Check(Engine.LoadedNativePath == nativeDllPath,
                $"LoadedNativePath is this instance's copy ({Path.GetFileName(nativeDllPath)})");
            Check(GdExtensionHost.Loaded, "all GDExtension interface procs resolved");
            Log($"cwd after boot={Directory.GetCurrentDirectory()}");

            // Typed-API work exercising the per-ALC static caches (method binds,
            // StringNames, InstanceBindings, DisposalQueue).
            var tree = engine.Tree;
            var root = tree.Root ?? throw new InvalidOperationException("SceneTree.Root is null");
            var baseChildren = root.GetChildCount();
            var marker = new Node { Name = $"{tag}_marker" };
            root.AddChild(marker);
            Check(root.GetChildCount() == baseChildren + 1, "AddChild visible in GetChildCount");
            Check(marker.Name == $"{tag}_marker", $"Name roundtrip = \"{marker.Name}\"");

            var node2d = new Node2D();
            node2d.Position = new Vector2(3.5f, -4.25f);
            var pos = node2d.Position;
            Check(pos.X == 3.5f && pos.Y == -4.25f, $"Vector2 arg/return roundtrip = {pos}");
            node2d.Free();
            Check(!node2d.IsValid, "Free() invalidates the wrapper");

            using (var rc = new RefCounted())
                Check(rc.GetReferenceCount() == 1, "new RefCounted() rc == 1");
            var releasedBefore = DisposalQueue.Released;
            DisposalQueue.Drain();
            Check(DisposalQueue.Released == releasedBefore + 1, "disposed RefCounted released via drain");

            // Concurrency handshake: block here until the sibling instance is
            // also booted, so the pump windows fully overlap.
            bootBarrier?.Invoke();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var pumped = 0;
            for (var i = 0; i < frames; i++)
            {
                if (godot.Iteration())
                {
                    Log($"engine requested quit at frame {i}");
                    break;
                }
                pumped++;
                if (churnPerFrame > 0)
                {
                    Churn(root, tag, i, churnPerFrame);
                    if (i % 60 == 59)
                    {
                        GC.Collect();
                        DisposalQueue.Drain();
                    }
                }
                if (i == frames / 2) Log($"cwd mid-pump={Directory.GetCurrentDirectory()}");
            }
            sw.Stop();
            Check(pumped == frames, $"pumped {pumped}/{frames} frames in {sw.ElapsedMilliseconds}ms");
            Check(root.GetChildCount() == baseChildren + 1, $"child count stable after pump ({root.GetChildCount()})");
            Check(marker.IsValid && marker.Name == $"{tag}_marker", "marker node survived the pump intact");

            exitBarrier?.Invoke();
            engine.Dispose();
            Log($"cwd after dispose={Directory.GetCurrentDirectory()}");
        }
        catch (Exception e)
        {
            failures++;
            Log($"EXCEPTION: {e}");
        }

        var line = $"RESULT {tag}: {(failures == 0 ? "PASS" : "FAIL")} checks={checks} failures={failures}";
        Console.WriteLine($"[{tag}] {line}");
        lock (report) report.AppendLine(line);
        return report.ToString();
    }

    /// <summary>Per-frame node + RefCounted churn for the stress stage.</summary>
    private static void Churn(Node root, string tag, int frame, int count)
    {
        var nodes = new List<Node>(count);
        for (var i = 0; i < count; i++)
        {
            var n = new Node { Name = $"{tag}_c{frame}_{i}" };
            root.AddChild(n);
            nodes.Add(n);
        }
        foreach (var n in nodes) n.Free();
        using var rc = new RefCounted();
        _ = rc.GetReferenceCount();
    }
}
