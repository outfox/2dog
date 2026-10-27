using Godot;
using Engine = twodog.Engine;

internal static class ForwardEffects
{
    // The engine warns once per process, so the second lifetime sees no warning.
    private static bool _sdfgiUnavailable;

    public static void Run(Engine engine, LightingScene scene)
    {
        var environment = scene.Environment;
        environment.VolumetricFogDensity = 0.1f;
        environment.VolumetricFogEmission = new Color(1, 0.1f, 0.05f);
        environment.VolumetricFogEmissionEnergy = 1;
        environment.VolumetricFogLength = 16;
        environment.VolumetricFogTemporalReprojectionEnabled = false;
        environment.SdfgiCascades = 1;
        environment.SdfgiReadSkyLight = false;
        // The far blur starts behind the box (4 units away), so every depth of field pass runs while the lit target stays
        // sharp; adapters without tier2 read-write storage take the raster passes here.
        var dof = new CameraAttributesPractical { DofBlurFarEnabled = true, DofBlurFarDistance = 6, DofBlurFarTransition = 1, DofBlurAmount = 0.2f };
        string[] effects = ["fog", "ssao", "ssil", "ssr", "taa", "sdfgi", "dof", "fsr1", "msaa"];
        foreach (string effect in effects)
        {
            scene.SelectStage(effect == "sdfgi" ? 3 : 0);
            scene.Camera.Attributes = effect == "dof" ? dof : null;
            scene.SetIndirectProbe(effect == "sdfgi");
            environment.VolumetricFogEnabled = effect == "fog";
            environment.SsaoEnabled = effect == "ssao";
            environment.SsilEnabled = effect == "ssil";
            environment.SsrEnabled = effect == "ssr";
            environment.SdfgiEnabled = effect == "sdfgi";
            engine.Tree.Root.UseTaa = effect == "taa";
            engine.Tree.Root.Scaling3DMode = effect == "fsr1" ? Viewport.Scaling3DModeEnum.Fsr : Viewport.Scaling3DModeEnum.Bilinear;
            engine.Tree.Root.Scaling3DScale = effect == "fsr1" ? 0.5f : 1;
            engine.Tree.Root.Msaa3D = effect == "msaa" ? Viewport.Msaa.Msaa4X : Viewport.Msaa.Disabled;
            Console.WriteLine("FORWARD_EFFECT_BEGIN " + effect);
            for (int frame = 0; frame < (effect == "sdfgi" ? 120 : 24); frame++)
            {
                engine.Update();
                Thread.Sleep(20);
            }
            using var pixels = engine.Tree.Root.GetTexture().GetImage();
            var pixel = pixels.GetPixel(2, 2);
            Console.WriteLine($"FORWARD_EFFECT_PIXEL {effect} {pixel}");
            if (effect == "fog" && pixel.R < 0.05f) throw new Exception("Volumetric fog did not illuminate background pixels.");
            var errors = engine.Errors.Drain();
            // The bokeh composite weighs the box edges against the background, so the blurred target reads about 0.48
            // on the compute and raster paths alike.
            if (effect != "sdfgi" && pixels.GetPixel(pixels.GetWidth() / 2, pixels.GetHeight() / 2).R < (effect == "dof" ? 0.3f : 0.5f))
                throw new Exception($"{effect} did not preserve the lit target: {pixels.GetPixel(pixels.GetWidth() / 2, pixels.GetHeight() / 2)}");
            if (effect == "dof") Console.WriteLine($"FORWARD_PLUS_DOF_PIXEL {pixels.GetPixel(pixels.GetWidth() / 2, pixels.GetHeight() / 2)}");
            if (effect == "sdfgi")
            {
                var indirect = pixels.GetPixel(pixels.GetWidth() / 2, pixels.GetHeight() / 2);
                Console.WriteLine($"FORWARD_PLUS_INDIRECT_PIXEL {indirect}");
                // Without RGBA8/16/32 read-write storage (WebGPU texture-formats-tier2), Forward+ warns once and runs without SDFGI.
                if (errors.Any(error => error.ToString().Contains("SDFGI is unavailable on this platform"))) _sdfgiUnavailable = true;
                if (_sdfgiUnavailable)
                {
                    errors = errors.Where(error => !error.ToString().Contains("SDFGI is unavailable on this platform")).ToArray();
                    if (indirect.B > 0.025f) throw new Exception("SDFGI was reported unavailable but still lit the target.");
                    Console.WriteLine("FORWARD_PLUS_SDFGI_UNAVAILABLE");
                }
                else if (indirect.B < 0.025f || indirect.B <= indirect.R + 0.01f)
                    throw new Exception("SDFGI did not transfer blue emission to the unlit target surface.");
            }
            if (errors.Length != 0) throw new Exception(string.Join("\n", errors.Select(error => error.ToString())));
            Console.WriteLine("FORWARD_EFFECT_RENDERED " + effect);
        }
        environment.SdfgiEnabled = false;
        scene.Camera.Attributes = null;
        engine.Tree.Root.Msaa3D = Viewport.Msaa.Disabled;
        engine.Tree.Root.Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear;
        engine.Tree.Root.Scaling3DScale = 1;
    }
}
