using Godot;
using Engine = twodog.Engine;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var driver = args.FirstOrDefault() ?? "webgpu";
        var method = args.ElementAtOrDefault(1)
            ?? (driver == "opengl3" ? "gl_compatibility" : driver == "webgpu" ? "default" : "mobile");
        for (int lifetime = 0; lifetime < 2; lifetime++)
        {
            string[] options = ["--rendering-driver", driver, "--audio-driver", "Dummy"];
            if (method != "default") options = [.. options, "--rendering-method", method];
            if (args.Contains("--project-settings")) options = ["--audio-driver", "Dummy"];
            if (OperatingSystem.IsLinux()) options = [.. options, "--display-driver", "x11"];
            if (args.Contains("--headless")) options = [.. options, "--headless"];
            using var engine = new Engine("NativeWebGpuSmoke", AppContext.BaseDirectory, options) { CaptureErrors = true, NativePath = System.Environment.GetEnvironmentVariable("TWODOG_WEBGPU_NATIVE_PATH") };
            engine.Start();
            if (args.Contains("--startup-only"))
            {
                engine.Dispose();
                engine.Completion.GetAwaiter().GetResult();
                if (engine.Errors.Drain().Length != 0) throw new Exception("Unexpected startup errors.");
                Console.WriteLine("NATIVE_RENDERER_STARTUP_PASSED");
                return;
            }
            if (RenderingServer.GetCurrentRenderingDriverName() != driver)
                throw new Exception($"{driver} selection fell back to another rendering driver.");
            if (method == "default" && driver == "webgpu"
                && RenderingServer.GetCurrentRenderingMethod() != "mobile")
                throw new Exception("An implicit WebGPU rendering method must select Mobile.");
            if (args.Contains("--contract-rejections"))
            {
                ContractRejections.Run(engine);
                engine.Dispose();
                engine.Completion.GetAwaiter().GetResult();
                if (engine.Errors.Drain().Length != 0) throw new Exception("Unexpected errors after capability probes.");
                Console.WriteLine("NATIVE_WEBGPU_CONTRACT_REJECTIONS_PASSED");
                return;
            }
            if (driver == "webgpu")
            {
                DescriptorArraySmoke.Run();
                ShaderBakingSmoke.Run();
                ShaderControlFlowSmoke.Run();
                TextureSwizzleSmoke.Run();
            }
            if (RenderingServer.GetCurrentRenderingMethod() is "forward_plus" or "mobile")
            {
                using var probe = new LightingScene(engine.Tree);
                bool forwardPlus = RenderingServer.GetCurrentRenderingMethod() == "forward_plus";
                for (int stage = 0; stage < (forwardPlus ? 4 : LightingScene.StageCount); stage++)
                {
                    probe.SelectStage(stage);
                    for (int frame = 0; frame < 12; frame++)
                    {
                        if (engine.Iteration()) throw new Exception("Unexpected engine quit.");
                        Thread.Sleep(20);
                    }
                    using var pixels = engine.Tree.Root.GetTexture().GetImage();
                    var pixel = pixels.GetPixel(pixels.GetWidth() / 2, pixels.GetHeight() / 2);
                    if (stage < 4) probe.Record(stage, (pixel.R + pixel.G + pixel.B) / 3.0);
                    else
                    {
                        if (stage == 4 && (pixel.R < 0.5f || pixel.R <= pixel.B + 0.2f))
                            throw new Exception($"Depth fog did not produce a red target: {pixel}.");
                        if (stage == 5 && (pixel.B < 0.025f || pixel.B <= pixel.R + 0.01f))
                            throw new Exception($"Ambient light did not produce a blue target: {pixel}.");
                        if (stage == 6 && pixel.R < 0.5f)
                            throw new Exception($"MSAA lost the lit target: {pixel}.");
                        if (stage == 7 && (pixel.R < 0.5f || pixel.B < 0.5f || pixel.G > 0.1f))
                            throw new Exception($"User varyings did not produce a magenta target: {pixel}.");
                        if (stage == 8 && (pixel.G < 0.2f || pixel.G <= pixel.R + 0.15f))
                            throw new Exception($"The textured lightmapped material did not produce a green target: {pixel}.");
                        if (stage == 9 && (pixel.R < 0.5f || pixel.G < 0.5f || pixel.B > 0.1f))
                            throw new Exception($"Point coordinates did not produce a yellow target: {pixel}.");
                        if (stage == 10 && pixel.R < 0.1f)
                            throw new Exception($"The depth-texture material sampled the zero fallback: {pixel}.");
                        if (stage == 11 && (pixel.R < 0.8f || pixel.G < 0.3f || pixel.G > 0.8f || pixel.B > 0.1f))
                            throw new Exception($"DXT5 RA_AS_RG material lost its channels: {pixel}.");
                        if (stage == 12 && (pixel.G < 0.3f || pixel.R > 0.2f || pixel.B > 0.1f))
                            throw new Exception($"ETC2 RA_AS_RG material lost its channels: {pixel}.");
                        Console.WriteLine($"MOBILE_EFFECT_PIXEL stage={stage} {pixel}");
                    }
                }
                probe.AssertLighting();
                if (forwardPlus) ForwardEffects.Run(engine, probe);
            }
            ShadowPixelSmoke.Run(engine);
            if (RenderingServer.GetCurrentRenderingMethod() == "forward_plus") SsaoPixelSmoke.Run(engine);
            var root = new Node3D();
            engine.Tree.Root.AddChild(root);
            root.AddChild(new MeshInstance3D { Mesh = new BoxMesh() });
            root.AddChild(new Camera3D { Position = new Vector3(0, 0, 3), Current = true });
            root.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-30, -30, 0) });
            for (int frame = 0; frame < 8; frame++)
            {
                if (engine.Iteration()) throw new Exception("Unexpected engine quit.");
                Thread.Sleep(20);
            }
            using (var image = engine.Tree.Root.GetTexture().GetImage())
            {
                if (image is null || image.IsEmpty()) throw new Exception("WebGPU returned no viewport pixels.");
                var center = image.GetPixel(image.GetWidth() / 2, image.GetHeight() / 2);
                var corner = image.GetPixel(0, 0);
                if (center.IsEqualApprox(corner)) throw new Exception("WebGPU did not render distinct object/background pixels.");
            }
            CanvasViewportSmoke.Run(engine);
            foreach (var format in new[] { Image.Format.L8, Image.Format.La8 })
            {
                byte[] pixels = format == Image.Format.L8 ? [64, 128, 192, 255] : [64, 128, 128, 192, 192, 64, 255, 255];
                using var source = Image.CreateFromData(2, 2, false, format, pixels);
                using var texture = ImageTexture.CreateFromImage(source);
                using var restored = texture.GetImage();
                var expected = source.GetPixel(0, 0);
                var actual = restored.GetPixel(0, 0);
                if (Math.Abs(actual.R - expected.R) > 0.01 || Math.Abs(actual.G - expected.G) > 0.01 ||
                    Math.Abs(actual.B - expected.B) > 0.01 || Math.Abs(actual.A - expected.A) > 0.01)
                    throw new Exception($"{format} texture readback changed: {expected} -> {actual}");
            }
            engine.Dispose();
            engine.Completion.GetAwaiter().GetResult();
            var errors = engine.Errors.Drain();
            if (errors.Length != 0) throw new Exception(string.Join("\n", errors.Select(error => error.ToString())));
            Console.WriteLine($"NATIVE_{driver.ToUpperInvariant()}_SMOKE_PASSED: lifetime {lifetime + 1}");
        }
    }
}
