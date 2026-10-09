using Godot;

namespace twodog.Repl;

/// <summary>A scratch viewport with its own 2D and 3D worlds, beside the running game.</summary>
public sealed class ReplWorld(Engine owner) : IDisposable
{
    private Window? window;
    private Node? current;

    /// <summary>The scratch window/viewport, or null before it is opened.</summary>
    public Window? Viewport { get { _ = owner.Tree; return GodotObject.IsInstanceValid(window) ? window : null; } }
    /// <summary>The scratch scene root. The game's SceneTree.CurrentScene is unchanged.</summary>
    public Node? Scene { get { _ = owner.Tree; return GodotObject.IsInstanceValid(current) ? current : null; } }

    /// <summary>Loads a scene in the scratch viewport. Calls and scene code run on Godot's owner thread.</summary>
    public Node Open(string path)
    {
        _ = owner.Tree;
        if (!ResourceLoader.Exists(path, "PackedScene"))
            throw new ArgumentException($"No PackedScene exists at '{path}'.", nameof(path));
        using var packed = ResourceLoader.Load<PackedScene>(path)
            ?? throw new ArgumentException($"Could not load a PackedScene from '{path}'.", nameof(path));
        return Replace(packed.Instantiate());
    }

    /// <summary>Opens an empty scratch scene, replacing any previous scratch scene.</summary>
    public Node Clear()
    {
        _ = owner.Tree;
        return Replace(new Node { Name = "Scratch" });
    }

    private Node Replace(Node scene)
    {
        var tree = owner.Tree;
        if (Viewport is null)
        {
            window = new Window
            {
                Visible = false, Name = "ReplWorld", Title = "2dog REPL - scratch world", Size = new Vector2I(960, 640),
                ForceNative = true, World2D = new World2D(), World3D = new World3D(),
            };
            window.CloseRequested += Close;
            tree.Root.AddChild(window, forceReadableName: true);
            // Headless fixtures still exercise the independent viewport and physics worlds.
            if (DisplayServer.GetName() != "headless") window.PopupCentered();
        }
        if (Scene is { } previous)
        {
            previous.GetParent().RemoveChild(previous);
            previous.QueueFree();
        }
        current = scene;
        window!.AddChild(scene);
        return scene;
    }

    /// <summary>Closes only the scratch window, leaving the game and REPL running.</summary>
    public void Close()
    {
        if (Viewport is not { } viewport) return;
        viewport.CloseRequested -= Close;
        viewport.GetParent()?.RemoveChild(viewport);
        viewport.QueueFree();
        window = null;
        current = null;
    }

    public void Dispose() => Close();
}
