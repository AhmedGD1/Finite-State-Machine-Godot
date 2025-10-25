using Godot;

[GlobalClass]
public partial class KnockbackComponent : Node
{
    [Signal] public delegate void KnockbackFinishedEventHandler();

    private CharacterBody2D controller;

    public bool IsActive { get; private set; }
    public float KbDuration { get; private set; }

    private float kbTime;

    public override void _Ready()
    {
        controller = GetOwner<CharacterBody2D>();
    }

    public void ApplyKnockback(float force, Vector2 direction, float? duration = null)
    {
        controller.Velocity += direction.Normalized() * force;
        kbTime = duration ?? KbDuration;
        IsActive = true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!IsActive)
            return;

        kbTime -= (float)delta;

        float smoothing = ExponentialSmoothing(delta);
        controller.Velocity = controller.Velocity.Lerp(Vector2.Zero, smoothing);

        if (kbTime <= 0f)
        {
            EmitSignal(SignalName.KnockbackFinished);
            IsActive = false;
        }
    }

    public void SetKbDuration(float value)
    {
        KbDuration = value;
    }

    private float ExponentialSmoothing(double delta)
    {
        return 1f - Mathf.Exp(-5f * (float)delta);
    }
}

