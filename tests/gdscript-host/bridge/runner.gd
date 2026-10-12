extends "res://addons/gdUnit4/src/core/runners/GdUnitTestSessionRunner.gd"
## Prototype integration with the upstream session lifecycle; .NET owns engine shutdown.

var bridge_done := false
var _messages: Array[String] = []
var _has_failures := false


func init_runner() -> void:
	set_process(false)
	ProjectSettings.set_setting(GdUnitSettings.TEST_FLAKY_CHECK, false)
	_executor.fail_fast(false)
	report_base_path = ProjectSettings.globalize_path("res://twodog_test_host/reports")
	max_report_history = 1
	var request_text := FileAccess.get_file_as_string("res://twodog_test_host/request.json")
	var request: Dictionary = JSON.parse_string(request_text)
	var collector := GdUnitScriptErrorCollector.new()
	var scanner := GdUnitTestSuiteScanner.new()
	for resource_path: String in request["roots"]:
		for script in scanner.scan(resource_path):
			GdUnitTestDiscoverer.discover_tests(script, func(test: GdUnitTestCase) -> void:
				_test_cases.append(test)
				_messages.append(JSON.stringify({"kind": "discovered", "test": GdUnitTestCase.to_dict(test)}))
			)
	if collector.has_errors() or _test_cases.is_empty():
		_messages.append(JSON.stringify({"kind": "discovery_error", "message": str(collector.errors())}))
		await quit(RETURN_ERROR_SCRIPT_ERRORS_DETECTED)
		return
	_state = RUN
	set_process(true)


func _on_gdunit_event(event: GdUnitEvent) -> void:
	_messages.append(JSON.stringify({"kind": "event", "event": event.serialize()}))
	if event.is_failed() or event.is_error() or event.is_warning():
		_has_failures = true


func get_exit_code() -> int:
	return RETURN_ERROR if _has_failures else RETURN_SUCCESS


func quit(code: int) -> void:
	set_process(false)
	_state = EXIT
	await super(code)
	_test_cases.clear()
	_executor = null
	_hooks = null
	_test_session = null
	var completion := {"kind": "completed", "exit_code": code}
	var fixture_state := get_tree().root.get_node_or_null("ProbeState")
	if fixture_state != null:
		completion["fixture_state"] = {
			"test_bodies": fixture_state.test_bodies,
			"setups": fixture_state.setups,
			"teardowns": fixture_state.teardowns,
			"suite_finished": fixture_state.suite_finished
		}
	_messages.append(JSON.stringify(completion))
	bridge_done = true


func drain_messages() -> PackedStringArray:
	var pending := PackedStringArray(_messages)
	_messages.clear()
	return pending
