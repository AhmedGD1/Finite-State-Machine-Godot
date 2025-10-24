using Godot;
using System;
using Godot.Collections;

/// <summary>
/// MADE BY AHMED GD ,
/// A flexible health management component with damage calculation, resistances, immunities, regeneration, and invincibility frames.
/// </summary>
[GlobalClass]
public partial class HealthComponent : Node
{
    /// <summary>
    /// Types of damage that can be dealt to entities.
    /// </summary>
    public enum DamageType
    {
        /// <summary>No specific damage type.</summary>
        Default,
        /// <summary>Damage from poison or toxins.</summary>
        Poison,
        /// <summary>Physical melee damage.</summary>
        Physical,
        /// <summary>Ranged projectile damage.</summary>
        Ranged
    }

    /// <summary>
    /// Regeneration behavior modes.
    /// </summary>
    public enum RegenType
    {
        /// <summary>Smooth continuous healing every frame.</summary>
        Sequential,
        /// <summary>Healing applied in discrete chunks at intervals.</summary>
        Intermittent
    }

    /// <summary>
    /// Emitted when health value changes.
    /// </summary>
    /// <param name="oldHealth">Previous health value.</param>
    /// <param name="newHealth">New health value.</param>
    [Signal] public delegate void HealthChangedEventHandler(float oldHealth, float newHealth);
    
    /// <summary>
    /// Emitted when the entity takes damage.
    /// </summary>
    /// <param name="source">The node that dealt the damage.</param>
    /// <param name="amount">Final damage amount after modifiers.</param>
    [Signal] public delegate void DamagedEventHandler(Node source, float amount);
    
    /// <summary>
    /// Emitted when the entity is healed.
    /// </summary>
    /// <param name="amount">Amount of health restored.</param>
    [Signal] public delegate void HealedEventHandler(float amount);
    
    /// <summary>
    /// Emitted when the entity dies (health reaches MinHealth).
    /// </summary>
    /// <param name="source">The node that dealt the killing blow.</param>
    [Signal] public delegate void DiedEventHandler(Node source);
    
    /// <summary>
    /// Emitted when health reaches maximum value.
    /// </summary>
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

    /// <summary>
    /// Current health value. Read-only.
    /// </summary>
    public float CurrentHealth { get; private set; }
    
    /// <summary>
    /// Minimum health before entity is considered dead. Default is 0.
    /// </summary>
    public float MinHealth { get; private set; }
    
    /// <summary>
    /// Default target health value for regeneration.
    /// </summary>
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

    /// <summary>
    /// Maximum health value. Read-only.
    /// </summary>
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

    /// <summary>
    /// Resets health to maximum and clears all timers.
    /// </summary>
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

    /// <summary>
    /// Ends invincibility frames immediately.
    /// </summary>
    public void ResetInvincibility()
    {
        invincibilityTimer = 0f;
    }

    /// <summary>
    /// Applies damage to the entity using a DamageData object.
    /// </summary>
    /// <param name="data">Damage data containing source, amount, type, and knockback information.</param>
    /// <returns>True if damage was applied, false if immune, invincible, or dead.</returns>
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

    /// <summary>
    /// Applies damage to the entity with simplified parameters.
    /// </summary>
    /// <param name="source">The node dealing the damage.</param>
    /// <param name="amount">Raw damage amount before modifiers.</param>
    /// <param name="damageType">Type of damage for resistance calculation.</param>
    /// <param name="force">Knockback force (for external systems).</param>
    /// <param name="dir">Direction of the hit (defaults to Vector2.Up).</param>
    /// <returns>True if damage was applied, false if immune, invincible, or dead.</returns>
    public bool TakeDamage(Node source, float amount, DamageType damageType = DamageType.Default, float force = 0f, Vector2? dir = null)
    {
        return TakeDamage(new DamageData(source, amount, damageType, force, dir));
    }

    /// <summary>
    /// Instantly kills the entity.
    /// </summary>
    /// <param name="source">The node responsible for the kill.</param>
    public void Kill(Node source)
    {
        TakeDamage(source, maxHealth);
    }

    /// <summary>
    /// Heals the entity by a specified amount.
    /// </summary>
    /// <param name="amount">Amount of health to restore. Must be greater than 0.</param>
    /// <param name="instant">If true, heals immediately. If false, starts regeneration after delay.</param>
    /// <param name="targetRegen">Optional target health for regeneration. Uses DefaultRegenTarget if null.</param>
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

    /// <summary>
    /// Sets the default target health value for regeneration.
    /// </summary>
    /// <param name="value">New default regeneration target.</param>
    public void SetDefaultRegenTarget(float value)
    {
        DefaultRegenTarget = value;
    }

    /// <summary>
    /// Sets the minimum health threshold. Entity is considered dead when health falls below this value.
    /// </summary>
    /// <param name="value">Minimum health value (clamped between 0 and maxHealth - 1).</param>
    public void SetMinHealth(float value)
    {
        MinHealth = Mathf.Clamp(value, 0f, maxHealth - 1f);
    }

    /// <summary>
    /// Checks if the entity is dead (health at or below MinHealth).
    /// </summary>
    /// <returns>True if dead, false otherwise.</returns>
    public bool IsDead()
    {
        return CurrentHealth < MinHealth + 0.001f;
    }

    /// <summary>
    /// Checks if the entity is currently invincible.
    /// </summary>
    /// <returns>True if in invincibility frames, false otherwise.</returns>
    public bool IsInvincible()
    {
        return invincibilityTimer > 0f;
    }

    /// <summary>
    /// Checks if the entity is at maximum health.
    /// </summary>
    /// <returns>True if health equals MaxHealth, false otherwise.</returns>
    public bool IsFullyHealed()
    {
        return CurrentHealth == maxHealth;
    }

    /// <summary>
    /// Checks if the entity is currently regenerating health.
    /// </summary>
    /// <returns>True if regenerating, false otherwise.</returns>
    public bool IsRegenerating()
    {
        return isRegenerating;
    }
}

/// <summary>
/// Data structure containing all information about a damage event.
/// </summary>
public class DamageData
{
    /// <summary>
    /// Type of damage being dealt.
    /// </summary>
    public HealthComponent.DamageType DamageType { get; private set; }
    
    /// <summary>
    /// The node that dealt the damage.
    /// </summary>
    public Node Source { get; private set; }
    
    /// <summary>
    /// Raw damage amount before defense and resistance modifiers.
    /// </summary>
    public float Damage { get; private set; }
    
    /// <summary>
    /// Knockback force (for use with external knockback systems).
    /// </summary>
    public float KbForce { get; private set; }
    
    /// <summary>
    /// Direction of the hit. Defaults to Vector2.Up if not specified.
    /// </summary>
    public Vector2 HitDirection { get; private set; }

    /// <summary>
    /// Creates a new DamageData instance.
    /// </summary>
    /// <param name="source">The node dealing the damage.</param>
    /// <param name="damage">Raw damage amount.</param>
    /// <param name="type">Type of damage.</param>
    /// <param name="kbForce">Knockback force.</param>
    /// <param name="hitDirection">Direction of the hit (optional, defaults to Vector2.Up).</param>
    public DamageData(Node source, float damage, HealthComponent.DamageType type, float kbForce, Vector2? hitDirection = null)
    {
        Source = source;
        Damage = damage;
        DamageType = type;
        KbForce = kbForce;
        HitDirection = hitDirection ?? Vector2.Up;
    }
}
