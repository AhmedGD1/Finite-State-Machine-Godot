class_name BufferManager

var _buffers: Array[InputBuffer] = []

func buffer_action(action: String, duration: float) -> void:
	var buffer: InputBuffer = InputBuffer.new(action, duration)
	_buffers.append(buffer)

func has_buffer(action: String) -> bool:
	return _buffers.any(func(b: InputBuffer): b.action == action && b.is_valid())

func consume(action: String) -> void:
	var index: int = _buffers.find_custom(func(b: InputBuffer): b.action == action && b.is_valid())
	if index == -1:
		return
	_buffers.remove_at(index)

func try_consume(action: String) -> bool:
	var index: int = _buffers.find_custom(func(b: InputBuffer): b.action == action && b.is_valid())
	if index == -1: 
		return false
	_buffers.remove_at(index)
	return true

func update(delta: float) -> void:
	for buffer: InputBuffer in _buffers:
		buffer.update(delta)
	_buffers = _buffers.filter(func(b: InputBuffer): b.is_valid())

class InputBuffer:
	var action: String
	var _expire_time: float
	
	func update(delta: float) -> void:
		_expire_time -= delta
	
	func is_valid() -> bool:
		return _expire_time > 0.0
	
	func _init(new_action: String, duration: float) -> void:
		action = new_action
		_expire_time = duration
