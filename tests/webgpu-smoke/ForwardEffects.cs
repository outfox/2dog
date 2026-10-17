using Godot;
using Engine = twodog.Engine;

internal static class ForwardEffects
{
    public static void Run(Engine engine, ForwardPlusScene scene)
    {
        var environment = scene.Environment;
        environment.VolumetricFogDensity = 0.1f;
        environment.VolumetricFogEmission = new Color(1, 0.1f, 0.05f);
        environment.VolumetricFogEmissionEnergy = 1;
        environment.VolumetricFogLength = 16;
        environment.VolumetricFogTemporalReprojectionEnabled = false;
        environment.SdfgiCascades = 1;
        environment.SdfgiReadSkyLight = false;
        string[] effects = ["fog", "ssao", "ssil", "ssr", "taa", "sdfgi", "fsr1"];
        foreach (string effect in effects)
        {
            scene.SelectStage(effect == "sdfgi" ? 3 : 0);
            scene.SetIndirectProbe(effect == "sdfgi");
            environment.VolumetricFogEnabled = effect == "fog";
            environment.SsaoEnabled = effect == "ssao";
            environment.SsilEnabled = effect == "ssil";
            environment.SsrEnabled = effect == "ssr";
            environment.SdfgiEnabled = effect == "sdfgi";
            engine.Tree.Root.UseTaa = effect == "taa";
            engine.Tree.Root.Scaling3DMode = effect == "fsr1" ? Viewport.Scaling3DModeEnum.Fsr : Viewport.Scaling3DModeEnum.Bilinear;
            engine.Tree.Root.Scaling3DScale = effect == "fsr1" ? 0.5f : 1;
            Console.WriteLine("FORWARD_EFFECT_BEGIN " + effect);
            for (int frame = 0; frame < (effect == "sdfgi" ? 120 : 24); frame++)
            {
                engine.Iteration();
                Thread.Sleep(20);
            }
            using var pixels = engine.Tree.Root.GetTexture().GetImage();
            var pixel = pixels.GetPixel(2, 2);
            Console.WriteLine($"FORWARD_EFFECT_PIXEL {effect} {pixel}");
            if (effect == "fog" && pixel.R < 0.05f) throw new Exception("Volumetric fog did not illuminate background pixels.");
            if (effect == "sdfgi")
            {
                var indirect = pixels.GetPixel(pixels.GetWidth() / 2, pixels.GetHeight() / 2);
                Console.WriteLine($"FORWARD_PLUS_INDIRECT_PIXEL {indirect}");
                if (indirect.B < 0.025f || indirect.B <= indirect.R + 0.01f)
                    throw new Exception("SDFGI did not transfer blue emission to the unlit target surface.");
            }
            var errors = engine.Errors.Drain();
            if (errors.Length != 0) throw new Exception(string.Join("\n", errors.Select(error => error.ToString())));
            Console.WriteLine("FORWARD_EFFECT_RENDERED " + effect);
        }
        environment.SdfgiEnabled = false;
        engine.Tree.Root.Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear;
        engine.Tree.Root.Scaling3DScale = 1;
    }
}