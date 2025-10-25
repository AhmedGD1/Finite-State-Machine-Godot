using Godot;

[GlobalClass]
public partial class HurtboxComponent : Area2D
{
    [Export] private HealthComponent healthComponent;
    [Export] private KnockbackComponent knockbackComponent;

    private CollisionShape2D collisionShape;

    public override void _Ready()
    {
        collisionShape = GetChild<CollisionShape2D>(0);
    }

    public void ReceiveDamage(DamageData data)
    {
        if (healthComponent?.TakeDamage(data) ?? false)
            knockbackComponent?.ApplyKnockback(data.KbForce, data.HitDirection);
    }

    public void SetActive(bool active)
    {
        collisionShape.SetDeferred("disabled", !active);
    }

    public void DisableFor(float duration)
    {
        SetActive(false);
        GetTree().CreateTimer(duration).Timeout += () => SetActive(true);
    }
}

