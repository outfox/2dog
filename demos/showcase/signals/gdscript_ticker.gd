extends Node
# Signal table source: a GDScript signal, emitted once per second.
# (CSharpTicker.cs does the same from C#; TwoDogTicker from the C GDExtension.)

signal ticked(count: int)

@export var interval := 1.0 # seconds

var count := 0
var _elapsed := 0.0


func _process(delta: float) -> void:
	_elapsed += delta
	if _elapsed >= interval:
		_elapsed -= interval
		tick()


func tick() -> int:
	count += 1
	ticked.emit(count)
	return count
