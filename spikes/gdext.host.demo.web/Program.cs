// Browser-wasm demo for the 2dog.gdextension host: the non-mono engine is
// statically linked into the .NET wasm module, C# extension classes register
// through the embedded GDExtension init callback, and emscripten owns the
// main loop after Engine.Run().

using Godot;
using Engine = twodog.Engine;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.WriteLine("2dog gdext web demo starting...");

        // No ClassRegistry.Register call: Spinner registers via the source
        // generator's module initializer (the ClassExists check below covers it).

        // args come from the JS shell (GODOT_CONFIG.args plus the
        // '--main-pack godot.pck' the engine loader prepends).
        var engine = new Engine("gdext.host.demo.web", null, args);
        engine.Start();

        Spinner spinner;
        try
        {
            GD.Print((Variant)"Hello from twodog.bindings (browser).");
            var tree = engine.Tree;
            GD.Print((Variant)$"Scene root: {tree.Root!.Name}");

            spinner = new Spinner { Name = "spinner" };
            tree.Root.AddChild(spinner);
            if (!ClassDB.ClassExists("Spinner") || spinner.GetClass() != "Spinner")
                throw new InvalidOperationException("Spinner did not register as an extension class.");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"2DOG_GDEXT_WASM_SMOKE_FAILED: {exception}");
            MarkSmoke("failed");
            throw;
        }

        // Frame-dependent checks run in the emscripten loop: after 60 frames
        // the registered class must have received _Process callbacks.
        var reported = false;
        engine.Run(() =>
        {
            if (reported || spinner.Frames < 60) return;
            reported = true;
            var ok = spinner.Rotation != 0f;
            if (ok)
            {
                Console.WriteLine("2DOG_GDEXT_WASM_SMOKE_PASSED");
                MarkSmoke("passed");
            }
            else
            {
                Console.Error.WriteLine("2DOG_GDEXT_WASM_SMOKE_FAILED: _Process ran but property writes did not stick");
                MarkSmoke("failed");
            }
        });

        // Run() hands the loop to emscripten and returns immediately; the
        // engine destroys itself when Godot requests quit. Do not dispose here.
        return 0;
    }

    private static void MarkSmoke(string state) =>
        JavaScriptBridge.Eval($"document.documentElement.setAttribute('data-twodog-smoke', '{state}')", false);
}

/// <summary>A user node exactly as a desktop 2dog.gdextension user would write it.</summary>
public partial class Spinner : Node2D
{
    public int Frames;

    public override void _Process(double delta)
    {
        Frames++;
        Rotation += (float)delta;
    }
}
