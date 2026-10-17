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
        var probe = new ForwardPlusScene(engine.Tree);
        probe.Environment.SdfgiCascades = 1;
        probe.Environment.SdfgiReadSkyLight = false;
        probe.Environment.VolumetricFogDensity = 0.1f;
        probe.Environment.VolumetricFogEmission = new Color(1, 0.1f, 0.05f);
        probe.Environment.VolumetricFogEmissionEnergy = 1;
        probe.Environment.VolumetricFogLength = 16;
        probe.Environment.VolumetricFogTemporalReprojectionEnabled = false;
        int phase = 0, frames = 0;
        bool validated = false;
        probe.SelectStage(0);
        engine.Run(() =>
        {
            int requested = (int)(double)JavaScriptBridge.Eval("Number(document.documentElement.dataset.requestPhase || 0)");
            if (requested != phase && requested < 7)
            {
                phase = requested;
                frames = 0;
                probe.SelectStage(phase < 4 ? phase : phase == 5 ? 3 : 0);
                probe.Environment.VolumetricFogEnabled = phase == 4;
                probe.Environment.SdfgiEnabled = phase == 5;
                probe.SetIndirectProbe(phase == 5);
                engine.Tree.Root.Msaa3D = phase == 6 ? Viewport.Msaa.Msaa4X : Viewport.Msaa.Disabled;
            }
            if (++frames == (phase == 5 ? 120 : 24))
                JavaScriptBridge.Eval($"document.documentElement.dataset.readyPhase='{phase}'");
            if (requested == 7 && !validated)
            {
                var samples = JavaScriptBridge.Eval("document.documentElement.dataset.samples").AsString().Split(',');
                for (int i = 0; i < 4; i++)
                    probe.Record(i, double.Parse(samples[i], System.Globalization.CultureInfo.InvariantCulture));
                probe.AssertLighting();
                validated = true;
                GD.Print("BROWSER_WEBGPU_FORWARD_PLUS_SMOKE_PASSED");
                JavaScriptBridge.Eval("document.documentElement.dataset.twodogSmoke='passed'");
            }
        });
    }
}
