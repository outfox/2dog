using Godot;
using Engine = twodog.Engine;

internal static class SsaoPixelSmoke
{
    public static void Run(Engine engine)
    {
        var root = new Node3D();
        using var material = new StandardMaterial3D { AlbedoColor = Colors.White, Roughness = 1, MetallicSpecular = 0 };
        using var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Black,
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White,
            AmbientLightEnergy = 1, AmbientLightSkyContribution = 0,
            SsaoRadius = 2, SsaoIntensity = 5, SsaoPower = 2,
        };
        root.AddChild(new WorldEnvironment { Environment = environment });
        root.AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(6, 6) }, MaterialOverride = material });
        root.AddChild(new MeshInstance3D { Mesh = new BoxMesh(), Position = new Vector3(0, 0.5f, 0), MaterialOverride = material });
        var camera = new Camera3D { Position = new Vector3(3, 3, 5), Current = true };
        root.AddChild(camera);
        engine.Tree.Root.AddChild(root);
        camera.LookAt(Vector3.Zero);
        try
        {
            Pump(engine);
            using var unshadowed = engine.Tree.Root.GetTexture().GetImage();
            environment.SsaoEnabled = true;
            Pump(engine);
            using var shadowed = engine.Tree.Root.GetTexture().GetImage();
            int darkerPixels = 0;
            for (int y = 0; y < shadowed.GetHeight(); y++)
                for (int x = 0; x < shadowed.GetWidth(); x++)
                    if (unshadowed.GetPixel(x, y).R - shadowed.GetPixel(x, y).R > 0.03f) darkerPixels++;
            if (darkerPixels < 10) throw new Exception($"Enabling SSAO did not darken the receiving plane: {darkerPixels} pixels.");
            var exposed = camera.UnprojectPosition(new Vector3(2, 0, 2));
            if (Math.Abs(unshadowed.GetPixel((int)exposed.X, (int)exposed.Y).R - shadowed.GetPixel((int)exposed.X, (int)exposed.Y).R) > 0.03f)
                throw new Exception("SSAO darkened the exposed part of the plane.");
            Console.WriteLine($"NATIVE_SSAO_PIXELS_PASSED: {darkerPixels}");
        }
        finally { root.Free(); }
    }

    private static void Pump(Engine engine)
    {
        for (int frame = 0; frame < 16; frame++)
        {
            if (!engine.Update()) throw new Exception("Unexpected engine quit while rendering SSAO.");
            Thread.Sleep(20);
        }
    }
}
