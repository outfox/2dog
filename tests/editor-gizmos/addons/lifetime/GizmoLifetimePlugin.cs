using Godot;

[Tool]
public partial class GizmoLifetimePlugin : EditorPlugin
{
    private LifetimeGizmoPlugin _gizmoPlugin;

    public override void _EnterTree()
    {
        _gizmoPlugin = new LifetimeGizmoPlugin();
        AddNode3DGizmoPlugin(_gizmoPlugin);
    }

    public override void _ExitTree()
    {
        RemoveNode3DGizmoPlugin(_gizmoPlugin);
        GD.Print($"Gizmo regression: {_gizmoPlugin.Gizmos.Count} gizmos created.");
    }
}
