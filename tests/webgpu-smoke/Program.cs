using Godot;
using Engine = twodog.Engine;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var driver = args.FirstOrDefault() ?? "webgpu";
        var method = driver == "opengl3" ? "gl_compatibility" : "mobile";
        for (int lifetime = 0; lifetime < 2; lifetime++)
        {
            string[] options = ["--rendering-driver", driver, "--rendering-method", method, "--audio-driver", "Dummy"];
            if (OperatingSystem.IsLinux()) options = [.. options, "--display-driver", "x11"];
            using var engine = new Engine("NativeWebGpuSmoke", AppContext.BaseDirectory, options) { CaptureErrors = true };
            engine.Start();
            if (RenderingServer.GetCurrentRenderingDriverName() != driver)
                throw new Exception($"{driver} selection fell back to another rendering driver.");
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
