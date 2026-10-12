class_name IsolatedGdUnitSuite
extends GdUnitTestSuite

const TEST_SCENE := preload("res://Pure.gdtest/tests/test_scene.tscn")


func after() -> void:
	ProbeState.suite_finished = true


func before_test() -> void:
	ProbeState.setups += 1


func after_test() -> void:
	ProbeState.teardowns += 1


func test_pass() -> void:
	ProbeState.test_bodies += 1
	assert_int(IsolatedGameRules.answer()).is_equal(42)
	var game_uid := FileAccess.get_file_as_string("res://game_rules.gd.uid").strip_edges()
	assert_str((load(game_uid) as GDScript).resource_path).is_equal("res://game_rules.gd")
	assert_bool(ProbeState.initialized).is_true()
	assert_object(get_tree().root.get_node_or_null("GameMainMustNotRun")).is_null()
	var scene := auto_free(TEST_SCENE.instantiate()) as Sprite2D
	assert_vector(scene.texture.get_size()).is_equal(Vector2(8, 8))
	assert_str(scene.get_meta("marker")).is_equal("isolated resource")


func test_failure() -> void:
	ProbeState.test_bodies += 1
	assert_int(1).is_equal(2)


func test_skip(_do_skip := true, _skip_reason := "prototype skip") -> void:
	push_error("A skipped test body must never execute.")


func test_async_parameter(value: int, expected: int, _test_parameters := [[2, 4], [3, 6]]) -> void:
	ProbeState.test_bodies += 1
	await get_tree().process_frame
	await get_tree().create_timer(0.01).timeout
	assert_int(value * 2).is_equal(expected)
