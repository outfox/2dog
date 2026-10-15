extends Camera3D
## Keeps the 3D rows aligned with the 2D legend at every window shape. The canvas_items/expand stretch keeps the
## base height on wide windows and the base width on narrow ones, so the camera locks the same axis.

@onready var _base_fov := fov


func _ready() -> void:
	get_viewport().size_changed.connect(_fit)
	_fit()


func _fit() -> void:
	var base := Vector2(ProjectSettings.get_setting("display/window/size/viewport_width"),
		ProjectSettings.get_setting("display/window/size/viewport_height"))
	var size := get_viewport().get_visible_rect().size
	if size.x * base.y >= size.y * base.x:
		keep_aspect = KEEP_HEIGHT
		fov = _base_fov
	else:
		keep_aspect = KEEP_WIDTH
		fov = rad_to_deg(2.0 * atan(tan(deg_to_rad(_base_fov) / 2.0) * base.x / base.y))
