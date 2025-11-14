class_name BufferManager

var buffers: Array[InputBuffer] = []

func buffer_action(action: String, duration: float) -> void:
	var index: int = buffers.find_custom(func(b: InputBuffer) -> bool: return b.action == action)
	
	if index != -1: # buffer with this action does exist
		buffers[index].expire_time = duration
		return
	var input_buffer: InputBuffer = InputBuffer.new(action, duration)
	buffers.append(input_buffer)

func has_action(action: String) -> bool:
	return buffers.any(func(b: InputBuffer) -> bool: return b.action == action && b.is_valid)

func try_consume(action: String) -> bool:
	var index: int = buffers.find_custom(func(b: InputBuffer) -> bool: return b.action == action && b.is_valid)
	if index == -1:
		return false
	buffers.remove_at(index)
	return true

func update(delta: float) -> void:
	for i in range(buffers.size() - 1, -1, -1):
		buffers[i].update(delta)
		if !buffers[i].is_valid:
			buffers.remove_at(i)

class InputBuffer:
	var action: String
	var expire_time: float
	
	var is_valid: bool:
		get: return expire_time > 0.0
	
	func update(delta: float) -> void:
		expire_time -= delta
	
	func _init(action_key: String, duration: float) -> void:
		action = action_key
		expire_time = duration
