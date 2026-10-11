namespace twodog.Repl;

/// <summary>A session's named scratch worlds. Access on Godot's owner thread.</summary>
public sealed class ReplWorlds(Engine owner) : IDisposable
{
    private readonly Dictionary<string, ReplWorld> named = new(StringComparer.Ordinal);
    /// <summary>Names of this session's worlds.</summary>
    public string[] Names { get { _ = owner.Tree; return named.Keys.ToArray(); } }
    /// <summary>Gets an existing named world.</summary>
    public ReplWorld this[string name] { get { _ = owner.Tree; return named[name]; } }
    /// <summary>Creates a named world, optionally loading a scene. Names are unique within the session.</summary>
    public ReplWorld Add(string name, string? path = null)
    {
        _ = owner.Tree;
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (named.ContainsKey(name)) throw new ArgumentException($"World '{name}' already exists.", nameof(name));
        var world = new ReplWorld(owner, name);
        try
        {
            if (path is null) world.Clear(); else world.Open(path);
            named.Add(name, world);
            return world;
        }
        catch { world.Dispose(); throw; }
    }
    internal ReplWorld Default()
    {
        var world = new ReplWorld(owner);
        named.Add("default", world);
        return world;
    }
    /// <summary>Closes and removes a named world. The default world remains available through world.</summary>
    public bool Remove(string name)
    {
        _ = owner.Tree;
        if (name == "default") throw new ArgumentException("Close or clear the default world with world.Close() or world.Clear().", nameof(name));
        if (!named.Remove(name, out var world)) return false;
        world.Dispose();
        return true;
    }
    public void Dispose()
    {
        foreach (var world in named.Values) world.Dispose();
        named.Clear();
    }
}
