using Godot;
using Engine = twodog.Engine;

internal static class PassRestartPixelSmoke
{
    // The WebGPU driver re-opens a render pass when its push constant ring (8192 slots) fills up. That once dropped
    // the multisampled depth buffer, so transparent meshes drawn after it showed through opaque ones.
    public static void Run(Engine engine)
    {
        var viewport = engine.Tree.Root;
        var root = new Node3D();
        using var environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Black };
        root.AddChild(new WorldEnvironment { Environment = environment });
        root.AddChild(new Camera3D { Position = new Vector3(0, 0, 10), Current = true });
        using var quad = new QuadMesh { Size = new Vector2(0.1f, 0.1f) };
        using var wall = new QuadMesh { Size = new Vector2(6, 3) };
        // A material each makes every quad its own draw. In Forward+ the opaque ones are drawn twice (depth, then
        // colour), which fills most of the ring, and the ring then runs out among the transparent ones.
        for (int i = 0; i < 3900; i++) Add(root, quad, new Vector3(-4 + i % 160 * 0.05f, -3.5f + i / 160 * 0.05f, 1), new Color(0, 0, 0.5f), false);
        Add(root, wall, new Vector3(0, 1, 0), Colors.Red, false);
        for (int i = 0; i < 600; i++) Add(root, quad, new Vector3(-2.5f + i % 50 * 0.1f, 0.2f + i / 50 * 0.1f, -1), new Color(0, 1, 0, 0.99f), true);
        viewport.AddChild(root);
        var msaa = viewport.Msaa3D;
        viewport.Msaa3D = Viewport.Msaa.Msaa4X;
        try
        {
            for (int frame = 0; frame < 16; frame++)
            {
                if (!engine.Update()) throw new Exception("Unexpected engine quit while rendering the pass restart scene.");
                Thread.Sleep(20);
            }
            using var image = viewport.GetTexture().GetImage();
            int hiddenPixels = 0, wallPixels = 0;
            for (int y = 0; y < image.GetHeight(); y++)
                for (int x = 0; x < image.GetWidth(); x++)
                {
                    var pixel = image.GetPixel(x, y);
                    if (pixel.G > 0.5f && pixel.R < 0.5f) hiddenPixels++;
                    if (pixel.R > 0.5f && pixel.G < 0.5f) wallPixels++;
                }
            if (wallPixels < 100) throw new Exception($"The opaque wall was not rendered: {wallPixels} pixels.");
            if (hiddenPixels > 0) throw new Exception($"Transparent meshes behind an opaque wall showed through it: {hiddenPixels} pixels.");
            Console.WriteLine($"NATIVE_PASS_RESTART_PIXELS_PASSED: {wallPixels}");
        }
        finally
        {
            viewport.Msaa3D = msaa;
            root.Free();
        }
    }

    private static void Add(Node3D root, Mesh mesh, Vector3 position, Color color, bool transparent)
    {
        var material = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = color };
        if (transparent) material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        root.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = position });
    }
}
