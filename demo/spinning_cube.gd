extends MeshInstance3D
# Red cubes: spun by GDScript inside the Godot project.
# (Blue cubes use the C# SpinningCube.cs; white cubes are driven by the 2dog host loop.)

@export var spin_axis := Vector3.UP
@export var spin_speed := 1.5 # radians per second


func _process(delta: float) -> void:
	rotate(spin_axis.normalized(), spin_speed * delta)
