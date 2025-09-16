class_name StateMachine extends RefCounted

signal state_changed(prev: int, next: int)
signal transition_triggered(from: int, to: int)
signal timeout_blocked(from: int)
signal state_timeout(from: int)

enum ProcessType { Process, PhysicsProcess }
enum LockType { Full, Transition, None }

var states_enum: Dictionary

var states: Dictionary[int, State] = {}
var transitions: Dictionary[int, Array] = {}
var global_transitions: Array[Transition] = []
var global_data: Dictionary[String, Object] = {}

var signal_connections: Dictionary[Object, Array] = {}
var signal_conditions: Dictionary[Object, Dictionary] = {}

var current_state: State
var current_id: int
var previous_id: int
var initial_id: int

var has_initial_id: bool
var paused: bool
var state_time: float

var animator: Animator

func _init(sm_enum: Dictionary) -> void:
	states_enum = sm_enum

func add_state(id: int, update = null, enter = null, exit = null, min_time: float = 0, timeout: float = -1, process_type: ProcessType = ProcessType.PhysicsProcess) -> State:
	if states.has(id):
		push_error("Trying to store an existent state %s" % id)
		return null
	
	var state = State.new(id, update, enter, exit, min_time, timeout, process_type)
	states[id] = state
	
	if !has_initial_id:
		initial_id = id
		has_initial_id = true
	
	return state

func start() -> void:
	if has_initial_id:
		_change_state_internal(initial_id, true)

func remove_state(id: int) -> void:
	if !states.has(id):
		push_warning("Trying to access a non-existent state")
		return
	
	states.erase(id)
	
	if initial_id == id:
		has_initial_id = false
	if current_id == id:
		reset()
	
	for key: int in transitions.keys():
		transitions[key] = transitions[key].filter(func(t: Transition): t.to != id && t.from != id)
	global_transitions = global_transitions.filter(func(t: Transition): t.to != id)

func reset() -> bool:
	if states.is_empty():
		push_warning("Trying to reset an empty state machine")
		return false
	
	if !has_initial_id:
		var state: State = states.values().front() as State
		set_initial_state(state.id)
	
	previous_id = -1
	_change_state_internal(initial_id)
	return true

func set_initial_state(id: int) -> void:
	if !states.has(id):
		push_error("Trying to set non-existent state as initial %s" % id)
		return
	initial_id = id
	has_initial_id = true

func restart_current_state(ignore_enter: bool = false, ignore_exit: bool = false) -> void:
	if current_state == null:
		push_warning("Trying to access a non-existent state")
		return
	state_time = 0.0
	
	if !ignore_exit && !current_state.is_locked(): current_state.exit.call()
	if !ignore_enter: current_state.enter.call()

func get_state(id: int) -> State:
	return states.get(id, null)

func try_change_state(id: int, condition: bool) -> bool:
	if !condition:
		return false
	_change_state_internal(id)
	return true

func force_change_state(id: int) -> bool:
	if !states.has(id) || current_state == null || current_state.is_locked():
		return false
	_change_state_internal(id)
	return true

func go_back() -> void:
	if !states.has(previous_id) || current_state == null || current_state.is_locked():
		push_error("There is no previous state to go back to or current state is locked. Current State Id: %s" % current_id)
		return
	_change_state_internal(previous_id)

func go_back_if_possible() -> bool:
	if !states.has(previous_id) || current_state == null || current_state.is_locked():
		return false
	_change_state_internal(previous_id)
	return true

func _change_state_internal(id: int, ignore_exit: bool = false) -> void:
	if !states.has(id):
		push_error("Trying to switch to a non-existent state")
		return
	
	var canExit: bool = !ignore_exit && current_state != null && !current_state.is_locked()
	if canExit: current_state.exit.call()
	
	state_time = 0.0
	previous_id = current_id
	current_id = id
	current_state = states[id]
	
	current_state.enter.call()
	
	if current_state.has_data("animation"):
		var config: AnimationConfig = current_state.get_data("animation") as AnimationConfig
		config.play_animation(animator)
	
	if has_initial_id:
		state_changed.emit(previous_id, current_id)

func add_transition(from: int, to: int, condition: Callable, override_min_time: float = -1) -> Transition:
	if !states.has(to):
		push_error("Trying to add a transition to a non-existent state")
		return null
	
	if !transitions.has(from):
		transitions[from] = []
	
	var transition: Transition = Transition.new(from, to, condition, override_min_time)
	transitions[from].append(transition)
	
	return transition

func add_global_transition(to: int, condition: Callable, override_min_time: float = -1) -> Transition:
	if !states.has(to):
		push_error("Trying to add a transition to a non-existent state")
		return null
	
	var transition: Transition = Transition.new(-1, to, condition, override_min_time)
	global_transitions.append(transition)
	
	return transition

func add_signal_transition(from: int, to: int, sig: Signal, override_min_time: float = -1) -> Transition:
	var condition: Callable = _create_condition_from_signal(sig)
	return add_transition(from, to, condition, override_min_time)

func add_global_signal_transition(to: int, sig: Signal, override_min_time: float = -1) -> Transition:
	var condition: Callable = _create_condition_from_signal(sig)
	return add_global_transition(to, condition, override_min_time)

func _create_condition_from_signal(sig: Signal) -> Callable:
	var obj: Object = sig.get_object()
	var signal_name: String = sig.get_name()
	
	if obj == null:
		push_error("Signal has no valid object")
		return func(): return false
	
	var weak_ref: WeakRef  = weakref(obj) as WeakRef # to detect when object is freed
	
	if !signal_conditions.has(obj):
		signal_conditions[obj] = {}
		signal_connections[obj] = []
	
	var key: String = signal_name
	signal_conditions[obj][key] = false
	
	var connection: Callable = func(_a = null, _b = null, _c = null, _d = null, _e = null, _f = null, _g = null):
		var current_obj: Object = weak_ref.get_ref()
		if current_obj: # object still exist
			signal_conditions[current_obj][key] = true
		else:
			_cleanup_object_signals(obj)
	
	sig.connect(connection, CONNECT_DEFERRED)
	signal_connections[obj].append({"signal": sig, "connection": connection})
	
	return func() -> bool:
		var current_obj: Object = weak_ref.get_ref()
		if current_obj == null:
			_cleanup_object_signals(obj)
			return false
		
		if signal_conditions[current_obj].get(key, false):
			signal_conditions[current_obj][key] = false
			return true
		return false

func _cleanup_object_signals(obj: Object) -> void:
	if signal_connections.has(obj):
		# disconned all signals for this object
		for connection_data: Dictionary in signal_connections[obj]:
			var sig: Signal = connection_data.signal as Signal
			var connection: Callable = connection_data.connection
			
			if sig.is_connected(connection):
				sig.disconnect(connection)
		signal_connections.erase(obj)
	signal_conditions.erase(obj)

func remove_transition(from: int, to: int) -> bool:
	if !transitions.has(from):
		push_warning("Trying to remove a non-existent transition")
		return false
	
	var original_size = transitions[from].size()
	transitions[from] = transitions[from].filter(func(t: Transition): t.to != to)
	
	if transitions[from].is_empty():
		transitions.erase(from)
	
	var removed: bool = transitions[from].size() < original_size if transitions.has(from) else original_size > 0
	if !removed: push_error("No Transition Was Found Between: %s -> %s" % [from, to])
	
	return removed

func remove_global_transition(to: int) -> bool:
	# if has any global transition
	var original_size: int = global_transitions.size()
	global_transitions = global_transitions.filter(func(t: Transition): t.to != to)
	
	var removed: bool = global_transitions.size() < original_size
	if !removed: push_error("No Global Transition Was Found Between: %s -> %s" % [current_id, to])
	
	return removed

func process(process_type: ProcessType, delta: float) -> void:
	if paused || current_state == null:
		return
	
	if current_state.process_type == process_type:
		state_time += delta
		current_state.update.call(delta)
		_check_transitions()

func _check_transitions() -> void:
	var timeout_triggered: bool = current_state.timeout > 0 && state_time >= current_state.timeout
	
	if timeout_triggered:
		if current_state.is_fully_locked():
			timeout_blocked.emit(current_id)
			return
		
		state_timeout.emit(current_id)
		var restart_id: int = current_state.restart_id
		_change_state_internal(restart_id)
		transition_triggered.emit(current_id, restart_id)
		return
	
	if current_state.transition_blocked():
		return
	
	var evaluator: TransitionEvaluator = TransitionPool.get_evaluator()
	
	if transitions.has(current_id):
		evaluator.candidate_transitions.append_array(transitions[current_id])
	evaluator.candidate_transitions.append_array(global_transitions)
	
	if evaluator.has_candidates():
		evaluator.candidate_transitions.sort_custom(Transition.compare)
		_check_transition_loop(evaluator.candidate_transitions)
	TransitionPool.return_evaluator(evaluator)

func _check_transition_loop(candidate_transitions: Array[Transition]) -> void:
	for transition: Transition in candidate_transitions:
		var required_time: float = transition.override_min_time if transition.override_min_time > 0.0 else current_state.min_time
		var time_requirement_met: bool = transition.force_instant_transition || state_time >= required_time
		
		if time_requirement_met && transition.condition.call():
			_change_state_internal(transition.to)
			transition_triggered.emit(transition.from, transition.to)
			
			if transition.on_triggered != null:
				transition.on_triggered.call()
			return

func set_animator(what) -> void:
	animator = Animator.new(what)

func pause() -> void: paused = true
func is_paused() -> bool: return paused

func resume(reset_state_time: bool = false) -> void:
	paused = false
	
	if reset_state_time:
		state_time = 0.0

func set_global_data(key: String, value: Variant) -> bool:
	if key.is_empty():
		return false
	global_data[key] = value
	return true

func remove_global_data(key: String) -> void: 
	global_data.erase(key)

func get_global_data(key: String) -> Variant:
	return global_data[key]

func get_current_state_id() -> int:
	return current_id

func get_initial_state_id() -> int:
	return initial_id

func get_previous_state_id() -> int:
	return previous_id

func get_state_time() -> float:
	return state_time

func get_min_state_time() -> float:
	return current_state.min_time if current_state != null else -1

func get_remaining_time() -> float:
	return max(0, current_state.timeout - state_time) if current_state != null && current_state.timeout > 0 else -1

func get_state_name(id: int) -> String:
	return states_enum.get(id, str(id))

func has_transition(from: int, to: int) -> bool:
	return transitions.has(from) && transitions[from].any(func(t: Transition): t.to == to)

func has_any_transition_from(from: int) -> bool:
	return transitions.has(from) && !transitions[from].is_empty()

func has_any_global_transition(to: int) -> bool:
	return global_transitions.any(func(t): t.to == to)

func has_previous_state() -> bool: 
	return previous_id != -1
	
func has_state_with_id(id: int) -> bool: 
	return states.has(id)

func is_current_state(id: int) -> bool:
	return current_state.id == id if current_state != null else false

func is_previous_state(id: int) -> bool:
	return previous_id == id

func is_in_state_with_tag(tag: String) -> bool:
	return current_state.tags.has(tag) if current_state != null else false

func debug_current_transition() -> String:
	return "%s -> %s" % [previous_id, current_id]

func debug_all_transitions() -> String:
	var result: Array[String] = []
	for t_list: Array[Transition] in transitions.values():
		for t: Transition in t_list:
			result.append("%s -> %s (Priority: %s)" % [get_state_name(t.from), get_state_name(t.to), t.priority])
	
	for t: Transition in global_transitions:
		result.append("Global -> %s (Priority: %s)" % [get_state_name(t.to), t.priority])
	
	return result.reduce(func(acc, w): return w if acc == "" else acc + "\n" + w, "")

func debug_all_states() -> String:
	var result: Array[String] = []
	for state: State in states.values():
		result.append(get_state_name(state.id))
	return result.reduce(func(acc, w): return w if acc == "" else acc + "\n" + w, "")

class State:
	var id: int
	var restart_id: int
	var min_time: float
	var timeout: float = -1
	
	var update: Callable
	var enter: Callable
	var exit: Callable
	
	var process_type: ProcessType
	var lock_type: LockType
	
	var tags: Array[String] = []
	var data: Dictionary[String, Variant] = {}
	
	func set_animation_data(anim_name: String, loop: bool, on_finished = null, speed: float = 1, blend: float = 0) -> void:
		var config: AnimationConfig = AnimationConfig.new(anim_name, speed, blend, loop, on_finished)
		set_data("animation", config)
	
	func lock(type: LockType) -> State:
		lock_type = type
		return self
	
	func unlock() -> State:
		lock_type = LockType.None
		return self
	
	func set_restart_id(id: int) -> State:
		restart_id = id
		return self
	
	func set_process_type(type: ProcessType) -> State:
		process_type = type
		return self
	
	func is_locked() -> bool: return lock_type != LockType.None
	func is_fully_locked() -> bool: return lock_type == LockType.Full
	func transition_blocked() -> bool: return lock_type == LockType.Transition
	
	func has_tag(tag: String) -> bool: return tags.has(tag)
	func has_data(key: String) -> bool: return data.has(key)
	func get_tags() -> Array[String]: return tags
	
	func add_tag(tag: String) -> State:
		tags.append(tag)
		return self
	
	func set_data(key: String, value: Variant) -> State:
		if !key.is_empty():
			data[key] = value
		return self
	
	func remove_data(key: String) -> State:
		data.erase(key)
		return self
	
	func get_data(key: String) -> Variant:
		if !data.has(key):
			push_error("Trying to access a non-existent data")
			return null
		return data[key]
	
	func _init(a: int, b, c, d, e: float, f: float, g: ProcessType) -> void:
		id = a
		update = b if b != null else func(): pass
		enter = c if c != null else func(): pass
		exit = d if d != null else func(): pass
		min_time = e
		timeout = f
		process_type = g
		restart_id = a
		
		lock_type = LockType.None

class Transition:
	var from: int
	var to: int
	var override_min_time: float
	var condition: Callable
	var force_instant_transition: bool
	var priority: int = 1
	var insertion_index: int
	
	var on_triggered: Callable
	
	const HIGHEST_PRIORITY: int = 9999999999
	
	func force_instant() -> Transition:
		force_instant_transition = true
		return self
	
	func set_priority(value: int) -> Transition:
		priority =  max(0, value)
		return self
	
	func set_highest_priority() -> Transition:
		priority = HIGHEST_PRIORITY
		return self
	
	func set_condition(value: Callable) -> Transition:
		condition = value
		return self
	
	func set_min_time(value: float) -> Transition:
		override_min_time = value
		return self
	
	func _init(from: int, to: int, condition: Callable, override_min_time: float) -> void:
		self.from = from
		self.to = to
		self.condition = condition
		self.override_min_time = override_min_time
		insertion_index = TransitionPool.get_next_index()
	
	static func compare(a: Transition, b: Transition) -> int:
		var priority_diff: int = b.priority - a.priority
		return priority_diff if priority_diff != 0 else (a.insertion_index - b.insertion_index)

class TransitionPool:
	static var pool: Array[TransitionEvaluator] = []
	static var next_index: int = 0
	
	const MAX_POOL_SIZE: int = 32
	
	static func get_evaluator() -> TransitionEvaluator:
		return pool.pop_front() if !pool.is_empty() else TransitionEvaluator.new()
	
	static func return_evaluator(evaluator: TransitionEvaluator) -> void:
		if evaluator == null:
			return
		evaluator.reset()
		if pool.size() < MAX_POOL_SIZE:
			pool.append(evaluator)
	
	static func get_next_index() -> int:
		next_index = (next_index + 1) % 2147483647
		return next_index

class TransitionEvaluator:
	var candidate_transitions: Array[Transition] = []
	
	func reset() -> void:
		candidate_transitions.clear()
	
	func has_candidates() -> bool:
		return !candidate_transitions.is_empty()

class AnimationConfig:
	var anim_name: String
	var speed: float = 1
	var custom_blend: float = 0
	var loop: bool
	var on_finished: Callable
	
	func play_animation(animator: Animator) -> void:
		if animator != null:
			animator.play_animation(anim_name, speed, custom_blend, loop, on_finished)
	
	func _init(anim_name: String, speed: float, custom_blend: float, loop: bool, on_finished = null) -> void:
		self.anim_name = anim_name
		self.speed = max(0.001, speed)
		self.custom_blend = max(0, custom_blend)
		self.loop = loop
		if on_finished != null:
			self.on_finished = on_finished
		
		if speed == 0.001:
			push_warning("Animation speed is extremely low: 0.001f")

class Animator:
	var adapter: Variant = null
	
	func _init(player) -> void:
		if player is AnimatedSprite2D:
			adapter = AnimatedSprite2DAdapter.new(player)
		elif player is AnimationPlayer:
			adapter = AnimationPlayerAdapter.new(player)
		else:
			push_error("Animation type is not supported")
	
	func play_animation(anim_name: String, speed: float, custom_blend: float, loop: bool, on_finished: Callable) -> void:
		if adapter != null:
			adapter.play_animation(anim_name, speed, custom_blend, loop, on_finished)

class AnimationPlayerAdapter:
	var player: AnimationPlayer
	var finish_callbacks: Dictionary[String, Callable] = {}
	
	func _init(player: AnimationPlayer) -> void:
		self.player = player
		player.animation_finished.connect(_on_animation_finished_signal)
	
	func play_animation(anim_name: String, speed: float, blend: float, loop: bool, on_finished: Callable) -> void:
		if player == null || anim_name.is_empty():
			return
			
		if !player.has_animation(anim_name):
			push_warning("Animation '%s' not found in AnimationPlayer" % anim_name)
			return
		
		var animation: Animation = player.get_animation(anim_name)
		if animation != null:
			animation.loop_mode = Animation.LOOP_LINEAR if loop else Animation.LOOP_NONE
		
		if on_finished != null:
			finish_callbacks[anim_name] = on_finished
		player.play(anim_name, blend, speed)
	
	func _on_animation_finished_signal(anim_name: String) -> void:
		if finish_callbacks.has(anim_name):
			var callback: Callable = finish_callbacks[anim_name]
			callback.call()
			finish_callbacks.erase(anim_name)

class AnimatedSprite2DAdapter:
	var sprite: AnimatedSprite2D
	var finish_callbacks: Dictionary[String, Callable] = {}
	
	func _init(what: AnimatedSprite2D) -> void:
		sprite = what
		
		sprite.animation_finished.connect(_on_animation_finished_signal)
	
	func play_animation(anim_name: String, speed: float, blend: float, loop: bool, on_finished: Callable) -> void:
		if sprite.sprite_frames == null || anim_name.is_empty():
			return
			
		if !sprite.sprite_frames.has_animation(anim_name):
			push_warning("Animation '%s' not found in AnimatedSprite2D" % anim_name)
			return
		
		sprite.speed_scale = speed
		sprite.play(anim_name)
		sprite.sprite_frames.set_animation_loop(anim_name, loop)
		
		if on_finished != null:
			finish_callbacks[anim_name] = on_finished
	
	func _on_animation_finished_signal() -> void:
		var anim_name: String = sprite.animation
		if finish_callbacks.has(anim_name):
			var callback: Callable = finish_callbacks[anim_name]
			callback.call()
			finish_callbacks.erase(anim_name)
