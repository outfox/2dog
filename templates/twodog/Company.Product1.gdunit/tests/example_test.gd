extends GdUnitTestSuite


func test_answer() -> void:
	assert_int(21 * 2).is_equal(42)
