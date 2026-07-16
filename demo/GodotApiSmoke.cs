using System;
using System.Linq;
using Godot;

/// <summary>
/// A deliberately broad GodotSharp smoke probe shared by desktop tests and
/// the browser host. Keep the calls here concrete: on browser-wasm they must
/// survive trimming and resolve through the statically linked Godot archive.
/// </summary>
public static class GodotApiSmoke
{
    public static void RunAll(SceneTree tree)
    {
        CoreTypesAndNativeHelpers(tree);
        ImagesAndResources();
        EngineSingletons();
        LowLevelServers();
        SceneAndGeneratedScript();
        GDScriptOnlyEngineFeatures(tree);
    }

    public static void CoreTypesAndNativeHelpers(SceneTree tree)
    {
        using Variant integer = 4_294_967_296L;
        using Variant floatingPoint = 12.5;
        Require(integer.AsInt64() == 4_294_967_296L, "64-bit Variant conversion failed");
        Require(Math.Abs(floatingPoint.AsDouble() - 12.5) < double.Epsilon, "double Variant conversion failed");

        var color = Color.FromOkHsl(0.35f, 0.7f, 0.55f, 0.8f);
        Require(color.A > 0.79f && color.A < 0.81f, "OK HSL color conversion failed");
        Require(color.OkHslH >= 0.0f && color.OkHslH <= 1.0f, "OK HSL component lookup failed");

        var payload = System.Text.Encoding.UTF8.GetBytes("2dog browser linker smoke");
        var compressed = payload.Compress(FileAccess.CompressionMode.GZip);
        Require(compressed.Length > 0, "packed byte compression returned no data");
        Require(payload.SequenceEqual(compressed.Decompress(payload.LongLength, FileAccess.CompressionMode.GZip)),
            "packed byte decompression did not round-trip");
        Require(payload.SequenceEqual(compressed.DecompressDynamic(1024, FileAccess.CompressionMode.GZip)),
            "dynamic packed byte decompression did not round-trip");

        using var values = new Godot.Collections.Array { 7, "two", new Vector3(1, 2, 3) };
        Require(values.Count == 3, "Godot Array marshalling failed");

        using var data = new Godot.Collections.Dictionary
        {
            ["name"] = "2dog",
            ["values"] = values,
        };
        var json = Json.Stringify(data);
        using var parsed = Json.ParseString(json);
        Require(parsed.VariantType == Variant.Type.Dictionary, "JSON Variant round-trip failed");

        ulong seed = 0x2D06UL;
        ulong repeatedSeed = seed;
        var seededRandom = GD.RandFromSeed(ref seed);
        var repeatedRandom = GD.RandFromSeed(ref repeatedSeed);
        Require(seededRandom == repeatedRandom && seed == repeatedSeed,
            "seeded random helper was not deterministic");
        GD.Seed(seed);
        var random = GD.Randf();
        Require(random >= 0.0f && random <= 1.0f, "random float helper returned an invalid value");

        var treeFromId = GodotObject.InstanceFromId(tree.GetInstanceId());
        Require(ReferenceEquals(tree, treeFromId), "object instance-id lookup failed");

        var callable = Callable.From<int, int>(value => value * 2);
        using var result = callable.Call(21);
        Require(result.AsInt32() == 42, "managed Callable invocation failed");
    }

    public static void ImagesAndResources()
    {
        var image = Image.CreateEmpty(2, 2, false, Image.Format.Rgba8);
        Require(image is not null, "Image creation failed");

        image.SetPixel(1, 1, Colors.CornflowerBlue);
        Require(image.GetPixel(1, 1).IsEqualApprox(Colors.CornflowerBlue), "Image pixel round-trip failed");

        var png = image.SavePngToBuffer();
        Require(png.Length > 8, "PNG encoding returned no data");

        var decoded = new Image();
        Require(decoded.LoadPngFromBuffer(png) == Error.Ok, "PNG decoding failed");
        Require(decoded.GetWidth() == 2 && decoded.GetHeight() == 2, "decoded PNG has the wrong size");

        Require(ResourceLoader.Exists("res://main.tscn", "PackedScene"), "main scene is absent from ResourceLoader");
        Require(ResourceLoader.Load<PackedScene>("res://main.tscn") is not null, "main scene could not be loaded");
        Require(ResourceLoader.Load<Texture2D>("res://icon.svg") is not null, "imported SVG texture could not be loaded");
    }

    public static void EngineSingletons()
    {
        var version = Godot.Engine.GetVersionInfo();
        Require(version.ContainsKey("major") && version.ContainsKey("string"), "engine version dictionary is incomplete");
        Require(!string.IsNullOrWhiteSpace(OS.GetName()), "OS singleton returned no platform name");
        Require(Time.GetTicksMsec() > 0, "Time singleton did not return a monotonic tick count");
        Require((string)ProjectSettings.GetSetting("application/config/name") == "demo",
            "ProjectSettings did not expose the active Godot project");
        Require(!string.IsNullOrWhiteSpace(TranslationServer.GetLocale()), "TranslationServer returned no locale");
        Require(DisplayServer.GetName() is not null, "DisplayServer returned a null backend name");
        Require(Input.GetConnectedJoypads() is not null, "Input singleton returned a null joypad list");
        Require(AudioServer.GetBusCount() > 0, "AudioServer has no default audio bus");
        Require(ClassDB.ClassExists("AnimationPlayer") && ClassDB.CanInstantiate("AnimationPlayer"),
            "ClassDB could not resolve a common engine class");

        using var dynamicValue = ClassDB.Instantiate("Node");
        var dynamicNode = dynamicValue.AsGodotObject() as Node;
        Require(dynamicNode is not null, "ClassDB failed to instantiate Node");
        dynamicNode.Free();
    }

    public static void LowLevelServers()
    {
        var physics2D = PhysicsServer2D.SpaceCreate();
        Require(physics2D.IsValid, "PhysicsServer2D returned an invalid RID");
        PhysicsServer2D.FreeRid(physics2D);

        var physics3D = PhysicsServer3D.SpaceCreate();
        Require(physics3D.IsValid, "PhysicsServer3D returned an invalid RID");
        PhysicsServer3D.FreeRid(physics3D);

        var navigation2D = NavigationServer2D.MapCreate();
        Require(navigation2D.IsValid, "NavigationServer2D returned an invalid RID");
        NavigationServer2D.FreeRid(navigation2D);

        var navigation3D = NavigationServer3D.MapCreate();
        Require(navigation3D.IsValid, "NavigationServer3D returned an invalid RID");
        NavigationServer3D.FreeRid(navigation3D);

        var canvas = RenderingServer.CanvasCreate();
        Require(canvas.IsValid, "RenderingServer returned an invalid canvas RID");
        RenderingServer.FreeRid(canvas);
    }

    public static void SceneAndGeneratedScript()
    {
        var packedScene = ResourceLoader.Load<PackedScene>("res://main.tscn");
        Require(packedScene is not null, "main PackedScene could not be loaded");

        var instance = packedScene.Instantiate();
        try
        {
            var label = instance.GetNodeOrNull<Label>("CenterContainer/TargetLabel");
            Require(label is not null && !string.IsNullOrWhiteSpace(label.Text), "scene Label was not instantiated");

            var cube = instance.GetNodeOrNull<SpinningCube>("Flair/BlueCubes/BlueCube1");
            Require(cube is not null, "generated C# script type was not attached to the scene");
            Require(GodotObject.IsInstanceValid(cube), "generated C# script instance is invalid");
        }
        finally
        {
            instance.Free();
        }
    }

    public static void GDScriptOnlyEngineFeatures(SceneTree tree)
    {
        const string nodePath = "CenterContainer/GDScriptLinkerProbe";
        const string passedMeta = "gdscript_linker_smoke_passed";
        const string failureMeta = "gdscript_linker_smoke_failure";

        var currentScene = tree.CurrentScene;
        Require(currentScene is not null, "there is no current scene for the GDScript linker probe");

        var probe = currentScene.GetNodeOrNull<Node>(nodePath);
        Require(probe is not null, "the scene-attached GDScript linker probe is missing");
        Require(probe.HasMeta(passedMeta), "the GDScript linker probe did not run _ready()");

        using var passed = probe.GetMeta(passedMeta);
        if (passed.AsBool())
            return;

        var failure = "unknown GDScript failure";
        if (probe.HasMeta(failureMeta))
        {
            using var failureValue = probe.GetMeta(failureMeta);
            failure = failureValue.AsString();
        }

        Require(false, failure);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException($"Godot API smoke failed: {message}");
    }
}
