using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;

// The test and GodotSharp assemblies are rooted whole, matching desktop hosts. Private callback reflection lets
// this regression exercise the actual packaged implementation without adding a public API or friend assembly.
internal static class Program
{
    private delegate bool SerializeSingle(Delegate callback, out byte[] buffer);
    private delegate bool DeserializeSingle(byte[] buffer, out Delegate callback);
    private static int _lastValue;

    [STAThread]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "GodotSharp is rooted whole by the test project.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "GodotSharp is rooted whole by the test project.")]
    private static void Main(string[] args)
    {
        if (args.Length != 1)
            throw new ArgumentException("Pass the absolute path of the release libgodot library.");

        using var engine = new twodog.Engine("delegate-serialization", AppContext.BaseDirectory, "--headless")
        {
            NativePath = Path.GetFullPath(args[0]),
        };
        engine.Start();

        var utils = typeof(GodotObject).Assembly.GetType("Godot.DelegateUtils", throwOnError: true)!;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var serialize = utils.GetMethod("TrySerializeSingleDelegate", flags)!.CreateDelegate<SerializeSingle>();
        var deserialize = utils.GetMethod("TryDeserializeSingleDelegate", flags)!.CreateDelegate<DeserializeSingle>();
        var serializeType = utils.GetMethod("SerializeType", flags)!.CreateDelegate<Action<BinaryWriter, Type?>>();
        var deserializeType = utils.GetMethod("DeserializeType", flags)!.CreateDelegate<Func<BinaryReader, Type?>>();

        // A blob may refer to a generic type never seen by this application's serializer. AOT must decline it
        // cleanly, even if a corresponding type exists in the image; JIT retains the legacy reconstruction path.
        using (var stream = new MemoryStream())
        {
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(1);
                writer.Write(typeof(Action<>).Assembly.GetName().Name!);
                writer.Write(typeof(Action<>).FullName!);
                writer.Write(0);
                writer.Write(typeof(Guid).Assembly.GetName().Name!);
                writer.Write(typeof(Guid).FullName!);
            }
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            Check(deserializeType(reader) == (RuntimeFeature.IsDynamicCodeSupported ? typeof(Action<Guid>) : null),
                "Unknown generic type did not follow the runtime's supported path.");
        }

        foreach (Type type in new[]
        {
            typeof(Action<int>), typeof(Action<string>), typeof(Func<int?, int?>),
            typeof(Func<List<int>, Dictionary<string, List<int>>>), typeof(Action<int[]>),
        })
            RoundTripType(type, serializeType, deserializeType);

        Check(serialize((Action<int>)TakeInt, out var valueData), "Value delegate serialization failed.");
        Check(deserialize(valueData, out var valueDelegate), "Value delegate deserialization failed.");
        ((Action<int>)valueDelegate)(42);
        Check(_lastValue == 42, "Deserialized value delegate invoked the wrong method.");

        Check(serialize((Func<List<int>, Dictionary<string, List<int>>>)Nest, out var nestedData),
            "Nested generic delegate serialization failed.");
        Check(deserialize(nestedData, out var nestedDelegate), "Nested generic delegate deserialization failed.");
        var result = ((Func<List<int>, Dictionary<string, List<int>>>)nestedDelegate)(new List<int> { 7 });
        Check(result["value"][0] == 7, "Nested generic delegate produced the wrong result.");

        Action<int> closure = Capture(11);
        Check(serialize(closure, out var closureData), "Closure serialization failed.");
        Check(deserialize(closureData, out var closureDelegate), "Closure deserialization failed.");
        ((Action<int>)closureDelegate)(3);
        Check(_lastValue == 14, "Deserialized closure lost its captured value.");

        var node = new Node();
        try
        {
            Check(serialize((Action<bool>)node.SetProcess, out var nodeData), "Godot object delegate serialization failed.");
            Check(deserialize(nodeData, out var nodeDelegate), "Godot object delegate deserialization failed.");
            ((Action<bool>)nodeDelegate)(true);
            Check(node.IsProcessing(), "Godot object delegate lost its native target.");
        }
        finally
        {
            node.Free();
        }

        // The registry is process-wide and serialization can happen on worker threads.
        Parallel.For(0, 64, _ => RoundTripType(typeof(Func<int, List<string>>), serializeType, deserializeType));

        if (RuntimeFeature.IsDynamicCodeSupported)
        {
            var knownTypes = (HashSet<Type>)utils.GetField("_aotSerializedGenericTypes", flags)!.GetValue(null)!;
            Check(knownTypes.Count == 0, "The JIT serializer retained types and would pin editor reload assemblies.");
        }

        Console.WriteLine($"DELEGATE_SERIALIZATION_PASSED (dynamic code: {RuntimeFeature.IsDynamicCodeSupported})");
    }

    private static void RoundTripType(Type type, Action<BinaryWriter, Type?> serialize, Func<BinaryReader, Type?> deserialize)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            serialize(writer, type);
        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        Check(deserialize(reader) == type, $"Type round trip failed: {type}.");
        Check(stream.Position == stream.Length, "Type round trip did not consume the entire legacy payload.");
    }

    private static void TakeInt(int value) => _lastValue = value;
    private static Action<int> Capture(int captured) => value => _lastValue = captured + value;
    private static Dictionary<string, List<int>> Nest(List<int> value) => new() { ["value"] = value };

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
