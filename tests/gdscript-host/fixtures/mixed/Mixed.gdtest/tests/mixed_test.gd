class_name MixedGdUnitSuite
extends GdUnitTestSuite

const MIXED_SCENE := preload("res://mixed_scene.tscn")


func test_managed_scene() -> void:
	var scene := auto_free(MIXED_SCENE.instantiate()) as Node
	get_tree().root.add_child(scene)
	await get_tree().process_frame
	assert_int(scene.get_node("ScriptValue").read_parent_value()).is_equal(42)
	assert_str(scene.get_script().resource_path).is_equal("res://GameValue.cs")
