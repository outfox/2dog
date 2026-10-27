using Godot;
using Engine = twodog.Engine;

internal static class ShadowPixelSmoke
{
    public static void Run(Engine engine)
    {
        var root = new Node3D();
        using var material = new StandardMaterial3D { AlbedoColor = Colors.White, Roughness = 1, MetallicSpecular = 0 };
        using var environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Black };
        root.AddChild(new WorldEnvironment { Environment = environment });
        root.AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(6, 6) }, MaterialOverride = material });
        root.AddChild(new MeshInstance3D { Mesh = new BoxMesh(), Position = new Vector3(0, 0.5f, 0), MaterialOverride = material });
        var light = new DirectionalLight3D { RotationDegrees = new Vector3(-50, -30, 0) };
        root.AddChild(light);
        var camera = new Camera3D { Position = new Vector3(3, 3, 5), Current = true };
        root.AddChild(camera);
        engine.Tree.Root.AddChild(root);
        camera.LookAt(Vector3.Zero);
        try
        {
            Pump(engine);
            using var unshadowed = engine.Tree.Root.GetTexture().GetImage();
            light.ShadowEnabled = true;
            Pump(engine);
            using var shadowed = engine.Tree.Root.GetTexture().GetImage();
            int darkerPixels = 0;
            for (int y = 0; y < shadowed.GetHeight(); y++)
                for (int x = 0; x < shadowed.GetWidth(); x++)
                    if (unshadowed.GetPixel(x, y).R - shadowed.GetPixel(x, y).R > 0.1f) darkerPixels++;
            if (darkerPixels < 20) throw new Exception($"Enabling shadows did not darken the receiving plane: {darkerPixels} pixels.");
            Console.WriteLine($"NATIVE_SHADOW_PIXELS_PASSED: {darkerPixels}");
        }
        finally { root.Free(); }
    }

    private static void Pump(Engine engine)
    {
        for (int frame = 0; frame < 16; frame++)
        {
            if (!engine.Update()) throw new Exception("Unexpected engine quit while rendering shadows.");
            Thread.Sleep(20);
        }
    }
}
