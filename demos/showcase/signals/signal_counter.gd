extends Label
# Signal table cell: counts what one source emits, received in GDScript (SignalCounter.cs is the C# column).

@export var source: NodePath
## C# [Signal]s keep their C# name ("Ticked"); the GDScript and GDExtension tickers emit "ticked".
@export var signal_name := &"ticked"

var received := 0
var last_count := -1


func _ready() -> void:
	var node := get_node_or_null(source)
	if node == null or not node.has_signal(signal_name):
		text = "n/a"
		return
	node.connect(signal_name, _on_signal)
	text = "0"


# count is the tickers' payload; Timer.timeout has none.
func _on_signal(count := -1) -> void:
	received += 1
	last_count = count
	text = str(received)
