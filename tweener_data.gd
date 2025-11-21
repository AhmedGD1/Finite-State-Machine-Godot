class_name TweenerData
extends Resource

@export_placeholder("Generic") var group: String
@export_placeholder("Position for example") var property: String
@export var values: Array
@export var durations: PackedFloat32Array

@export var transition: Tween.TransitionType
@export var ease_type: Tween.EaseType

@export var delay_on_start: float = 0.0
@export var loops: int = 1
@export var parallel: bool = false

var callback: Callable = Callable()
var start_call: Callable = Callable()

func reset() -> void:
	group = TweenerComponent.DEFAULT_GROUP
	property = ""
	values.clear()
	durations.clear()
	transition = Tween.TRANS_CUBIC
	ease_type = Tween.EASE_IN
	delay_on_start = 0.0
	callback = Callable()
	start_call = Callable()
	loops = 1
	parallel = false
