using Godot;
using System;
using Godot.Collections;

[GlobalClass]
public partial class HealthComponent : Node
{
    public enum DamageType
    {
        Default,
        Poison,
        Physical,
        Ranged
    }

    public enum RegenType
    {
        Sequential,
        Intermittent
    }

    [Signal] public delegate void HealthChangedEventHandler(float oldHealth, float newHealth);
    [Signal] public delegate void DamagedEventHandler(Node source, float amount);
    [Signal] public delegate void HealedEventHandler(float amount);
    [Signal] public delegate void DiedEventHandler(Node source);
    [Signal] public delegate void FullyHealedEventHandler();

    [Export(PropertyHint.Range, "1, 1000")]
    private float maxHealth = 1;

    [Export(PropertyHint.Range, "0, 1")]
    private float invincibilityTime;

    [ExportGroup("Damage & Defense")]
    [Export(PropertyHint.Range, "0, 1000")]
    private float Defense { get; set; } = 0f;

    [Export]
    private Dictionary<DamageType, float> Resistances { get; set; } = new()
    {
        { DamageType.Default, 0f },
        { DamageType.Physical, 0f },
        { DamageType.Poison, 0f },
        { DamageType.Ranged, 0f }
    }; 

    [Export] private Array<DamageType> immunity = [];

    [ExportGroup("Regeneration Settings")]

    [Export]
    private RegenType regenType = RegenType.Sequential;

    [Export(PropertyHint.Range, "0, 10")]
    private float regenRate;

    [Export(PropertyHint.Range, "0, 1")]
    private float regenDelay;

    [Export(PropertyHint.Range, "0, 10")]
    private float regenCooldown;

    [Export]
    private float intermittentTime = 0.3f;

    public float CurrentHealth { get; private set; }
    public float MinHealth { get; private set; }
    public float DefaultRegenTarget { get; private set; }

    private float invincibilityTimer;
    private float intermittentTimer;
    private float regenDelayTimer;
    private float regenCooldownTimer;
    private float stackedHealth;
    private float activeRegenTarget;

    private bool isRegenerating;
    private bool wasRegenerating;
    private bool fullyHealedTriggered;

    public float MaxHealth => maxHealth;

    public override void _Ready()
    {
        CurrentHealth = maxHealth;
        DefaultRegenTarget = maxHealth;
        MinHealth = 0f;

        activeRegenTarget = DefaultRegenTarget;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (invincibilityTimer > 0f)
        {
            invincibilityTimer -= (float)delta;
            
            if (invincibilityTimer <= 0f && wasRegenerating)
            {
                isRegenerating = true;
                wasRegenerating = false;
            }
        }

        if (regenCooldownTimer > 0f)
        {
            regenCooldownTimer -= (float)delta;

            if (regenCooldownTimer <= 0f && !IsDead())
                isRegenerating = true;
        }

        if (regenDelayTimer > 0f)
        {
            regenDelayTimer -= (float)delta;
            if (regenDelayTimer <= 0f)
                isRegenerating = true;
        }

        if (regenRate > 0 && isRegenerating)
        {
            float amount = regenRate * (float)delta;
            
            switch (regenType)
            {
                case RegenType.Sequential:
                    SetHealth(CurrentHealth + amount);
                    EmitSignal(SignalName.Healed, amount);
                    break;
                
                case RegenType.Intermittent:
                    intermittentTimer -= (float)delta;
                    stackedHealth += amount;

                    if (intermittentTimer <= 0f)
                    {
                        SetHealth(CurrentHealth + stackedHealth);
                        EmitSignal(SignalName.Healed, stackedHealth);
                        stackedHealth = 0f;
                        intermittentTimer = intermittentTime;
                    }
                    break;
            }
            
            if (CurrentHealth >= activeRegenTarget)
            {
                isRegenerating = false;
                stackedHealth = 0f;
                intermittentTimer = intermittentTime;
            }
        }
    }

    public void ResetHealth()
    {
        SetHealth(maxHealth);
        invincibilityTimer = 0f;
        regenDelayTimer = 0f;
        regenCooldownTimer = 0f;
        intermittentTimer = intermittentTime;
        isRegenerating = false;
        wasRegenerating = false;
        stackedHealth = 0f;
    }

    public void ResetInvincibility()
    {
        invincibilityTimer = 0f;
    }

    public bool TakeDamage(DamageData data)
    {
        bool isImmune = immunity.Count > 0 && immunity.Contains(data.DamageType);

        if (IsInvincible() || IsDead() || isImmune)
            return false;
        
        
        bool wasHealing = isRegenerating;
        isRegenerating = false;
        wasRegenerating = wasHealing;

        float finalDamage = Mathf.Max(0f,GetDamageAfterModifiers(data));

        SetHealth(CurrentHealth - finalDamage);
        EmitSignal(SignalName.Damaged, data.Source, finalDamage);

        if (IsDead())
        {
            EmitSignal(SignalName.Died, data.Source);
            regenDelayTimer = 0f;
            isRegenerating = false;
        }
        
        invincibilityTimer = invincibilityTime;
        regenCooldownTimer = regenCooldown;
        return true;
    }

    public bool TakeDamage(Node source, float amount, DamageType damageType = DamageType.Default, float force = 0f, Vector2? dir = null)
    {
        return TakeDamage(new DamageData(source, amount, damageType, force, dir));
    }

    public void Kill(Node source)
    {
        TakeDamage(source, maxHealth);
    }

    public void Heal(float amount, bool instant = true, float? targetRegen = null)
    {
        if (amount <= 0f)
        {
            GD.PushError($"{Owner.Name} Can only heal if heal amount was greater than zero. Current amount: {amount}");
            return;
        }

        if (instant)
        {
            SetHealth(CurrentHealth + amount);
            EmitSignal(SignalName.Healed, amount);
            return;
        }

        activeRegenTarget = Mathf.Clamp(targetRegen ?? DefaultRegenTarget, MinHealth, maxHealth);
        intermittentTimer = intermittentTime;
        
        if (!isRegenerating && CurrentHealth < activeRegenTarget)
            regenDelayTimer = regenDelay;
    }

    private float GetDamageAfterModifiers(DamageData data)
    {
        float result = Mathf.Max(0f, data.Damage - Defense);
        if (Resistances.TryGetValue(data.DamageType, out float resistance))
            result *= 1f - Mathf.Clamp(resistance, 0f, 1f);
        return Mathf.Max(0f, result);
    }

    private void SetHealth(float value)
    {
        float oldHealth = CurrentHealth;
        CurrentHealth = Mathf.Clamp(value, MinHealth, maxHealth);
        EmitSignal(SignalName.HealthChanged, oldHealth, CurrentHealth);

        if (CurrentHealth < maxHealth)
        {
            fullyHealedTriggered = false;
            return;
        }

        if (!fullyHealedTriggered)
        {
            EmitSignal(SignalName.FullyHealed);
            fullyHealedTriggered = true;
        }
    }

    public void SetDefaultRegenTarget(float value)
    {
        DefaultRegenTarget = value;
    }

    public void SetMinHealth(float value)
    {
        MinHealth = Mathf.Clamp(value, 0f, maxHealth - 1f);
    }

    public bool IsDead()
    {
        return CurrentHealth < MinHealth + 0.001f;
    }

    public bool IsInvincible()
    {
        return invincibilityTimer > 0f;
    }

    public bool IsFullyHealed()
    {
        return CurrentHealth == maxHealth;
    }

    public bool IsRegenerating()
    {
        return isRegenerating;
    }
}

public class DamageData
{
    public HealthComponent.DamageType DamageType { get; private set; }
    public Node Source { get; private set; }
    public float Damage { get; private set; }
    public float KbForce { get; private set; }
    public Vector2 HitDirection { get; private set; }

    public DamageData(Node source, float damage, HealthComponent.DamageType type, float kbForce, Vector2? hitDirection = null)
    {
        Source = source;
        Damage = damage;
        DamageType = type;
        KbForce = kbForce;
        HitDirection = hitDirection ?? Vector2.Up;
    }
}

