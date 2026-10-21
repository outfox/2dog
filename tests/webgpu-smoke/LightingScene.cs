using Godot;

// Both halves of the light list must contribute on the selected renderer.
internal sealed class LightingScene : IDisposable
{
    private readonly Node3D _root = new();
    private readonly OmniLight3D[] _lights;
    private readonly Window _viewport;
    private StandardMaterial3D? _emitterMaterial;
    private MeshInstance3D? _emitter;
    private readonly double[] _luminance = new double[4];
    private readonly StandardMaterial3D _material = new()
    {
        AlbedoColor = Colors.White,
        Roughness = 1,
        MetallicSpecular = 0,
    };
    private readonly Godot.Environment _environment = new()
    {
        BackgroundMode = Godot.Environment.BGMode.Color,
        BackgroundColor = Colors.Black,
        AmbientLightSource = Godot.Environment.AmbientSource.Color,
        AmbientLightColor = Colors.Blue,
        AmbientLightEnergy = 0,
        AmbientLightSkyContribution = 0,
        FogLightColor = Colors.Red,
        FogDensity = 1,
    };

    public LightingScene(SceneTree tree)
    {
        var method = RenderingServer.GetCurrentRenderingMethod();
        if (method is not ("mobile" or "forward_plus"))
            throw new Exception("A rendering-device renderer was not selected.");
        if (method == "forward_plus" && RenderingServer.GetRenderingDevice().LimitGet(RenderingDevice.Limit.MaxTexturesPerShaderStage) < 48)
            throw new Exception("Forward+ requires at least 48 sampled textures per shader stage.");
        _lights = new OmniLight3D[method == "mobile" ? 8 : 32];
        _viewport = tree.Root;
        tree.Root.AddChild(_root);
        _root.AddChild(new WorldEnvironment { Environment = _environment });
        _root.AddChild(new MeshInstance3D { Mesh = new BoxMesh(), MaterialOverride = _material });
        _root.AddChild(new Camera3D { Position = new Vector3(0, 0, 4), Current = true });
        for (int i = 0; i < _lights.Length; i++)
        {
            _lights[i] = new OmniLight3D
            {
                Position = new Vector3(0, 0, 2),
                OmniRange = 4,
                LightEnergy = 0.64f / _lights.Length,
            };
            _root.AddChild(_lights[i]);
        }
    }

    public Godot.Environment Environment => _environment;

    public string StageName(int stage) => stage switch
    {
        0 => $"all_{_lights.Length}_lights",
        1 => $"first_{_lights.Length / 2}_lights",
        2 => $"last_{_lights.Length / 2}_lights",
        3 => "lights_out_of_range",
        _ => throw new ArgumentOutOfRangeException(nameof(stage)),
    };

    public void SelectStage(int stage)
    {
        _environment.FogEnabled = stage == 4;
        _environment.AmbientLightEnergy = stage == 5 ? 1 : 0;
        _viewport.Msaa3D = stage == 6 ? Viewport.Msaa.Msaa4X : Viewport.Msaa.Disabled;
        for (int i = 0; i < _lights.Length; i++)
        {
            _lights[i].Visible = stage switch { 1 => i < _lights.Length / 2, 2 => i >= _lights.Length / 2, _ => true };
            _lights[i].Position = stage is 3 or 5 ? new Vector3(100, 0, 2) : new Vector3(0, 0, 2);
        }
    }

    public void SetIndirectProbe(bool enabled)
    {
        if (_emitter is null && enabled)
        {
            _emitterMaterial = new StandardMaterial3D
            {
                AlbedoColor = Colors.Black,
                EmissionEnabled = true,
                Emission = Colors.Blue,
                EmissionEnergyMultiplier = 4,
            };
            _emitter = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.25f, 2, 1) },
                MaterialOverride = _emitterMaterial,
                Position = new Vector3(1.2f, 0, 1.0f),
                GIMode = GeometryInstance3D.GIModeEnum.Static,
            };
            _root.AddChild(_emitter);
        }
        if (_emitter is not null) _emitter.Visible = enabled;
    }
    public void Record(int stage, double luminance)
    {
        _luminance[stage] = luminance;
        Console.WriteLine($"LIGHTING_PIXEL {StageName(stage)}={luminance:F6}");
    }

    public void AssertLighting()
    {
        double all = _luminance[0], first = _luminance[1], last = _luminance[2], outside = _luminance[3];
        if (first < 0.02 || last < 0.02)
            throw new Exception($"Both light-list halves must illuminate the surface: first={first}, last={last}.");
        if (all < Math.Max(first, last) * 1.15)
            throw new Exception($"All {_lights.Length} lights must contribute: all={all}, first={first}, last={last}.");
        if (Math.Abs(first - last) > 0.025)
            throw new Exception($"Equal halves of the light list produced different lighting: {first}, {last}.");
        if (outside > 0.015)
            throw new Exception($"Out-of-range lights illuminated the surface: {outside}.");
        Console.WriteLine("LIGHT_LIST_PIXELS_PASSED");
    }

    public void Dispose()
    {
        _viewport.Msaa3D = Viewport.Msaa.Disabled;
        _root.Free();
        _emitterMaterial?.Dispose();
        _material.Dispose();
        _environment.Dispose();
    }
}
