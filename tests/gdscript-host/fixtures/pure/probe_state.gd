extends Node

var initialized := false
var setups := 0
var teardowns := 0
var test_bodies := 0
var suite_finished := false


func _ready() -> void:
	initialized = true
