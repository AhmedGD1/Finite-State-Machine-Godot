class_name VelocityComponent
extends Node

signal landed()
signal left_ground()
signal max_height_reached()

const JUMP_BUFFER_TIME: float = 0.15
const COYOTE_TIME: float = 0.15

@export_range(1.0, 999.0) var max_speed: float = 100.0
@export_range(1.0, 99.0) var acceleration: float = 20.0
@export_range(1.0, 99.0) var deceleration: float = 25.0

@export var use_gravity: bool = true

@export_group("Platformer Settings")
@export_range(1.0, 99.0) var air_acceleration: float = 5.0
@export_range(1.0, 99.0) var air_deceleration: float = 5.0

@export_subgroup("Jump height & Apex time")
@export_range(5.0, 999.0) var jump_height: float = 30.0
@export_range(0.05, 9.0) var time_to_apex: float = 0.3

@export_subgroup("Gravity Settings")
@export_range(100.0, 999.0) var max_fall_speed: float = 500.0
@export_range(1.0, 9.0) var fall_multiplier: float = 1.0
@export_range(0.1, 9.0) var gravity_scale: float = 1.0

@export_subgroup("Wall Collide Settings")
@export var wall_detectors: Array[RayCast2D] = []

@onready var controller: CharacterBody2D = get_owner()
@onready var is_floating_mode: bool = controller.motion_mode == CharacterBody2D.MotionMode.MOTION_MODE_FLOATING

var velocity: Vector2:
	get: return controller.velocity
	set(value): controller.velocity = value

var gravity: float:
	get: return _calculate_gravity(jump_height, time_to_apex)

var jump_velocity: float:
	get: return _calculate_jump_velocity(gravity)

var _start_timer: bool
var _timer: float
var _jumped_this_frame: bool

var _jump_buffer_timer: float
var _coyote_timer: float

var is_grounded: bool
var was_grounded: bool

var is_on_wall: bool

var jump_resistor_factor: float = 0.2

func _physics_process(delta: float) -> void:
	_update_timers(delta)
	_apply_gravity(delta, use_gravity)
	
	controller.move_and_slide()
	
	_update_grounded_info() # includes move and slide method
	_update_max_height_info(delta)
	_update_wall_info()
	
	_jumped_this_frame = false

func _update_grounded_info() -> void:
	was_grounded = is_grounded
	is_grounded = controller.is_on_floor()
	
	if was_grounded && !is_grounded:
		left_ground.emit()
		
		if !_jumped_this_frame:
			start_coyote()
	
	if !was_grounded && is_grounded:
		landed.emit()

func _update_max_height_info(delta: float) -> void:
	if !_start_timer:
		return
	
	_timer += delta
	var stop: bool = is_grounded || _timer >= time_to_apex
	
	if stop:
		_start_timer = false
		
		if _timer >= time_to_apex:
			max_height_reached.emit()
		_timer = 0.0

func _update_wall_info() -> void:
	if wall_detectors.is_empty():
		is_on_wall = controller.is_on_wall()
		return
	
	is_on_wall = wall_detectors.any(func(r: RayCast2D): r.is_colliding())

func _update_timers(delta: float) -> void:
	if _coyote_timer > 0:
		_coyote_timer -= delta
	if _jump_buffer_timer > 0:
		_jump_buffer_timer -= delta

func accelerate(direction: Vector2, delta: float, custom_speed: float = -1) -> void:
	var applied_speed: float = max_speed if custom_speed == -1 else custom_speed
	var applied_accel: float = acceleration if is_floating_mode || is_grounded else air_acceleration
	var smoothing: float = _exponential_smoothing(applied_accel, delta)
	var desired: Vector2 = direction.normalized() * applied_speed
	
	if is_floating_mode:
		velocity.y = lerp(velocity.y, desired.y, smoothing)
	velocity.x = lerp(velocity.x, desired.x, smoothing)

func decelerate(delta: float) -> void:
	var applied_decel: float = deceleration if is_floating_mode || is_grounded else air_deceleration
	var smoothing: float = _exponential_smoothing(applied_decel, delta)
	
	if is_floating_mode:
		velocity.y = lerp(velocity.y, 0.0, smoothing)
	velocity.x = lerp(velocity.x, 0.0, smoothing)

func apply_impulse(value: Vector2) -> void:
	velocity += value

func apply_force(value: Vector2, delta: float) -> void:
	velocity += value * delta

func force_stop() -> void:
	velocity = Vector2.ZERO

func apply_jump_resistor(value: float = -1) -> void:
	var applied_value: float = jump_resistor_factor if value == -1 else value
	velocity.y = lerp(velocity.y, 0.0, applied_value)

func can_jump(jump_pressed: bool = true, ignore_floor: bool = false) -> bool:
	var condition: bool = is_grounded if !ignore_floor else true
	return (has_coyote() || condition) && (jump_pressed || has_buffered_jump())

func try_consume_jump(jump_pressed: bool, ignore_floor: bool = false) -> bool:
	if !can_jump(jump_pressed, ignore_floor):
		return false
	
	consume_buffered_jump()
	consume_coyote()
	jump()
	return true

func jump() -> void:
	velocity.y = -abs(jump_velocity) * sign(controller.up_direction.y)
	_start_timer = true
	_jumped_this_frame = true

func is_falling(epsilon: float = 0.01) -> bool:
	return velocity.dot(controller.up_direction) < -epsilon

func is_on_wall_with_name(wall_name: String) -> bool:
	return !wall_detectors.is_empty() && wall_detectors.any(func(r: RayCast2D): r.is_colliding() && r.name == wall_name)

func _apply_gravity(delta: float, condition: bool = true) -> void:
	if !condition || is_grounded:
		return
	velocity += controller.up_direction * _get_gravity_internal() * delta
	
	var down = sign(controller.up_direction.y)
	var max_down = max_fall_speed * down
	var max_up = -max_down
	velocity.y = clamp(velocity.y, max_up, max_down)

func start_coyote() -> void:
	_coyote_timer = COYOTE_TIME

func has_coyote() -> bool:
	return _coyote_timer > 0.0

func consume_coyote() -> void:
	_coyote_timer = 0.0

func buffer_jump() -> void:
	_jump_buffer_timer = JUMP_BUFFER_TIME

func has_buffered_jump() -> bool:
	return _jump_buffer_timer > 0.0

func consume_buffered_jump() -> void:
	_jump_buffer_timer = 0.0

func _get_gravity_internal() -> float:
	if velocity.dot(controller.up_direction) > 0:
		return gravity
	return gravity * fall_multiplier

func _exponential_smoothing(value: float, delta: float) -> float:
	return 1.0 - exp(-value * delta)

func _calculate_jump_velocity(grav: float) -> float:
	return sqrt(2.0 * grav * jump_height)

func _calculate_gravity(height: float, time: float) -> float:
	var formula: float = 2.0 * height / (time * time)
	return formula * gravity_scale






