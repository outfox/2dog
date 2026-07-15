extends Node

const PASSED_META := "gdscript_linker_smoke_passed"
const FAILURE_META := "gdscript_linker_smoke_failure"


func _ready() -> void:
	set_meta(PASSED_META, false)
	var failure := _exercise_gdscript_only_engine_features()
	if failure.is_empty():
		set_meta(PASSED_META, true)
		print("2DOG_GDSCRIPT_LINKER_SMOKE_PASSED")
	else:
		set_meta(FAILURE_META, failure)
		push_error("GDScript linker smoke failed: " + failure)


# These engine classes are intentionally not referenced by C#. On browser-wasm
# they must remain reachable because GDScript resolves them dynamically.
func _exercise_gdscript_only_engine_features() -> String:
	var regex := RegEx.new()
	if regex.compile("2(dog)") != OK:
		return "RegEx.compile"
	var regex_match := regex.search("2dog")
	if regex_match == null or regex_match.get_string(1) != "dog":
		return "RegEx.search"

	var noise := FastNoiseLite.new()
	noise.seed = 42
	var noise_sample := noise.get_noise_2d(12.5, -3.25)
	if not is_finite(noise_sample):
		return "FastNoiseLite.get_noise_2d"

	var astar := AStarGrid2D.new()
	astar.region = Rect2i(0, 0, 4, 4)
	astar.update()
	var path := astar.get_id_path(Vector2i(0, 0), Vector2i(3, 3))
	if path.is_empty() or path[0] != Vector2i(0, 0) or path[-1] != Vector2i(3, 3):
		return "AStarGrid2D.get_id_path"

	var expression := Expression.new()
	if expression.parse("sqrt(pow(3, 2) + pow(4, 2))") != OK:
		return "Expression.parse"
	var expression_result = expression.execute()
	if expression.has_execute_failed() or not is_equal_approx(float(expression_result), 5.0):
		return "Expression.execute"

	var hashing := HashingContext.new()
	if hashing.start(HashingContext.HASH_SHA256) != OK:
		return "HashingContext.start"
	if hashing.update("2dog".to_utf8_buffer()) != OK:
		return "HashingContext.update"
	if hashing.finish().size() != 32:
		return "HashingContext.finish"

	return ""
