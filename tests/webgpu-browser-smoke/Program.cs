using Godot;
using Engine = twodog.Engine;

namespace GodotPlugins.Game
{
    internal static partial class Main
    {
        internal static unsafe nint PluginsInitializer() =>
            (nint)(delegate* unmanaged<nint, nint, nint, int, Godot.NativeInterop.godot_bool>)&InitializeFromGameProject;
    }
}

internal static class Program
{
    private static void Main(string[] args)
    {
        Engine.RegisterWebPluginsInitializer(GodotPlugins.Game.Main.PluginsInitializer());
        var engine = new Engine("WebGpuBrowserSmoke", args: args);
        engine.Start();
        if (RenderingServer.GetCurrentRenderingDriverName() != "webgpu" || RenderingServer.GetCurrentRenderingMethod() != "mobile")
            throw new Exception("WebGPU Mobile was not selected.");
        var probe = new LightingScene(engine.Tree);
        int phase = 0, frames = 0;
        bool validated = false;
        probe.SelectStage(0);
        engine.Run(() =>
        {
            int requested = (int)(double)JavaScriptBridge.Eval("Number(document.documentElement.dataset.requestPhase || 0)");
            if (requested != phase && requested < 9)
            {
                phase = requested;
                frames = 0;
                probe.SelectStage(phase);
            }
            if (++frames == 24)
                JavaScriptBridge.Eval($"document.documentElement.dataset.readyPhase='{phase}'");
            if (requested == 9 && !validated)
            {
                var samples = JavaScriptBridge.Eval("document.documentElement.dataset.samples").AsString().Split(',');
                for (int i = 0; i < 4; i++)
                    probe.Record(i, double.Parse(samples[i], System.Globalization.CultureInfo.InvariantCulture));
                probe.AssertLighting();
                validated = true;
                GD.Print("BROWSER_WEBGPU_MOBILE_SMOKE_PASSED");
                JavaScriptBridge.Eval("document.documentElement.dataset.twodogSmoke='passed'");
            }
        });
    }
}
