## Made By Ahmed GD
class_name StateMachine extends Node

enum ProcessType{
	Process, PhysicsProcess
}

var states: Dictionary[int, State] = {}
var transitions: Dictionary[int, Array] = {}
var signal_conditions: Dictionary[String, bool] = {}

var global_transitions: Array[Transition] = []

var currentState: State
var current_id: int = 0
var previous_id: int = -1
var initial_id: int = -1

var paused: bool
var state_time: float

func _process(delta: float) -> void: _process_state(ProcessType.Process, delta)
func _physics_process(delta: float) -> void: _process_state(ProcessType.PhysicsProcess, delta)

func add_state(id: int, update: Variant = null, enter: Variant = null, exit: Variant = null, min_time: float = 0.0, timeout: float = -1) -> State:
	if states.has(id):
		push_error("There is already a state with id %s" % id)
		return null
	
	var state: State = State.new(id, update, enter, exit, min_time, timeout)
	states[id] = state
	
	if initial_id == -1:
		initial_id = id
		current_id = id
		currentState = states[id]
		_change_state_internal(id, true)
	
	return state

func remove_state(id: int) -> void:
	if !states.has(id):
		push_error("There is no state with id: %s" % id)
		return
	
	states.erase(id)
	
	if current_id == id:
		reset()
	
	for key: int in transitions.keys():
		transitions[key] = transitions[key].filter(func(t: Transition): return t.to != id && t.from != id)
	global_transitions = global_transitions.filter(func(t: Transition): return t.to != id)

func reset() -> void:
	if states.is_empty():
		return
	
	_change_state_internal(initial_id)
	previous_id = -1

func restart_current_state(ignore_exit: bool = false, ignore_enter: bool = false) -> void:
	reset_state_time()
	
	if !ignore_exit: currentState.exit.call()
	if !ignore_enter: currentState.enter.call()

func get_state(id: int) -> State:
	return states.get(id, null)

func change_state(id: int, condition: bool) -> void:
	if condition && states.has(id):
		_change_state_internal(id)

func force_change_state(id: int) -> bool:
	if !states.has(id):
		return false
	_change_state_internal(id)
	return true

func go_back() -> void:
	if previous_id == -1:
		return
	_change_state_internal(previous_id)

func go_back_if_possible() -> bool:
	if previous_id == -1 || !states.has(previous_id):
		return false
	_change_state_internal(previous_id)
	return true

func _change_state_internal(id: int, ignore_exit: bool = false) -> void:
	if !states.has(id):
		push_error("There is no state with id: %s" % id)
		return
	
	if !ignore_exit:
		currentState.exit.call()
	
	reset_state_time()
	previous_id = current_id
	current_id = id
	currentState = states[id]
	
	currentState.enter.call()

func add_transition(from: int, to: int, condition: Callable, override_min_time: float = -1) -> Transition:
	if !states.has(to):
		push_error("There is no To state with id: %s" % to)
		return null
	
	if !transitions.has(from):
		transitions[from] = []
	
	var transition: Transition = Transition.new(from, to, condition, override_min_time)
	transitions[from].append(transition)
	transitions[from].sort_custom(_transition_sorter)
	
	return transition

func add_global_transition(to: int, condition: Callable, override_min_time: float = -1) -> Transition:
	if !states.has(to):
		push_error("There is no To state with id: %s" % to)
		return null
	
	var transition: Transition = Transition.new(-1, to, condition, override_min_time)
	global_transitions.append(transition)
	global_transitions.sort_custom(_transition_sorter)
	
	return transition

func remove_transition(from: int, to: int) -> void:
	if !states.has(from):
		return
	
	var original_size: int = transitions[from].size()
	transitions[from] = transitions[from].filter(func(t: Transition): return t.to != to)
	
	if transitions[from].is_empty():
		transitions.erase(from)
	
	if transitions.has(from) && transitions[from].size() == original_size:
		push_error("No transition was found between: %s -> %s" % [from, to])

func remove_global_transition(to: int) -> void:
	var original_size: int = global_transitions.size()
	global_transitions = global_transitions.filter(func(t: Transition): return t.to != to)
	
	if global_transitions.size() == original_size:
		push_error("No transition was found between: %s -> %s" % [current_id, to])

func clean_transitions() -> void: transitions.clear()
func clean_global_transitions() -> void: global_transitions.clear()

func add_signal_transition(from: int, to: int, sig: Signal, override_min_time: float = -1) -> Transition:
	var condition: Callable = _create_condition_from_signal(sig)
	return add_transition(from, to, condition, override_min_time)

func add_global_signal_transition(to: int, sig: Signal, override_min_time: float = -1) -> Transition:
	var condition: Callable = _create_condition_from_signal(sig)
	return add_global_transition(to, condition, override_min_time)

func _create_condition_from_signal(sig: Signal) -> Callable:
	var key: String = "%s_%s_%s" % [sig.get_object().get_instance_id(), sig.get_name(), signal_conditions.size()]
	signal_conditions[key] = false
	
	var _on_signal_internal: Callable = func(_a = null, _b = null, _c = null, _d = null, _e = null) -> void:
		signal_conditions[key] = true
	
	sig.connect(_on_signal_internal, CONNECT_DEFERRED)
	
	return func(): return signal_conditions.get(key, false)

func _reset_signal_conditions() -> void:
	for key: String in signal_conditions:
		signal_conditions[key] = false

func _process_state(process_type: ProcessType, delta: float) -> void:
	if paused || currentState == null:
		return
	
	if currentState.process_type == process_type:
		state_time += delta
		currentState.update.call(delta)
		_check_transitions()
		_reset_signal_conditions()

func _check_transitions() -> void:
	if currentState.timeout > 0 && state_time >= currentState.timeout:
		_change_state_internal(currentState.restart_id)
		return
	
	var local_transitions: Array = transitions.get(current_id, [])
	if !local_transitions.is_empty():
		_check_transitions_loop(transitions[current_id])
	_check_transitions_loop(global_transitions)

func _check_transitions_loop(current_transitions: Array) -> void:
	var min_time_required: float = currentState.min_time
	
	for transition: Transition in current_transitions:
		if currentState.is_locked():
			break
		
		var required_time: float = transition.override_min_time if transition.override_min_time > 0 else min_time_required
		var time_requirement_met: bool = transition.force_instant_transition || state_time >= required_time
		
		if time_requirement_met && transition.condition.call():
			_change_state_internal(transition.to)
			return

func pause() -> void: paused = true

func resume(reset_time: bool = false) -> void:
	paused = false
	
	if reset_time:
		reset_state_time()

func reset_state_time() -> void: state_time = 0.0

func get_current_state_id() -> int: return currentState.id
func get_initial_state_id() -> int: return initial_id
func get_previous_state_id() -> int: return previous_id
func get_current_time() -> float: return state_time
func get_min_state_time() -> float: return currentState.min_time

func has_transition(from: int, to: int) -> bool:
	return transitions.has(from) && transitions[from].any(func(t: Transition): return t.to == to)

func has_global_transition(to: int) -> bool:
	return global_transitions.any(func(t: Transition): return t.to == to)

func has_state_id(id: int) -> bool: return states.has(id)

func is_current_id(id: int) -> bool: return currentState.id == id
func is_previous_id(id: int) -> bool: return previous_id == id

func debug_current_transition(states_enum: Dictionary) -> void:
	var get_key = func(id: int): return states_enum.find_key(id)
	print("%s -> %s" % [get_key.call(previous_id), get_key.call(current_id)])

static func _transition_sorter(a: Transition, b: Transition) -> int:
	return b.priority - a.priority

class State: 
	var id: int = 0
	var restart_id: int = 0
	var min_time: float = 0.0
	var timeout: float = -1.0
	
	var update: Callable
	var enter: Callable
	var exit: Callable
	
	var locked: bool = false
	var process_type: ProcessType = ProcessType.PhysicsProcess
	
	func lock() -> void: locked = true
	func unlock() -> void: locked = false
	func is_locked() -> bool: return locked
	
	func set_restart_id(value: int) -> void: restart_id = value
	func set_process_type(type: ProcessType) -> void: process_type = type
	
	func _init(_id: int, _update: Variant, _enter: Variant, _exit: Variant, _min_time: float, _timeout: float) -> void:
		id = _id
		update = _update if _update else func(delta: float): pass
		enter = _enter if _enter else func(): pass
		exit = _exit if _exit else func(): pass
		min_time = _min_time
		timeout = _timeout

class Transition:
	var from: int = 0
	var to: int = 1
	var override_min_time: float = -1
	var condition: Callable
	var force_instant_transition: bool = false
	var priority: int = 0
	
	func force_instant() -> void: force_instant_transition = true
	
	func set_priority(value: int) -> void:
		if value < 0:
			push_error("Priority Should be greater than zero!")
			return
		priority = value
	
	func _init(_from: int, _to: int, _condition: Callable, _override_min_time: float) -> void:
		from = _from
		to = _to
		condition = _condition
		override_min_time = _override_min_time



