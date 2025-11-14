class_name VelocityComponent
extends Node

signal landed()
signal jumped()
signal left_ground()
signal started_falling()
signal max_height_reached()
signal multiple_jump_performed(index: int)
signal motion_mode_switched(mode: CharacterBody2D.MotionMode)

const COYOTE_TIME: float = 0.15
const JUMP_BUFFERING_TIME: float = 0.15
const MOVEMENT_THRESHOLD: float = 0.01
## player has only 0.6s to be able to jump in air, if he started to fall
## if this cooldown ended, player will not be able to perform any multiple jumps
const MULTIPLE_JUMP_COOLDOWN: float = 0.6 

@export_range(0.5, 100) var mass: float = 1.0
@export_range(10, 1000) var max_speed: float = 100.0

@export var use_gravity: bool = true

@export_group("Control")
@export_range(1, 100) var acceleration: float = 30.0
@export_range(1, 100) var deceleration: float = 40.0

@export_subgroup("Air Control")
@export_range(0.05, 100) var air_acceleration: float = 10.0
@export_range(0.05, 100) var air_deceleration: float = 10.0

@export_group("Gravity & Jump")
@export_range(1, 10) var max_jumps: int = 1
@export_range(5, 500) var jump_height: float = 40.0
@export_range(0.05, 1.0) var time_to_apex: float = 0.3
@export_range(0.1, 10) var gravity_scale: float = 1.0

@export_group("Factors")
@export_range(1.0, 2.0) var fall_gravity_multiplier: float = 1.0
@export_range(0.05, 0.9) var jump_resistance: float = 0.4
@export_range(50, 1000) var max_fall_speed: float = 300.0

@export_group("Custom Ground Check")
@export var ground_check: RayCast2D

@onready var controller: CharacterBody2D = get_owner()

@onready var _jumps_left: int = max_jumps
@onready var _is_floating_mode: bool = controller.motion_mode == \
	CharacterBody2D.MotionMode.MOTION_MODE_FLOATING

var _gravity: float:
	get: return _calculate_gravity(jump_height, time_to_apex)

var _jump_velocity: float:
	get: return _calculate_jump_velocity(_gravity)

var velocity: Vector2:
	get: return controller.velocity
	set(value): controller.velocity = value

var vertical_speed: float:
	get: return velocity.dot(controller.up_direction)

var is_falling: bool:
	get: return vertical_speed < 0.0

var velocity_length: float:
	get: return velocity.length()

var was_grounded: bool
var is_grounded: bool

var _jumped_this_frame: bool
var _started_to_fall: bool
var _max_height_fired: bool

var _coyote_timer: float
var _jump_buffering_timer: float
var _max_height_timer: float
var _multiple_jump_cooldown_timer: float

func _physics_process(delta: float) -> void:
	_update_timers(delta)
	_apply_gravity(delta)
	_update_floor_info()
	controller.move_and_slide()
	_check_falling()
	
	_jumped_this_frame = false

func _update_floor_info() -> void:
	was_grounded = is_grounded
	is_grounded = ground_check.is_colliding() if is_instance_valid(ground_check) else controller.is_on_floor()
	
	if was_grounded && !is_grounded:
		left_ground.emit()
		
		if !_jumped_this_frame && vertical_speed <= 0.0:
			start_coyote()
	
	if !was_grounded && is_grounded:
		reset_jumps()
		landed.emit()
		_max_height_fired = false

func _update_timers(delta: float) -> void:
	if is_grounded:
		_reset_timers()
	
	_coyote_timer = _reduce_time(_coyote_timer, delta)
	_jump_buffering_timer = _reduce_time(_jump_buffering_timer, delta)
	_max_height_timer = _reduce_time(_max_height_timer, delta)
	_multiple_jump_cooldown_timer = _reduce_time(_multiple_jump_cooldown_timer, delta)
	
	if _max_height_timer <= 0.0 && !is_grounded && !_max_height_fired:
		max_height_reached.emit()
		_max_height_fired = true
		
		_multiple_jump_cooldown_timer = MULTIPLE_JUMP_COOLDOWN

func _reduce_time(timer: float, delta: float) -> float:
	return max(0.0, timer - delta)

func _check_falling() -> void:
	if is_grounded:
		_started_to_fall = false
		return
	
	if is_falling && !_started_to_fall:
		_started_to_fall = true
		started_falling.emit()

func _apply_gravity(delta: float) -> void:
	if is_grounded || _is_floating_mode || !use_gravity:
		return
	var gravity: float = _gravity * (fall_gravity_multiplier if is_falling else 1.0)
	velocity += gravity * delta * (-controller.up_direction)
	
	if is_falling && vertical_speed < -max_fall_speed:
		var clamped: float = max(vertical_speed, -max_fall_speed)
		velocity += (clamped - vertical_speed) * controller.up_direction

func add_impulse(value: Vector2) -> void:
	velocity += value / mass

func add_force(value: Vector2, delta: float) -> void:
	velocity += (value / mass) * delta

func accelerate(direction: Vector2, delta: float, speed: float = -1.0, weight: float = -1.0) -> void:
	var applied_speed: float = speed if speed > 0.0 else max_speed
	var accel: float = acceleration if _is_floating_mode || is_grounded else air_acceleration
	var applied_accel: float = weight if weight > 0.0 else accel
	var smoothing: float = _exponential_smoothing(applied_accel, delta)
	var desired: Vector2 = direction.normalized() * applied_speed
	
	velocity.x = lerp(velocity.x, desired.x, smoothing)
	if _is_floating_mode:
		velocity.y = lerp(velocity.y, desired.y, smoothing)

func decelerate(delta: float, weight: float = -1.0) -> void:
	var decel: float = deceleration if _is_floating_mode || is_grounded else air_deceleration
	var applied_weight: float = weight if weight > 0.0 else decel
	var smoothing: float = _exponential_smoothing(applied_weight, delta)
	
	velocity.x = lerp(velocity.x, 0.0, smoothing)
	if _is_floating_mode:
		velocity.y = lerp(velocity.y, 0.0, smoothing)

func apply_jump_resistance(value: float = -1.0) -> void:
	var applied_value: float = value if value > 0.0 else jump_resistance
	if vertical_speed > 0:
		velocity.y *= applied_value

func jump() -> void:
	if _is_floating_mode:
		return
	velocity -= vertical_speed * controller.up_direction
	velocity += _jump_velocity * controller.up_direction
	
	jumped.emit()
	# is performing more jumps;
	if _jumps_left < max_jumps:
		multiple_jump_performed.emit(max_jumps - _jumps_left)
	
	_max_height_timer = time_to_apex
	_jumps_left = max(0, _jumps_left - 1)
	
	_max_height_fired = false
	_jumped_this_frame = true

func can_jump(jump_condition: bool, ignore_floor: bool = false) -> bool:
	var has_more_jumps: bool = can_perform_extra_jump() && _multiple_jump_cooldown_timer > 0.0
	var is_on_floor: bool = is_grounded || ignore_floor
	return (is_on_floor || has_coyote() || has_more_jumps) && (jump_condition || has_buffered_jump())

func try_consume_jump(jump_condition: bool, ignore_floor: bool = false) -> bool:
	if !can_jump(jump_condition, ignore_floor):
		return false
	consume_coyote()
	consume_buffered_jump()
	jump()
	return true

#region Coyote & Jump Buffers
func start_coyote(duration: float = COYOTE_TIME) -> void:
	_coyote_timer = duration

func buffer_jump(duration: float = JUMP_BUFFERING_TIME) -> void:
	_jump_buffering_timer = duration

func has_coyote() -> bool:
	return _coyote_timer > 0.0

func has_buffered_jump() -> bool:
	return _jump_buffering_timer > 0.0

func consume_coyote() -> void:
	_coyote_timer = 0.0

func consume_buffered_jump() -> void:
	_jump_buffering_timer = 0.0
#endregion

#region Helper Methods
func set_motion_mode(mode: CharacterBody2D.MotionMode) -> void:
	controller.motion_mode = mode
	_is_floating_mode = mode == CharacterBody2D.MotionMode.MOTION_MODE_FLOATING
	
	motion_mode_switched.emit(mode)

func is_moving_horizontally() -> bool:
	var right_direction: Vector2 = controller.up_direction.rotated(-PI / 2)
	var horizontal_speed: float = abs(velocity.dot(right_direction))
	return horizontal_speed > MOVEMENT_THRESHOLD && abs(vertical_speed) < MOVEMENT_THRESHOLD

func can_perform_extra_jump() -> bool:
	return _jumps_left > 0 && _jumps_left < max_jumps && is_falling

func reset_jumps() -> void:
	_jumps_left = max_jumps

func flip_gravity() -> void:
	controller.up_direction *= -1

func set_up_direction(value: Vector2) -> void:
	controller.up_direction = value.normalized()
#endregion

func _exponential_smoothing(value: float, delta: float) -> float:
	return 1.0 - exp(-value * delta)

func _calculate_jump_velocity(gravity: float) -> float:
	return sqrt(2.0 * gravity * jump_height)

func _calculate_gravity(height: float, time: float) -> float:
	var new_gravity: float = 2.0 * height / (time * time)
	return new_gravity * gravity_scale

func _reset_timers() -> void:
	_coyote_timer = 0.0
	_jump_buffering_timer = 0.0
	_max_height_timer = 0.0
	_multiple_jump_cooldown_timer = 0.0


