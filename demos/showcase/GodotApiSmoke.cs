using System;
using Godot;

namespace showcase;

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
        ManagedCallables();
        ErrorReportingAndTypedCollections();
        ImagesAndResources();
        EngineSingletons();
        LowLevelServers();
        SceneAndGeneratedScript();
        GDScriptOnlyEngineFeatures(tree);
        GDExtensionProbe(tree);
        SignalTable(tree);
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
        // Exercise an int32_t Error result immediately after an int64_t result with a nonzero
        // upper word. Reading the native result as Error : long used to retain stale wasm state.
        Require(integer.AsInt64() == 4_294_967_296L, "64-bit Variant conversion failed before resize");
        Require(values.Resize(4) == Error.Ok && values.Count == 4, "Godot Array resize returned a corrupt Error");
        Require(values.Resize(3) == Error.Ok, "Godot Array shrink failed");

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

    public static void ManagedCallables()
    {
        using var emitter = new Node();
        using var child = new Node();
        try
        {
            var received = 0;
            Action<Node> listener = node =>
            {
                Require(ReferenceEquals(node, child), "managed Callable received the wrong object argument");
                received++;
            };
            var callable = Callable.From<Node>(listener);
            using Variant boxed = callable;
            var roundTrip = boxed.AsCallable();
            Require(ReferenceEquals(roundTrip.Delegate, listener), "Callable marshalling lost the original delegate");
            using var ignored = roundTrip.Call(child);
            Require(received == 1, "round-tripped managed Callable did not invoke its listener");

            Require(emitter.Connect(Node.SignalName.ChildEnteredTree, roundTrip) == Error.Ok,
                "managed Callable could not connect to an object-argument signal");
            emitter.EmitSignal(Node.SignalName.ChildEnteredTree, child);
            Require(received == 2, "connected managed Callable did not receive its signal");
            // A new Callable has a different native handle, but must compare by the original delegate.
            emitter.Disconnect(Node.SignalName.ChildEnteredTree, Callable.From<Node>(listener));
            emitter.EmitSignal(Node.SignalName.ChildEnteredTree, child);
            Require(received == 2, "disconnecting an equivalent Callable left its listener connected");

            var transform = Callable.From<Vector3, Vector3>(value => value * 2);
            using var transformed = transform.Call(new Vector3(1, 2, 3));
            Require(transformed.AsVector3() == new Vector3(2, 4, 6), "managed Callable lost its struct argument or result");
        }
        finally
        {
            emitter.Free();
            child.Free();
        }
    }

    public static void ErrorReportingAndTypedCollections()
    {
        // Both calls go through the 7-pointer-arg native shape that had no wasm trampoline
        // until twodog.WebTrampolines declared it (err_print_error / dictionary_set_typed).
        // Printing is muted: the native call is what is under test, and the warning would
        // otherwise land in every test and smoke log as noise.
        var printErrors = Godot.Engine.PrintErrorMessages;
        Godot.Engine.PrintErrorMessages = false;
        try
        {
            GD.PushWarning("2dog smoke: expected warning, exercising err_print_error");
            GD.PushError("2dog smoke: expected error, exercising err_print_error");
        }
        finally
        {
            Godot.Engine.PrintErrorMessages = printErrors;
        }

        var typed = new Godot.Collections.Dictionary<string, int> { ["answer"] = 42 };
        Require(typed["answer"] == 42, "typed Dictionary round-trip failed");

        var values = new Godot.Collections.Array<int> { 3, 1, 2 };
        Require(values.IndexOf(1) == 1, "typed Array IndexOf failed");
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
        Require((string)ProjectSettings.GetSetting("application/config/name") == "showcase",
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

            const string cubePath = "Flair/BlueCubes/BlueCube1";
            var cube = instance.GetNodeOrNull<SpinningCube>(cubePath);
            Require(cube is not null, "generated C# script type was not attached to the scene: "
                                      + DescribeScriptBinding(instance.GetNodeOrNull(cubePath)!, typeof(SpinningCube)));
            Require(GodotObject.IsInstanceValid(cube), "generated C# script instance is invalid");
        }
        finally
        {
            instance.Free();
        }
    }

    public static void GDScriptOnlyEngineFeatures(SceneTree tree) =>
        RequireScenePassed(tree, "CenterContainer/GDScriptLinkerProbe", "gdscript_linker_smoke", "GDScript linker probe");

    /// <summary>
    /// The C GDExtension in gdextension/ (a side module loaded from the pck on the web, a native library elsewhere):
    /// called from C# here, and through GDScript's variant and typed (ptrcall) paths by the scene's probe label.
    /// </summary>
    public static void GDExtensionProbe(SceneTree tree)
    {
        Require(ClassDB.ClassExists("TwoDogProbe"), "the TwoDogProbe GDExtension class is not registered");
        using var instance = ClassDB.Instantiate("TwoDogProbe");
        var probe = instance.AsGodotObject();
        Require(probe is not null, "TwoDogProbe could not be instantiated");

        using var sum = probe.Call("add", 40, 2);
        Require(sum.AsInt64() == 42, $"TwoDogProbe.add returned {sum.AsInt64()}");

        var platform = OperatingSystem.IsBrowser() ? "web"
            : OperatingSystem.IsWindows() ? "windows"
            : OperatingSystem.IsMacOS() ? "macos"
            : "linux";
        using var description = probe.Call("describe");
        Require(description.AsString().StartsWith($"twodog_probe (C) on {platform},", StringComparison.Ordinal),
            $"TwoDogProbe.describe returned '{description.AsString()}'");

        RequireScenePassed(tree, "CenterContainer/GDExtensionProbe", "gdextension_smoke", "GDExtension probe");
    }

    /// <summary>
    /// The scene's signal table: a C# [Signal], a GDScript signal, an engine Timer and a GDExtension signal, each
    /// counted by a C# (SignalCounter) and a GDScript (signal_counter.gd) listener. Fires every source once, out of
    /// band of the sources' own once-per-second ticks, and requires both listeners to receive it with its payload.
    /// </summary>
    public static void SignalTable(SceneTree tree)
    {
        var scene = tree.CurrentScene;
        Require(scene is not null, "there is no current scene for the signal table");

        RequireSignalReceived(scene, "CSharpTicker", source =>
        {
            Require(source is CSharpTicker,
                "the CSharpTicker script did not bind: " + DescribeScriptBinding(source, typeof(CSharpTicker)));
            return ((CSharpTicker)source).Tick();
        });
        RequireSignalReceived(scene, "GDScriptTicker", source =>
        {
            using var count = source.Call("tick");
            return count.AsInt32();
        });
        RequireSignalReceived(scene, "EngineTimer", source =>
        {
            source.EmitSignal(Timer.SignalName.Timeout);
            return -1;
        });
        RequireSignalReceived(scene, "GDExtensionTicker", source =>
        {
            using var count = source.Call("tick");
            return count.AsInt32();
        });
    }

    // emit returns the payload the listeners must report, or -1 for a signal without one.
    private static void RequireSignalReceived(Node scene, string source, Func<Node, int> emit)
    {
        var emitter = scene.GetNodeOrNull($"Signals/Sources/{source}");
        Require(emitter is not null, $"signal source {source} is missing");
        var inCSharp = scene.GetNodeOrNull<SignalCounter>($"Signals/Table/{source}InCSharp");
        var inGDScript = scene.GetNodeOrNull<Label>($"Signals/Table/{source}InGDScript");
        Require(inCSharp is not null && inGDScript is not null, $"the {source} signal counters are missing");

        var csharpBefore = inCSharp.Received;
        var gdscriptBefore = GDScriptCounter(inGDScript, "received");
        var payload = emit(emitter);

        Require(inCSharp.Received == csharpBefore + 1,
            $"{source}'s signal reached the C# listener {inCSharp.Received - csharpBefore} times, not once");
        var gdscriptReceived = GDScriptCounter(inGDScript, "received");
        Require(gdscriptReceived == gdscriptBefore + 1,
            $"{source}'s signal reached the GDScript listener {gdscriptReceived - gdscriptBefore} times, not once");
        if (payload < 0) return;

        Require(inCSharp.LastCount == payload,
            $"the C# listener got {source}'s payload as {inCSharp.LastCount}, not {payload}");
        var gdscriptPayload = GDScriptCounter(inGDScript, "last_count");
        Require(gdscriptPayload == payload,
            $"the GDScript listener got {source}'s payload as {gdscriptPayload}, not {payload}");
    }

    private static int GDScriptCounter(Label counter, string property)
    {
        using var value = counter.Get(property);
        return value.AsInt32();
    }

    // Scene-attached probe scripts record <prefix>_passed (and <prefix>_failure) metadata in _ready().
    private static void RequireScenePassed(SceneTree tree, string nodePath, string metaPrefix, string what)
    {
        var passedMeta = metaPrefix + "_passed";
        var failureMeta = metaPrefix + "_failure";

        var currentScene = tree.CurrentScene;
        Require(currentScene is not null, $"there is no current scene for the {what}");

        var probe = currentScene.GetNodeOrNull<Node>(nodePath);
        Require(probe is not null, $"the scene-attached {what} is missing");
        Require(probe.HasMeta(passedMeta), $"the {what} did not run _ready()");

        using var passed = probe.GetMeta(passedMeta);
        if (passed.AsBool())
            return;

        var failure = $"unknown {what} failure";
        if (probe.HasMeta(failureMeta))
        {
            using var failureValue = probe.GetMeta(failureMeta);
            failure = failureValue.AsString();
        }

        Require(false, failure);
    }

    // Distinguishes a missing node, a script that failed to bind, and a managed type loaded twice
    // (different AssemblyLoadContext), which is otherwise invisible in a null GetNodeOrNull<T>.
    private static string DescribeScriptBinding(Node node, Type expected)
    {
        if (node is null) return "node not found";

        var actual = node.GetType();
        using var scriptValue = node.GetScript();
        var script = scriptValue.AsGodotObject() as Script;
        var scriptInfo = script is null ? "no script"
            : $"script '{script.ResourcePath}' (can instantiate: {script.CanInstantiate()})";
        var typeInfo = $"node type {actual.FullName} from {actual.Assembly.Location} in ALC '{System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(actual.Assembly)?.Name}'";
        var expectedInfo = $"expected {expected.FullName} from {expected.Assembly.Location} in ALC '{System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(expected.Assembly)?.Name}'";
        return $"{scriptInfo}; {typeInfo}; {expectedInfo}";
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException($"Godot API smoke failed: {message}");
    }
}
