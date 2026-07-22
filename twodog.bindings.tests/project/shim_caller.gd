extends Node

var sprouted := false

func read_sprouts():
	return get_parent().Sprouts

func call_bump(by):
	return get_parent().Bump(by)

func on_sprouted():
	sprouted = true

func was_sprouted():
	return sprouted
