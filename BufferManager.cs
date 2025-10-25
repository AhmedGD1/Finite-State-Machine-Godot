using Godot;
using System.Collections.Generic;

public partial class BufferManager : Node
{
    public static BufferManager Instance { get; private set; }

    private readonly List<InputBuffer> buffers = [];

    public override void _Ready()
    {
        if (Instance != null && Instance != this)
        {
            QueueFree();
            return;
        }
        Instance = this;
    }

    public void BufferAction(string action, float duration)
    {
        InputBuffer existing = GetValidBuffer(action);
        
        if (existing != null)
        {
            existing.SetDuration(duration);
            return;
        }

        InputBuffer buffer = new InputBuffer(action, duration);
        buffers.Add(buffer);
    }

    public bool HasAction(string action)
    {
        return GetValidBuffer(action) != null;
    }

    public bool TryConsume(string action)
    {
        InputBuffer inputBuffer = GetValidBuffer(action);

        if (inputBuffer == null) 
            return false;
        buffers.Remove(inputBuffer);
        return true;
    }

    public override void _PhysicsProcess(double delta)
    {
        bool anyExpired = false;
        foreach (InputBuffer buffer in buffers)
        {
            buffer.Update(delta);
            if (!buffer.IsValid) anyExpired = true;
        }
        if (anyExpired)
            buffers.RemoveAll(b => !b.IsValid);
    }

    private InputBuffer GetValidBuffer(string action)
    {
        return buffers.Find(b => b.BufferedAction == action && b.IsValid);
    }
    
    private class InputBuffer
    {
        public string BufferedAction { get; private set; }
        public float ExpireTime { get; private set; }

        public bool IsValid => ExpireTime > 0f;

        public void Update(double delta)
        {
            ExpireTime = Mathf.Max(ExpireTime - (float)delta, 0f);
        }

        public void SetDuration(float value)
        {
            ExpireTime = value;
        }

        public InputBuffer(string action, float duration)
        {
            BufferedAction = action;
            ExpireTime = duration;
        }
    }
}
