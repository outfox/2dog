using Godot;

// Both halves of the light list must contribute on the selected renderer.
internal sealed class LightingScene : IDisposable
{
    public const int StageCount = 13;
    private readonly Node3D _root = new();
    private readonly OmniLight3D[] _lights;
    private readonly Window _viewport;
    private readonly Camera3D _camera = new() { Position = new Vector3(0, 0, 4), Current = true };
    private readonly MeshInstance3D _subject = new() { Name = "Subject" };
    private readonly BoxMesh _boxMesh = new() { AddUV2 = true };
    private ArrayMesh? _pointMesh;
    private ShaderMaterial? _varyingMaterial;
    private ShaderMaterial? _pointMaterial;
    private ShaderMaterial? _depthMaterial;
    private MeshInstance3D? _depthProbe;
    private readonly ShaderMaterial?[] _normalMaterials = new ShaderMaterial?[2];
    private readonly ImageTexture?[] _normalTextures = new ImageTexture?[2];
    private LightmapGI? _lightmap;
    private StandardMaterial3D? _emitterMaterial;
    private MeshInstance3D? _emitter;
    private readonly double[] _luminance = new double[4];
    private readonly StandardMaterial3D _material = new()
    {
        AlbedoColor = Colors.White,
        Roughness = 1,
        MetallicSpecular = 0,
    };
    public Camera3D Camera => _camera;

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
        // WebGPU binds a compact Forward+ scene that fits 40 sampled textures per stage; other drivers need 48.
        long texturesNeeded = RenderingServer.GetCurrentRenderingDriverName() == "webgpu" ? 40 : 48;
        if (method == "forward_plus" && RenderingServer.GetRenderingDevice().LimitGet(RenderingDevice.Limit.MaxTexturesPerShaderStage) < (ulong)texturesNeeded)
            throw new Exception($"Forward+ requires at least {texturesNeeded} sampled textures per shader stage.");
        _lights = new OmniLight3D[method == "mobile" ? 8 : 32];
        _viewport = tree.Root;
        tree.Root.AddChild(_root);
        _root.AddChild(new WorldEnvironment { Environment = _environment });
        _subject.Mesh = _boxMesh;
        _subject.MaterialOverride = _material;
        _root.AddChild(_subject);
        _root.AddChild(_camera);
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
        if (stage == 7 && _varyingMaterial is null)
        {
            _varyingMaterial = new ShaderMaterial
            {
                Shader = new Shader { Code = """
                    shader_type spatial;
                    render_mode unshaded;
                    varying vec3 red;
                    varying vec3 blue;
                    void vertex() { red = vec3(0.8, 0.0, 0.0); blue = vec3(0.0, 0.0, 0.8); }
                    void fragment() { ALBEDO = red + blue; }
                    """ },
            };
        }
        _subject.MaterialOverride = stage == 7 ? _varyingMaterial : _material;
        _subject.Mesh = _boxMesh;
        if (stage == 9)
        {
            _pointMaterial ??= new ShaderMaterial { Shader = new Shader { Code = "shader_type spatial; render_mode unshaded; void vertex() { POINT_SIZE = 32.0; } void fragment() { ALBEDO = vec3(POINT_COORD, 0.0); }" } };
            if (_pointMesh is null)
            {
                var arrays = new Godot.Collections.Array();
                arrays.Resize((int)Mesh.ArrayType.Max);
                arrays[(int)Mesh.ArrayType.Vertex] = new Vector3[] { Vector3.Zero };
                _pointMesh = new ArrayMesh();
                _pointMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Points, arrays);
                arrays.Clear();
            }
            _subject.Mesh = _pointMesh;
            _subject.MaterialOverride = _pointMaterial;
        }
        if (stage == 10 && _depthProbe is null)
        {
            _depthMaterial = new ShaderMaterial { Shader = new Shader { Code = """
                shader_type spatial;
                render_mode unshaded, depth_draw_never, depth_test_disabled;
                uniform sampler2D depth_tex : hint_depth_texture, filter_linear;
                void fragment() { ALBEDO = vec3(textureLod(depth_tex, SCREEN_UV, 0.0).r * 50.0); ALPHA = 1.0; }
                """ } };
            _depthProbe = new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(2, 2) }, MaterialOverride = _depthMaterial, Position = new Vector3(0, 0, 2) };
            _root.AddChild(_depthProbe);
        }
        if (_depthProbe is not null) _depthProbe.Visible = stage == 10;
        if (stage is 11 or 12)
        {
            int index = stage - 11;
            if (_normalMaterials[index] is null)
            {
                using var source = TextureSwizzleSmoke.CreateSource(index == 0 ? Image.Format.Dxt5RaAsRg : Image.Format.Etc2RaAsRg);
                _normalTextures[index] = ImageTexture.CreateFromImage(source);
                _normalMaterials[index] = new ShaderMaterial { Shader = new Shader { Code = "shader_type spatial; render_mode unshaded; uniform sampler2D normal_data; void fragment() { ALBEDO = texture(normal_data, UV).rgb; }" } };
                _normalMaterials[index]!.SetShaderParameter("normal_data", _normalTextures[index]!);
            }
            _subject.MaterialOverride = _normalMaterials[index];
        }
        if (stage == 8 && _lightmap is null)
        {
            using var light = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8);
            light.Fill(Colors.Green);
            var atlas = new Texture2DArray();
            if (atlas.CreateFromImages([light]) != Error.Ok) throw new Exception("Could not create the lightmap atlas.");
            using var white = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8);
            white.Fill(Colors.White);
            var mask = new Texture2DArray();
            if (mask.CreateFromImages([white]) != Error.Ok) throw new Exception("Could not create the shadow mask.");
            _material.AlbedoTexture = ImageTexture.CreateFromImage(white);
            var data = new LightmapGIData { LightmapTextures = [atlas], ShadowmaskTextures = [mask] };
            data.AddUser("../Subject", new Rect2(Vector2.Zero, Vector2.One), 0, -1);
            _lightmap = new LightmapGI { Name = "Lightmap", ShadowmaskMode = LightmapGIData.ShadowmaskMode.Overlay };
            _root.AddChild(_lightmap);
            _lightmap.LightData = data;
        }
        _environment.FogEnabled = stage == 4;
        _environment.AmbientLightEnergy = stage == 5 ? 1 : 0;
        _viewport.Msaa3D = stage == 6 ? Viewport.Msaa.Msaa4X : Viewport.Msaa.Disabled;
        for (int i = 0; i < _lights.Length; i++)
        {
            _lights[i].Visible = stage switch { 1 => i < _lights.Length / 2, 2 => i >= _lights.Length / 2, 8 => false, _ => true };
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
        var data = _lightmap?.LightData;
        var atlases = data?.LightmapTextures;
        var masks = data?.ShadowmaskTextures;
        var albedo = _material.AlbedoTexture;
        var varyingShader = _varyingMaterial?.Shader;
        var pointShader = _pointMaterial?.Shader;
        var depthShader = _depthMaterial?.Shader;
        var depthMesh = _depthProbe?.Mesh;
        _viewport.Msaa3D = Viewport.Msaa.Disabled;
        _root.Free();
        _emitterMaterial?.Dispose();
        _varyingMaterial?.Dispose();
        varyingShader?.Dispose();
        _pointMaterial?.Dispose(); pointShader?.Dispose();
        _pointMesh?.Dispose(); _boxMesh.Dispose();
        _depthMaterial?.Dispose(); depthShader?.Dispose(); depthMesh?.Dispose();
        for (int i = 0; i < _normalMaterials.Length; i++)
        {
            var shader = _normalMaterials[i]?.Shader;
            _normalMaterials[i]?.Dispose(); shader?.Dispose(); _normalTextures[i]?.Dispose();
        }
        _material.Dispose();
        albedo?.Dispose();
        if (atlases is not null) foreach (var texture in atlases) texture.Dispose();
        if (masks is not null) foreach (var texture in masks) texture.Dispose();
        atlases?.Clear();
        masks?.Clear();
        data?.Dispose();
        _environment.Dispose();
    }
}
