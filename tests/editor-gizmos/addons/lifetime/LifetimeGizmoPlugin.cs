using System.Collections.Generic;
using Godot;

[Tool]
public partial class LifetimeGizmoPlugin : EditorNode3DGizmoPlugin
{
    public HashSet<ulong> Gizmos { get; } = new();

    public override bool _HasGizmo(Node3D node) => node.Name.ToString().StartsWith("Lifetime");

    public override string _GetGizmoName() => "Lifetime regression";

    public override void _Redraw(EditorNode3DGizmo gizmo) => Gizmos.Add(gizmo.GetInstanceId());
}
