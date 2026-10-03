extends Label
## Shows and checks the C GDExtension in this folder (twodog_probe.gdextension). On the web the library is a side
## module loaded from the pck; on desktop a native library in bin/ (or beside a published executable); on Android a
## native library in the APK.

const PASSED_META := "gdextension_smoke_passed"
const FAILURE_META := "gdextension_smoke_failure"

var _description := ""


func _ready() -> void:
	set_meta(PASSED_META, false)
	var failure := _exercise()
	if failure.is_empty():
		set_meta(PASSED_META, true)
		text = "This text comes from a GDExtension: " + _description
		print("2DOG_GDEXTENSION_SMOKE_PASSED ", _description)
	else:
		set_meta(FAILURE_META, failure)
		text = "GDExtension probe failed: " + failure
		push_error("GDExtension smoke failed: " + failure)


func _exercise() -> String:
	# Looked up dynamically: a missing extension is reported here instead of failing to parse this script.
	if not ClassDB.class_exists(&"TwoDogProbe"):
		return "TwoDogProbe is not registered (the extension did not load)"
	var probe: Object = ClassDB.instantiate(&"TwoDogProbe")
	if probe == null:
		return "ClassDB.instantiate(TwoDogProbe)"
	if probe.call(&"add", 40, 2) != 42:
		return "TwoDogProbe.add (variant call)"

	# Statically typed calls go through the extension's ptrcall entry points.
	_description = load("res://gdextension/gdextension_typed_calls.gd").describe()
	if _description.is_empty():
		return "TwoDogProbe.add (typed call)"
	if not _description.begins_with("twodog_probe (C) on "):
		return "TwoDogProbe.describe returned '%s'" % _description
	return ""
