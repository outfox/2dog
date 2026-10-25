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
        var method = RenderingServer.GetCurrentRenderingMethod();
        if (RenderingServer.GetCurrentRenderingDriverName() != "webgpu" || method is not ("mobile" or "forward_plus"))
            throw new Exception("WebGPU Forward+ or Mobile was not selected.");
        // The page script compares this with the method its adapter is expected to run.
        JavaScriptBridge.Eval($"document.documentElement.dataset.renderingMethod='{method}'");
        bool forwardPlus = method == "forward_plus";
        // Forward+ phases: four light-list samples, volumetric fog, SDFGI and MSAA.
        int phaseCount = forwardPlus ? 7 : LightingScene.StageCount;
        var probe = new LightingScene(engine.Tree);
        if (forwardPlus)
        {
            probe.Environment.SdfgiCascades = 1;
            probe.Environment.SdfgiReadSkyLight = false;
            probe.Environment.VolumetricFogDensity = 0.1f;
            probe.Environment.VolumetricFogEmission = new Color(1, 0.1f, 0.05f);
            probe.Environment.VolumetricFogEmissionEnergy = 1;
            probe.Environment.VolumetricFogLength = 16;
            probe.Environment.VolumetricFogTemporalReprojectionEnabled = false;
        }
        int phase = 0, frames = 0;
        bool validated = false;
        probe.SelectStage(0);
        engine.Run(() =>
        {
            int requested = (int)(double)JavaScriptBridge.Eval("Number(document.documentElement.dataset.requestPhase || 0)");
            if (requested != phase && requested < phaseCount)
            {
                phase = requested;
                frames = 0;
                if (forwardPlus)
                {
                    probe.SelectStage(phase == 4 ? 0 : phase == 5 ? 3 : phase);
                    probe.Environment.VolumetricFogEnabled = phase == 4;
                    probe.Environment.SdfgiEnabled = phase == 5;
                    probe.SetIndirectProbe(phase == 5);
                }
                else probe.SelectStage(phase);
            }
            if (++frames == (forwardPlus && phase == 5 ? 120 : 24))
                JavaScriptBridge.Eval($"document.documentElement.dataset.readyPhase='{phase}'");
            if (requested == phaseCount && !validated)
            {
                var samples = JavaScriptBridge.Eval("document.documentElement.dataset.samples").AsString().Split(',');
                for (int i = 0; i < 4; i++)
                    probe.Record(i, double.Parse(samples[i], System.Globalization.CultureInfo.InvariantCulture));
                probe.AssertLighting();
                validated = true;
                GD.Print(forwardPlus ? "BROWSER_WEBGPU_FORWARD_PLUS_SMOKE_PASSED" : "BROWSER_WEBGPU_MOBILE_SMOKE_PASSED");
                JavaScriptBridge.Eval("document.documentElement.dataset.twodogSmoke='passed'");
            }
        });
    }
}
