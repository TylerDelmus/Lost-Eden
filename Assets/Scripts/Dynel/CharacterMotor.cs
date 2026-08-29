using System;
using System.Collections.Generic;
using SmokeLounge.AOtomation.Messaging.GameData;
using UnityEngine;
using MovementAction = AOSharp.Common.GameData.MovementAction;
using MovementState = AOSharp.Common.GameData.MovementState;

[Flags]
public enum MovementFlags
{
    None = 0,
    Forward = 1 << 0,
    Backward = 1 << 1,
    TurnLeft = 1 << 2,
    TurnRight = 1 << 3,
    StrafeLeft = 1 << 4,
    StrafeRight = 1 << 5,
    Jump = 1 << 6,
    MouseTurn = 1 << 7,
}

public readonly struct VelocityLimits
{
    public float Forward { get; }
    public float Backward { get; }
    public float Strafe { get; }

    public VelocityLimits(float forward, float backward, float strafe)
    {
        Forward = forward;
        Backward = backward;
        Strafe = strafe;
    }
}

[RequireComponent(typeof(CharacterController))]
public class CharacterMotor : MonoBehaviour
{
    const string MovementConfigResourcePath = "MovementConfig";

    const byte AxisMoving = 2;
    const byte StrafeActive = 2;
    const byte TurnActive = 4;
    const byte JumpActive = 3;
    const byte DirForward = 1;
    const byte DirReverse = 2;
    const byte DirLeft = 3;
    const byte DirRight = 4;

    [SerializeField] MovementConfig _movementConfig;

    CharacterController _controller;
    float _verticalVelocity;
    Vector3 _velocity;
    Vector3 _desiredVelocity;
    VelocityLimits _runLimits;
    int _jumpStrength;
    int _jumpAgility;
    int _jumpGmLevel;
    bool _jumpArmed = true;

    MovementFlags _flags;
    MovementState _state = MovementState.Run;
    MovementState _lastSpeedMode = MovementState.Run;

    public event Action JumpStarted;
    public event Action JumpLanded;

    readonly List<Vector3> _path = new();
    int _pathIndex = -1;

    bool HasPath => _pathIndex >= 0 && _pathIndex < _path.Count;

    static readonly MovementFlags TranslationFlags =
        MovementFlags.Forward | MovementFlags.Backward |
        MovementFlags.StrafeLeft | MovementFlags.StrafeRight;

    public MovementConfig Config
    {
        get
        {
            if (_movementConfig == null)
                _movementConfig = Resources.Load<MovementConfig>(MovementConfigResourcePath);
            return _movementConfig;
        }
    }

    public MovementFlags MovementFlags
    {
        get => _flags;
        private set => _flags = value;
    }

    public MovementState State => _state;

    public float CurrentSpeed => _velocity.magnitude;
    public float DesiredSpeed => _desiredVelocity.magnitude;
    public Vector3 Velocity => _velocity;
    public Vector3 DesiredVelocity => _desiredVelocity;
    public float RunForward => _runLimits.Forward;

    public float LocomotionMaxSpeed => GetLocomotionMaxSpeed();

    public float MaxForce => ComputeMaxForce(GetLocomotionMaxSpeed());

    float SpeedStopEpsilon => Config != null ? Config.SpeedStopEpsilon : 0.05f;
    float WalkBaseVelocity => Config != null ? Config.WalkBaseVelocity : 1.5f;
    float WaypointArrivalRadius => Config != null ? Config.WaypointArrivalRadius : 0.5f;

    public bool IsMoving => CurrentSpeed > SpeedStopEpsilon
        || (_flags & TranslationFlags) != 0
        || HasPath;

    /// <summary>
    /// Logical animation name for current locomotion (idle, run/walk, run-back/walk-back, walk-left, walk-right).
    /// </summary>
    public string GetLocomotionLogicalName()
    {
        if ((_flags & TranslationFlags) == 0 && !HasPath)
            return GetIdleLogicalName();

        bool walking = _state == MovementState.Walk;

        if (HasPath || (_flags & MovementFlags.Forward) != 0)
            return walking ? "walk" : "run";

        if ((_flags & MovementFlags.Backward) != 0)
            return walking ? "walk-back" : "run-back";

        if ((_flags & MovementFlags.StrafeLeft) != 0)
            return "walk-left";

        if ((_flags & MovementFlags.StrafeRight) != 0)
            return "walk-right";

        return walking ? "walk" : "run";
    }

    public string GetIdleLogicalName()
    {
        if (_state == MovementState.Sit)
            return "idle-sit";

        return "idle";
    }

    /// <summary>
    /// Takeoff clip from planar intent at launch.
    /// </summary>
    public string GetJumpTakeoffLogicalName()
    {
        if ((_flags & TranslationFlags) != 0 || HasPath || CurrentSpeed > SpeedStopEpsilon)
            return "jump-forward";

        return "jump-stand";
    }

    /// <summary>
    /// Land clip from planar intent at touchdown.
    /// Forward walk/run → land-walk/land-run; back, strafe, or idle → land-idle.
    /// </summary>
    public string GetJumpLandLogicalName()
    {
        if ((_flags & TranslationFlags) == 0 && !HasPath)
            return "jump-land-idle";

        // Back/strafe use idle land overlaid on directional locomotion.
        bool forward = HasPath || (_flags & MovementFlags.Forward) != 0;
        if (!forward)
            return "jump-land-idle";

        if (_state == MovementState.Walk)
            return "jump-land-walk";

        return "jump-land-run";
    }

    VelocityLimits GetActiveLimits()
    {
        if (_state != MovementState.Walk)
            return _runLimits;

        return new VelocityLimits(
            Mathf.Min(_runLimits.Forward, WalkBaseVelocity),
            Mathf.Min(_runLimits.Backward, WalkBaseVelocity),
            Mathf.Min(_runLimits.Strafe, WalkBaseVelocity));
    }

    /// <summary>
    /// Max planar speed for the active locomotion direction (stat caps, not current speed).
    /// Walk mode is hard-capped at <see cref="MovementConfig.WalkBaseVelocity"/>.
    /// Forward/back + strafe keeps forward/back speed and only redirects.
    /// </summary>
    public float GetLocomotionMaxSpeed()
    {
        if (HasPath)
            return GetActiveLimits().Forward;

        Vector3 planar = ComputeActionPlanarVelocity();
        if (planar.sqrMagnitude > 1e-6f)
            return planar.magnitude;

        return GetActiveLimits().Forward;
    }

    /// <summary>
    /// Planar intent from movement flags. Combined axes share one speed
    /// (forward &gt; backward &gt; strafe) and only change direction.
    /// </summary>
    Vector3 ComputeActionPlanarVelocity()
    {
        VelocityLimits limits = GetActiveLimits();
        Vector3 dir = Vector3.zero;
        if ((_flags & MovementFlags.Forward) != 0)
            dir += transform.forward;
        if ((_flags & MovementFlags.Backward) != 0)
            dir -= transform.forward;
        if ((_flags & MovementFlags.StrafeRight) != 0)
            dir += transform.right;
        if ((_flags & MovementFlags.StrafeLeft) != 0)
            dir -= transform.right;

        if (dir.sqrMagnitude < 1e-6f)
            return Vector3.zero;

        float speed;
        if ((_flags & MovementFlags.Forward) != 0)
            speed = limits.Forward;
        else if ((_flags & MovementFlags.Backward) != 0)
            speed = limits.Backward;
        else
            speed = limits.Strafe;

        return dir.normalized * speed;
    }

    /// <summary>
    /// Authored mode base velocity for playback-rate scaling (not stat-scaled max speed).
    /// </summary>
    public float GetLocomotionBaseVelocity()
    {
        MovementConfig config = Config;
        float walkBase = WalkBaseVelocity;
        float runForwardBase = config != null ? config.RunForwardBase : 5f;
        float runBackwardBase = config != null ? config.RunBackwardBase : 3f;
        float runStrafeBase = config != null ? config.RunStrafeBase : 2.5f;

        if (_state == MovementState.Walk)
            return walkBase;

        if (HasPath || (_flags & MovementFlags.Forward) != 0)
            return runForwardBase;

        if ((_flags & MovementFlags.Backward) != 0)
            return runBackwardBase;

        if ((_flags & MovementFlags.StrafeLeft) != 0 || (_flags & MovementFlags.StrafeRight) != 0)
            return runStrafeBase;

        return runForwardBase;
    }

    public void UpdateRunLimitsFromStats(int runSpeed, int currentHealth, int maxHealth)
        => _runLimits = ComputeRunLimits(runSpeed, currentHealth, maxHealth);

    public void UpdateJumpStatsFromStats(int strength, int agility, int gmLevel)
    {
        _jumpStrength = strength;
        _jumpAgility = agility;
        _jumpGmLevel = gmLevel;
    }

    /// <summary>
    /// AO playback rate: calibration × (100 / animSpeedStat) × (desiredSpeed / baseVelocity).
    /// </summary>
    public float ComputeLocomotionPlaybackRate(
        float desiredSpeed,
        float baseVelocity,
        int animSpeedStat,
        float calibration = 1f)
    {
        if (baseVelocity <= 0f)
            return 1f;

        MovementConfig config = Config;
        float rateMax = config != null ? config.RunPlaybackRateMax : 1.3f;
        float rateSpeedThreshold = config != null ? config.RunPlaybackRateSpeedThreshold : 4f;

        int stat = animSpeedStat > 0 ? animSpeedStat : 100;
        float rate = calibration * (100f / stat) * (desiredSpeed / baseVelocity);

        if (rate > rateMax && desiredSpeed > rateSpeedThreshold)
            rate = rateMax;

        return rate;
    }

    float ComputeStatFactor(int runSpeed, int currentHealth, int maxHealth)
    {
        if (maxHealth <= 0)
            return runSpeed;

        MovementConfig config = Config;
        float healthPenaltyThreshold = config != null ? config.HealthPenaltyThreshold : 0.15f;
        float statFactorOffset = config != null ? config.StatFactorOffset : 1000f;

        float ratio = currentHealth / (maxHealth * healthPenaltyThreshold);
        if (ratio < 1f)
            return ratio * (runSpeed + statFactorOffset) - statFactorOffset;

        return runSpeed;
    }

    float ComputeMaxForce(float maxVel)
    {
        MovementConfig config = Config;
        float mass = config != null ? config.Mass : 50f;
        float forceReachTime = config != null ? config.ForceReachTime : 0.5f;
        return mass * maxVel / forceReachTime;
    }

    void IntegrateVelocity(Vector3 desiredVelocity, float maxVel, float dt)
    {
        MovementConfig config = Config;
        float mass = config != null ? config.Mass : 50f;
        float speedStopEpsilon = SpeedStopEpsilon;

        float maxForce = ComputeMaxForce(maxVel);
        Vector3 steerForce = (desiredVelocity - _velocity) * maxForce;
        Vector3 force = Vector3.ClampMagnitude(steerForce, maxForce);
        _velocity += force / mass * dt;
        _velocity = Vector3.ClampMagnitude(_velocity, maxVel);

        // Only snap to zero when not trying to move — otherwise low max speeds
        // (walk/strafe/back) never exceed SpeedStopEpsilon on the first frames.
        if (desiredVelocity.sqrMagnitude < 1e-6f
            && _velocity.sqrMagnitude < speedStopEpsilon * speedStopEpsilon)
            _velocity = Vector3.zero;
    }

    VelocityLimits ComputeRunLimits(int runSpeed, int currentHealth, int maxHealth)
    {
        MovementConfig config = Config;
        float statFactor = ComputeStatFactor(runSpeed, currentHealth, maxHealth);

        float forwardSlope = config != null ? config.RunForwardSlope : 1f / 275f;
        float forwardBase = config != null ? config.RunForwardBase : 5f;
        float forwardMin = config != null ? config.RunForwardMin : 1.5f;
        float forwardMax = config != null ? config.RunForwardMax : 13f;

        float backwardSlope = config != null ? config.RunBackwardSlope : 0.0025454545f;
        float backwardBase = config != null ? config.RunBackwardBase : 3f;
        float backwardMin = config != null ? config.RunBackwardMin : 1.05f;
        float backwardMax = config != null ? config.RunBackwardMax : 9.1f;

        float strafeBase = config != null ? config.RunStrafeBase : 2.5f;
        float strafeSlope = config != null ? config.RunStrafeSlope : 0.5f / 275f;
        float strafeMin = config != null ? config.RunStrafeMin : 0.75f;
        float strafeMax = config != null ? config.RunStrafeMax : 6.5f;

        return new VelocityLimits(
            Mathf.Clamp(statFactor * forwardSlope + forwardBase, forwardMin, forwardMax),
            Mathf.Clamp(statFactor * backwardSlope + backwardBase, backwardMin, backwardMax),
            Mathf.Clamp(strafeBase + statFactor * strafeSlope, strafeMin, strafeMax));
    }

    void Awake()
    {
        _controller = GetComponent<CharacterController>();
        _runLimits = ComputeRunLimits(0, 1, 1);
    }

    public void Warp(Vector3 position, Quaternion rotation, bool resetVelocity = true)
    {
        float yawDelta = Mathf.DeltaAngle(transform.eulerAngles.y, rotation.eulerAngles.y);

        bool wasEnabled = _controller.enabled;
        _controller.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        _controller.enabled = wasEnabled;

        if (resetVelocity)
        {
            MovementConfig config = Config;
            _verticalVelocity = config != null ? config.GroundStickVelocity : -2f;
            _jumpArmed = true;
            Halt();
        }
        else if (!Mathf.Approximately(yawDelta, 0f))
        {
            _velocity = Quaternion.Euler(0f, yawDelta, 0f) * _velocity;
        }
    }

    public void ApplyMovementStatus(CharMovementStatus status)
    {
        ClearPath();
        _state = ToMovementState(status.ModeId);
        _lastSpeedMode = ToMovementState(status.LastSpeedMode);

        MovementFlags flags = MovementFlags.None;

        // FwdState: 1=stopped, 2=moving. FwdDir: 0=none, 1=forward, 2=reverse
        if (status.FwdState == AxisMoving)
        {
            if (status.FwdDir == DirForward)
                flags |= MovementFlags.Forward;
            else if (status.FwdDir == DirReverse)
                flags |= MovementFlags.Backward;
        }

        // StrafeState: 1=none, 2=strafing. StrafeDir: 0, 3=left, 4=right
        if (status.StrafeState == StrafeActive)
        {
            if (status.StrafeDir == DirLeft)
                flags |= MovementFlags.StrafeLeft;
            else if (status.StrafeDir == DirRight)
                flags |= MovementFlags.StrafeRight;
        }

        // TurnState: 1=none, 4=turning. TurnDir: 0, 3=left, 4=right
        if (status.TurnState == TurnActive)
        {
            if (status.TurnDir == DirLeft)
                flags |= MovementFlags.TurnLeft;
            else if (status.TurnDir == DirRight)
                flags |= MovementFlags.TurnRight;
        }

        // JumpState: 1=none, 3=jumping
        if (status.JumpState == JumpActive)
            flags |= MovementFlags.Jump;

        SetFlags(flags);
    }

    public void SetPath(IReadOnlyList<Vector3> waypoints)
    {
        ClearPath();
        StopAllFlags();

        if (waypoints == null || waypoints.Count == 0)
            return;

        for (int i = 0; i < waypoints.Count; i++)
            _path.Add(waypoints[i]);

        _pathIndex = 0;
    }

    public void ClearPath()
    {
        _path.Clear();
        _pathIndex = -1;
    }

    public void SetInputs(MovementFlags flags, Quaternion rotation)
    {
        ClearPath();

        if (_state == MovementState.Sit)
        {
            SetFlags(MovementFlags.None);
            return;
        }

        bool jumpRising = (flags & MovementFlags.Jump) != 0
            && (_flags & MovementFlags.Jump) == 0;

        SetFlags(flags);

        if (jumpRising)
            TryStartJump();

        if ((flags & MovementFlags.MouseTurn) != 0)
            SetYaw(rotation.eulerAngles.y);
    }

    public void ApplyAction(MovementAction action)
    {
        ClearPath();

        switch (action)
        {
            case MovementAction.ForwardStart:
                SetFlags(_flags | MovementFlags.Forward);
                break;
            case MovementAction.ForwardStop:
                SetFlags(_flags & ~MovementFlags.Forward);
                break;
            case MovementAction.BackwardStart:
                SetFlags(_flags | MovementFlags.Backward);
                break;
            case MovementAction.BackwardStop:
                SetFlags(_flags & ~MovementFlags.Backward);
                break;
            case MovementAction.StrafeLeftStart:
                SetFlags(_flags | MovementFlags.StrafeLeft);
                break;
            case MovementAction.StrafeLeftStop:
                SetFlags(_flags & ~MovementFlags.StrafeLeft);
                break;
            case MovementAction.StrafeRightStart:
                SetFlags(_flags | MovementFlags.StrafeRight);
                break;
            case MovementAction.StrafeRightStop:
                SetFlags(_flags & ~MovementFlags.StrafeRight);
                break;
            case MovementAction.TurnLeftStart:
                SetFlags(_flags | MovementFlags.TurnLeft);
                break;
            case MovementAction.TurnLeftStop:
                SetFlags(_flags & ~MovementFlags.TurnLeft);
                break;
            case MovementAction.TurnRightStart:
                SetFlags(_flags | MovementFlags.TurnRight);
                break;
            case MovementAction.TurnRightStop:
                SetFlags(_flags & ~MovementFlags.TurnRight);
                break;
            case MovementAction.JumpStart:
                SetFlags(_flags | MovementFlags.Jump);
                TryStartJump(requireGrounded: false);
                break;
            case MovementAction.JumpStop:
                SetFlags(_flags & ~MovementFlags.Jump);
                break;
            case MovementAction.FullStop:
                StopAllFlags();
                break;
            case MovementAction.SwitchToFrozen:
                EnterMovementState(MovementState.Rooted);
                break;
            case MovementAction.SwitchToWalk:
                EnterMovementState(MovementState.Walk);
                break;
            case MovementAction.SwitchToRun:
                EnterMovementState(MovementState.Run);
                break;
            case MovementAction.SwitchToSwim:
                EnterMovementState(MovementState.Swim);
                break;
            case MovementAction.SwitchToCrawl:
                EnterMovementState(MovementState.Crawl);
                break;
            case MovementAction.SwitchToSneak:
                EnterMovementState(MovementState.Sneak);
                break;
            case MovementAction.SwitchToFly:
                EnterMovementState(MovementState.Fly);
                break;
            case MovementAction.SwitchToSit:
                EnterMovementState(MovementState.Sit);
                break;
            case MovementAction.LeaveSwim:
            case MovementAction.LeaveSneak:
            case MovementAction.LeaveSit:
            case MovementAction.LeaveFrozen:
            case MovementAction.LeaveFly:
            case MovementAction.LeaveCrawl:
            case MovementAction.LeaveSleep:
            case MovementAction.LeaveLounge:
                LeaveMovementState();
                break;
        }
    }

    void Update()
    {
        float dt = Time.deltaTime;
        Vector3 desiredVelocity;
        float maxVel;
        if (HasPath)
        {
            desiredVelocity = ComputePathDesiredVelocity(dt);
            maxVel = GetActiveLimits().Forward;
        }
        else
        {
            desiredVelocity = ComputeActionDesiredVelocity(dt);
            maxVel = GetLocomotionMaxSpeed();
        }

        _desiredVelocity = desiredVelocity;

        // Strafe snaps to target speed — no accel ramp (matches AO feel).
        bool strafing = !HasPath
            && ((_flags & (MovementFlags.StrafeLeft | MovementFlags.StrafeRight)) != 0);
        if (strafing)
            _velocity = desiredVelocity;
        else
            IntegrateVelocity(desiredVelocity, maxVel, dt);

        Vector3 move = _velocity;
        MovementConfig config = Config;
        float groundStick = config != null ? config.GroundStickVelocity : -2f;
        float gravity = config != null ? config.Gravity : -20f;
        float terminalVelocity = config != null ? config.TerminalVelocity : 50f;

        if (_controller.isGrounded)
        {
            if (!_jumpArmed && _verticalVelocity <= 0f)
                CompleteLanding();

            if (_verticalVelocity < 0f)
                _verticalVelocity = groundStick;
        }
        else
        {
            _verticalVelocity += gravity * dt;
            _verticalVelocity = Mathf.Clamp(_verticalVelocity, -terminalVelocity, terminalVelocity);
        }

        move.y = _verticalVelocity;
        _controller.Move(move * dt);

        if (!_jumpArmed && _controller.isGrounded && _verticalVelocity <= 0f)
            CompleteLanding();
    }

    bool TryStartJump(bool requireGrounded = true)
    {
        if (!_jumpArmed || _state == MovementState.Sit)
            return false;
        if (requireGrounded && !_controller.isGrounded)
            return false;

        _verticalVelocity = ComputeJumpVerticalVelocity(_jumpStrength, _jumpAgility, _jumpGmLevel);
        _jumpArmed = false;
        JumpStarted?.Invoke();
        return true;
    }

    void CompleteLanding()
    {
        if (_jumpArmed)
            return;

        _jumpArmed = true;
        _flags &= ~MovementFlags.Jump;
        JumpLanded?.Invoke();
    }

    float ComputeJumpVerticalVelocity(int strength, int agility, int gmLevel)
    {
        MovementConfig config = Config;
        float jumpStatCap = config != null ? config.JumpStatCap : 800f;
        float jumpHeightPerStatPool = config != null ? config.JumpHeightPerStatPool : 200f;
        float jumpHeightBase = config != null ? config.JumpHeightBase : 1f;
        float jumpHeightFloor = config != null ? config.JumpHeightFloor : 0.5f;
        float gravity = config != null ? config.Gravity : -20f;

        float str = strength;
        float agi = agility;
        if (str + agi > jumpStatCap && gmLevel == 0)
        {
            str = jumpStatCap;
            agi = 0f;
        }

        float height = (str + agi) / jumpHeightPerStatPool + jumpHeightBase;
        if (height < jumpHeightFloor)
            height = jumpHeightFloor;

        return Mathf.Sqrt(2f * height * Mathf.Abs(gravity));
    }

    Vector3 ComputePathDesiredVelocity(float dt)
    {
        float forwardLimit = GetActiveLimits().Forward;
        float arrivalRadius = WaypointArrivalRadius;
        float speedStopEpsilon = SpeedStopEpsilon;
        MovementConfig config = Config;
        float mass = config != null ? config.Mass : 50f;

        while (HasPath)
        {
            Vector3 target = _path[_pathIndex];
            Vector3 toTarget = target - transform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;
            bool isFinal = _pathIndex >= _path.Count - 1;

            if (!isFinal && distance <= arrivalRadius)
            {
                _pathIndex++;
                continue;
            }

            if (isFinal)
            {
                float speed = _velocity.magnitude;
                float deceleration = ComputeMaxForce(forwardLimit) / mass;
                float stopDistance = (speed * speed) / (2f * Mathf.Max(deceleration, 0.01f));
                bool shouldBrake = distance <= Mathf.Max(arrivalRadius, stopDistance);

                if (shouldBrake && speed <= speedStopEpsilon && distance <= arrivalRadius)
                {
                    ClearPath();
                    return Vector3.zero;
                }

                if (shouldBrake)
                {
                    if (distance > 1e-4f)
                        RotateToward(toTarget / distance, dt);
                    return Vector3.zero;
                }

                Vector3 dir = toTarget / Mathf.Max(distance, 1e-4f);
                RotateToward(dir, dt);
                return dir * forwardLimit;
            }

            Vector3 midDir = toTarget / Mathf.Max(distance, 1e-4f);
            RotateToward(midDir, dt);
            return midDir * forwardLimit;
        }

        return Vector3.zero;
    }

    Vector3 ComputeActionDesiredVelocity(float dt)
    {
        if (_state == MovementState.Sit)
            return Vector3.zero;

        float turn = 0f;
        if ((_flags & MovementFlags.TurnLeft) != 0)
            turn -= 1f;
        if ((_flags & MovementFlags.TurnRight) != 0)
            turn += 1f;
        if (turn != 0f)
            RotateYaw(turn * GetTurnRateRadians() * Mathf.Rad2Deg * dt);

        return ComputeActionPlanarVelocity();
    }

    float GetTurnRateRadians()
    {
        MovementConfig config = Config;
        float movingRate = config != null ? config.TurnRateRadiansMoving : 1.5f;
        float stoppedRate = config != null ? config.TurnRateRadiansStopped : 3.5f;

        bool moving = ((_flags & TranslationFlags) != 0)
            || HasPath
            || CurrentSpeed > SpeedStopEpsilon;
        return moving ? movingRate : stoppedRate;
    }

    void RotateToward(Vector3 direction, float dt)
    {
        if (direction.sqrMagnitude < 1e-6f)
            return;

        float pathTurnRate = Config != null ? Config.PathTurnRateDegrees : 500f;
        Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
        Quaternion next = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            pathTurnRate * dt);
        RotateYaw(Mathf.DeltaAngle(transform.eulerAngles.y, next.eulerAngles.y));
    }

    void SetYaw(float yawDegrees)
    {
        RotateYaw(Mathf.DeltaAngle(transform.eulerAngles.y, yawDegrees));
    }

    void RotateYaw(float yawDeltaDegrees)
    {
        if (Mathf.Approximately(yawDeltaDegrees, 0f))
            return;

        transform.Rotate(0f, yawDeltaDegrees, 0f);
        _velocity = Quaternion.Euler(0f, yawDeltaDegrees, 0f) * _velocity;
    }

    public void Halt()
    {
        _velocity = Vector3.zero;
        _desiredVelocity = Vector3.zero;
    }

    void SetFlags(MovementFlags flags)
    {
        _flags = flags;
        if ((_flags & TranslationFlags) == 0)
            Halt();
    }

    void StopAllFlags()
    {
        SetFlags(MovementFlags.None);
    }

    void EnterMovementState(MovementState state)
    {
        if (_state == MovementState.Walk || _state == MovementState.Run)
            _lastSpeedMode = _state;

        if (state == MovementState.Sit)
        {
            StopAllFlags();
            ClearPath();
        }

        _state = state;
    }

    void LeaveMovementState()
    {
        _state = _lastSpeedMode is MovementState.Walk or MovementState.Run
            ? _lastSpeedMode
            : MovementState.Run;
    }

    static MovementState ToMovementState(uint modeId)
    {
        if (Enum.IsDefined(typeof(MovementState), (int)modeId))
            return (MovementState)modeId;

        return MovementState.Run;
    }

    void OnDrawGizmos()
    {
        if (!HasPath || _path.Count == 0)
            return;

        Vector3 origin = transform.position;

        Gizmos.color = new Color(0.5f, 0.5f, 0.5f, 0.6f);
        int completedEnd = Mathf.Min(_pathIndex, _path.Count - 1);
        for (int i = 0; i < completedEnd; i++)
            Gizmos.DrawLine(_path[i], _path[i + 1]);

        Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.95f);
        Gizmos.DrawLine(origin, _path[_pathIndex]);
        for (int i = _pathIndex + 1; i < _path.Count; i++)
            Gizmos.DrawLine(_path[i - 1], _path[i]);

        for (int i = 0; i < _path.Count; i++)
        {
            bool isCurrent = i == _pathIndex;
            bool isFinal = i == _path.Count - 1;
            Gizmos.color = isCurrent
                ? Color.yellow
                : isFinal
                    ? new Color(0.3f, 1f, 0.4f, 0.95f)
                    : new Color(0.2f, 0.85f, 1f, 0.95f);
            Gizmos.DrawWireSphere(_path[i], isCurrent ? WaypointArrivalRadius * 1.25f : WaypointArrivalRadius);
        }
    }
}
