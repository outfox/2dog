extends Node
# Parent of the signal table's sources. The GDExtension ticker is created here rather than placed in the scene, so
# a missing extension leaves its row at "n/a" instead of failing to instantiate the class (the scene's
# GDExtensionProbe label reports why). Sources come before the counters in the tree, so this runs before they connect.


func _ready() -> void:
	if not ClassDB.class_exists(&"TwoDogTicker"):
		return
	var ticker: Node = ClassDB.instantiate(&"TwoDogTicker")
	ticker.name = "GDExtensionTicker"
	add_child(ticker)
