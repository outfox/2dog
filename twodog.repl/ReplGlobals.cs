using Godot;

namespace twodog.Repl;

/// <summary>Live objects available by name in every submission. Access them on the Godot thread.</summary>
public sealed class ReplGlobals(Engine owner)
{
    /// <summary>The engine owned by this host.</summary>
    public Engine engine => owner;
    /// <summary>The running scene tree.</summary>
    public SceneTree tree => owner.Tree;
    /// <summary>The root viewport.</summary>
    public Window root => tree.Root;
    /// <summary>The current scene, if one is loaded.</summary>
    public Node? scene => tree.CurrentScene;
    private Node? currentNode;
    /// <summary>The selected node (cd), falling back to root if it leaves the tree. Access on the Godot thread.</summary>
    public Node here
    {
        get
        {
            if (currentNode is { } node && GodotObject.IsInstanceValid(node) && !node.IsQueuedForDeletion() && node.IsInsideTree() && node.GetTree() == tree)
                return node;
            currentNode = null;
            return root;
        }
    }
    internal void SelectNode(Node node) => currentNode = node;
    /// <summary>An isolated scratch viewport beside the game. All calls belong on Godot's thread.</summary>
    public ReplWorld world { get; } = new(owner);
    /// <summary>Cooperative cancellation for the current submission (Ctrl+C).</summary>
    public CancellationToken ct { get; internal set; }
}
