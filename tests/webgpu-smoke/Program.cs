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
            if (OperatingSystem.IsLinux()) options = [.. options, "--display-driver", "x11"];
            using var engine = new Engine("NativeWebGpuSmoke", AppContext.BaseDirectory, options) { CaptureErrors = true, NativePath = System.Environment.GetEnvironmentVariable("TWODOG_WEBGPU_NATIVE_PATH") };
            engine.Start();
            if (RenderingServer.GetCurrentRenderingDriverName() != driver)
                throw new Exception($"{driver} selection fell back to another rendering driver.");
            if (method == "default" && driver == "webgpu"
                && RenderingServer.GetCurrentRenderingMethod() != "forward_plus")
                throw new Exception("An implicit WebGPU rendering method must select Forward+.");
            if (driver == "webgpu") DescriptorArraySmoke.Run();
            if (RenderingServer.GetCurrentRenderingMethod() == "forward_plus")
            {
                using var probe = new ForwardPlusScene(engine.Tree);
                for (int stage = 0; stage < 4; stage++)
                {
                    probe.SelectStage(stage);
                    for (int frame = 0; frame < 12; frame++)
                    {
                        if (engine.Iteration()) throw new Exception("Unexpected engine quit.");
                        Thread.Sleep(20);
                    }
                    using var pixels = engine.Tree.Root.GetTexture().GetImage();
                    var pixel = pixels.GetPixel(pixels.GetWidth() / 2, pixels.GetHeight() / 2);
                    probe.Record(stage, (pixel.R + pixel.G + pixel.B) / 3.0);
                }
                probe.AssertLighting();
                ForwardEffects.Run(engine, probe);
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
