using System;
using Godot;
using System.Linq;

[GlobalClass]
public partial class VelocityComponent : Node
{
    public event Action Landed;
    public event Action MaxHeightReached;
    public event Action LeftGround;
    public event Action Jumped;

    private const float CoyoteTime = 0.15f;
    private const float JumpBufferingTime = 0.15f;

    [Export(PropertyHint.Range, "1, 100")]
    public float mass = 1f;

    [Export(PropertyHint.Range, "1, 1000")]
    public float maxSpeed = 100f;

    [Export(PropertyHint.Range, "1, 100")]
    public float acceleration = 30f;

    [Export(PropertyHint.Range, "1, 100")]
    public float deceleration = 35f;

    [Export] public bool useGravity = true;
    [Export] public bool autoCoyote;

    [ExportGroup("Air Settings")]

    [Export(PropertyHint.Range, "1, 100")]
    public float airAccel = 10f;

    [Export(PropertyHint.Range, "1, 100")]
    public float airDecel = 10f;

    [ExportSubgroup("Gravity Settings")]
    [Export(PropertyHint.Range, "0.1, 10")]
    public float gravityScale = 1f;

    [Export(PropertyHint.Range, "1, 200")]
    public float jumpHeight = 40f;

    [Export(PropertyHint.Range, "0.1, 1")]
    public float timeToApex = 0.3f;

    [ExportGroup("Wall Settings")]
    [Export] private RayCast2D[] wallDetectors = [];

    public Vector2 velocity
    {
        get => controller.Velocity;
        set => controller.Velocity = value;
    }

    public bool isGrounded { get; private set; }
    public bool wasGrounded { get; private set; }
    public bool isOnWall { get; private set; }
    public bool isFalling { get; private set; }

    private float gravity => CalculateGravity(jumpHeight, timeToApex);
    private float jumpVelocity => CalculateJumpVelocity(gravity);

    private CharacterBody2D controller;
    private bool isFloatingMode;

    public float jumpResistanceFactor = 0.2f;

    private float coyoteTimer;
    private float jumpBufferingTimer;
    private float timer;

    private double lastPhysicsDelta;

    private bool startTimer;
    private bool jumpedThisFrame;

    public override void _Ready()
    {
        controller = GetOwner<CharacterBody2D>();

        isFloatingMode = controller.MotionMode == CharacterBody2D.MotionModeEnum.Floating;
    }

    public override void _PhysicsProcess(double delta)
    {
        lastPhysicsDelta = delta;

        UpdateTimers(delta);
        ApplyGravity(delta);
        controller.MoveAndSlide();
        UpdateFloorInfo();
        UpdateMaxHeightInfo(delta);
        UpdateWallInfo();

        isFalling = velocity.Y > 0f;
        jumpedThisFrame = false;
    }

    private void UpdateFloorInfo()
    {
        if (isFloatingMode)
            return;
        
        wasGrounded = isGrounded;
        isGrounded = controller.IsOnFloor();

        if (wasGrounded && !isGrounded)
        {
            LeftGround?.Invoke();

            if (!jumpedThisFrame && autoCoyote)
                StartCoyote();
        }

        if (!wasGrounded && isGrounded)
            Landed?.Invoke();
    }

    private void UpdateWallInfo()
    {
        isOnWall = false;

        if (wallDetectors.Length == 0)
        {
            isOnWall = controller.IsOnWall();
            return;
        }

        foreach (RayCast2D rayCast in wallDetectors)
        {
            if (rayCast.IsColliding())
            {
                isOnWall = true;
                break;
            }
        }
    }

    private void UpdateMaxHeightInfo(double delta)
    {
        if (!startTimer) return;

        timer += (float)delta;

        bool stop = isGrounded || timer >= timeToApex;

        if (stop)
        {
            startTimer = false;

            if (timer >= timeToApex)
                MaxHeightReached?.Invoke();
            timer = 0f;
        }
    }

    private void UpdateTimers(double delta)
    {
        if (coyoteTimer > 0f)
            coyoteTimer -= (float)delta;
        
        if (jumpBufferingTimer > 0f)
            jumpBufferingTimer -= (float)delta;
    }

    private float GetWallDirection()
    {
        if (wallDetectors.Length == 0)
        {
            GD.PushWarning("Can't get the current wall direction because wall detectors does not exist");
            return 0f;
        }

        bool left = wallDetectors.Any(r => r.IsColliding() && r.TargetPosition.X < 0f);
        bool right = wallDetectors.Any(r => r.IsColliding() && r.TargetPosition.X > 0f);

        float leftIndex = left ? -1f : 0f;
        float rightIndex = right ? 1f : 0f;

        return leftIndex + rightIndex;
    }

    private void ApplyGravity(double delta)
    {
        if (!useGravity || isGrounded || isFloatingMode)
            return;
        
        velocity += Vector2.Down * gravity * (float)delta;
    }

    public void Accelerate(Vector2 direction, double delta, float? customSpeed = null, float? customAccel = null) 
    {
        float appliedSpeed = customSpeed ?? maxSpeed;
        float accel = isFloatingMode || isGrounded ? acceleration : airAccel;
        float appliedAccel = customAccel ?? accel;
        float smoothing = ExponentialSmoothing(appliedAccel, delta);

        Vector2 desired = direction.Normalized() * appliedSpeed;
        Vector2 simulated = velocity.Lerp(desired, smoothing);

        velocity = new Vector2(simulated.X, isFloatingMode ? simulated.Y : velocity.Y);
    }

    public void Decelerate(double delta, float? value = null)
    {
        float decel = isFloatingMode || isGrounded ? deceleration : airDecel;
        float appliedDecel = value ?? decel;
        float smoothing = ExponentialSmoothing(appliedDecel, delta);

        Vector2 simulated = velocity.Lerp(Vector2.Zero, smoothing);
        velocity = new Vector2(simulated.X, isFloatingMode ? simulated.Y : velocity.Y);
    }

    public void AddForce(Vector2 value, ForceMode mode)
    {
        switch (mode)
        {
            case ForceMode.Impulse:
                velocity += value / mass;
                break;
            
            case ForceMode.Force:
                velocity += value * (float)lastPhysicsDelta / mass;
                break;
        }
    }

    public void Jump()
    {
        velocity = new Vector2(velocity.X, -jumpVelocity);
        jumpedThisFrame = true;
        startTimer = true;

        Jumped?.Invoke();
    }

    public bool CanJump(bool condition, bool ignoreFloor = false)
    {
        bool cond = ignoreFloor || isGrounded;
        return (HasCoyote() || cond) && (condition || HasBufferedJump());
    }

    public bool TryConsumeJump(bool condition, bool ignoreFloor = false)
    {
        if (!CanJump(condition, ignoreFloor))
            return false;

        ConsumeBufferedJump();
        ConsumeCoyote();
        Jump();
        
        return true;
    }

    public void ApplyJumpResistance(bool condition)
    {
        if (!condition) return;

        velocity += Vector2.Down * gravity * (1f + jumpResistanceFactor) * (float)lastPhysicsDelta;
    }

    public void StartCoyote() => coyoteTimer = CoyoteTime;
    public bool HasCoyote() => coyoteTimer > 0f;
    public void ConsumeCoyote() => coyoteTimer = 0f;

    public void BufferJump() => jumpBufferingTimer = JumpBufferingTime;
    public bool HasBufferedJump() => jumpBufferingTimer > 0f;
    public void ConsumeBufferedJump() => jumpBufferingTimer = 0f;

    private float CalculateGravity(float height, float t)
    {
        float formula = 2f * height / (t * t);
        return formula * gravityScale;
    }

    private float CalculateJumpVelocity(float grav)
    {
        return Mathf.Sqrt(2f * grav * jumpHeight);
    }

    private float ExponentialSmoothing(float value, double delta)
    {
        return 1f - Mathf.Exp(-value * (float)delta);
    }
}

public enum ForceMode
{
    Impulse,
    Force
}

