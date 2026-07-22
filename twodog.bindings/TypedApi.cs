namespace Godot;

// GodotSharp-compatible generic conveniences over the generated API.

public partial class Node
{
    /// <summary>Fetches a node and casts, throwing on mismatch (GodotSharp semantics).</summary>
    public T GetNode<T>(NodePath path) where T : class =>
        (T)(object)(GetNode(path) ?? throw new InvalidOperationException($"Node not found: {path}"));

    public T? GetNodeOrNull<T>(NodePath path) where T : class => GetNodeOrNull(path) as T;

    public T GetParent<T>() where T : class =>
        (T)(object)(GetParent() ?? throw new InvalidOperationException("Node has no parent."));

    public T GetChild<T>(int idx) where T : class =>
        (T)(object)(GetChild(idx) ?? throw new InvalidOperationException($"No child at index {idx}."));
}

public partial class PackedScene
{
    /// <summary>Instantiates and casts, throwing on mismatch (GodotSharp semantics).</summary>
    public T Instantiate<T>(GenEditState editState = GenEditState.Disabled) where T : class =>
        (T)(object)(Instantiate(editState) ?? throw new InvalidOperationException("Scene failed to instantiate."));
}

public static partial class ResourceLoader
{
    /// <summary>Loads and casts, throwing on mismatch (GodotSharp semantics).</summary>
    public static T Load<T>(string path, string typeHint = "", CacheMode cacheMode = CacheMode.Reuse)
        where T : class =>
        (T)(object)(Load(path, typeHint, cacheMode) ?? throw new InvalidOperationException($"Failed to load: {path}"));
}
