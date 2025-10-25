using Godot;

[GlobalClass]
public partial class HitboxComponent : Area2D
{
    [Signal] public delegate void HurtboxDetectedEventHandler(HurtboxComponent hurtbox);

    [Export] private HealthComponent.DamageType damageType;

    [Export(PropertyHint.Range, "1, 1000")] 
    private float damage = 1f;

    [Export(PropertyHint.Range, "1, 1000")]
    private float kbForce;

    private CollisionShape2D collisionShape;

    public override void _Ready()
    {
        AreaEntered += OnAreaEntered;

        collisionShape = GetChild<CollisionShape2D>(0);
    }

    private void OnAreaEntered(Area2D area)
    {
        if (area is HurtboxComponent hurtbox)
        {
            Vector2 dir = hurtbox.GlobalPosition.DirectionTo(GlobalPosition);
            DamageData damageData = new DamageData(Owner, damage, damageType, kbForce, dir);
            hurtbox.ReceiveDamage(damageData);
        }
    }

    public void SetActive(bool active)
    {
        collisionShape.SetDeferred("disabled", !active);
    }

    public void EnableFor(float duration)
    {
        SetActive(true);
        GetTree().CreateTimer(duration).Timeout += () => SetActive(false);
    }
}

