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

public readonly struct RunSpeedLimits
{
    public float Forward { get; }
    public float Backward { get; }
    public float Strafe { get; }

    public RunSpeedLimits(float forward, float backward, float strafe)
    {
        Forward = forward;
        Backward = backward;
        Strafe = strafe;
    }
}

[RequireComponent(typeof(CharacterController))]
public class CharacterMotor : MonoBehaviour
{
    const float DefaultTurnRateDegrees = 90f;
    const float PathTurnRateDegrees = 500f;
    const float DefaultAcceleration = 10f;
    const float DefaultDeceleration = 14f;
    const float Gravity = -20f;
    const float GroundStickVelocity = -2f;
    const float WaypointArrivalRadius = 0.5f;
    const float SpeedStopEpsilon = 0.05f;

    const float HealthPenaltyThreshold = 0.15f;
    const float StatFactorOffset = 1000f;

    const float RunForwardSlope = 1f / 275f;
    const float RunForwardBase = 5f;
    const float RunForwardMin = 1.5f;
    const float RunForwardMax = 13f;

    const float RunBackwardSlope = 0.0025454545f;
    const float RunBackwardBase = 3f;
    const float RunBackwardMin = 1.05f;
    const float RunBackwardMax = 9.1f;

    const float RunStrafeBase = 2.5f;
    const float RunStrafeSlope = 0.5f / 275f;
    const float RunStrafeMin = 0.75f;
    const float RunStrafeMax = 6.5f;

    const float RunAnimBaseVelocity = 5f;
    const float RunPlaybackRateMax = 1.3f;
    const float RunPlaybackRateSpeedThreshold = 4f;

    const byte AxisMoving = 2;
    const byte StrafeActive = 2;
    const byte TurnActive = 4;
    const byte JumpActive = 3;
    const byte DirForward = 1;
    const byte DirReverse = 2;
    const byte DirLeft = 3;
    const byte DirRight = 4;

    [SerializeField] float _turnRateDegrees = DefaultTurnRateDegrees;
    [SerializeField] float _acceleration = DefaultAcceleration;
    [SerializeField] float _deceleration = DefaultDeceleration;

    CharacterController _controller;
    float _verticalVelocity;
    float _currentSpeed;
    Vector3 _moveDirection = Vector3.forward;
    RunSpeedLimits _runLimits = ComputeRunLimits(0, 1, 1);

    MovementFlags _flags;
    MovementState _state = MovementState.Run;
    MovementState _lastSpeedMode = MovementState.Run;

    readonly List<Vector3> _path = new();
    int _pathIndex = -1;

    bool HasPath => _pathIndex >= 0 && _pathIndex < _path.Count;

    static readonly MovementFlags TranslationFlags =
        MovementFlags.Forward | MovementFlags.Backward |
        MovementFlags.StrafeLeft | MovementFlags.StrafeRight;

    public MovementFlags MovementFlags
    {
        get => _flags;
        private set => _flags = value;
    }

    public MovementState State => _state;

    public float CurrentSpeed => _currentSpeed;
    public float RunForward => _runLimits.Forward;
    public bool IsMoving => _currentSpeed > SpeedStopEpsilon
        || (_flags & TranslationFlags) != 0
        || HasPath;

    /// <summary>
    /// Logical animation name for current locomotion (idle, run, run-back, walk-left, walk-right).
    /// </summary>
    public string GetLocomotionLogicalName()
    {
        if (!IsMoving)
            return "idle";

        if (HasPath || (_flags & MovementFlags.Forward) != 0)
            return "run";

        if ((_flags & MovementFlags.Backward) != 0)
            return "run-back";

        if ((_flags & MovementFlags.StrafeLeft) != 0)
            return "walk-left";

        if ((_flags & MovementFlags.StrafeRight) != 0)
            return "walk-right";

        return "run";
    }

    /// <summary>
    /// Max planar speed for the active locomotion direction (stat caps, not current speed).
    /// </summary>
    public float GetLocomotionMaxSpeed()
    {
        if (HasPath)
            return _runLimits.Forward;

        Vector3 planar = Vector3.zero;
        if ((_flags & MovementFlags.Forward) != 0)
            planar += transform.forward * _runLimits.Forward;
        if ((_flags & MovementFlags.Backward) != 0)
            planar -= transform.forward * _runLimits.Backward;
        if ((_flags & MovementFlags.StrafeRight) != 0)
            planar += transform.right * _runLimits.Strafe;
        if ((_flags & MovementFlags.StrafeLeft) != 0)
            planar -= transform.right * _runLimits.Strafe;

        if (planar.sqrMagnitude > 1e-6f)
            return planar.magnitude;

        return _runLimits.Forward;
    }

    public void UpdateRunLimitsFromStats(int runSpeed, int currentHealth, int maxHealth)
        => _runLimits = ComputeRunLimits(runSpeed, currentHealth, maxHealth);

    public static float ComputeRunPlaybackRate(float maxSpeed, int animSpeedStat)
    {
        float animNorm = animSpeedStat > 0 ? 100f / animSpeedStat : 1f;
        float rate = animNorm * (maxSpeed / RunAnimBaseVelocity);
        if (rate > RunPlaybackRateMax && maxSpeed > RunPlaybackRateSpeedThreshold)
            rate = RunPlaybackRateMax;
        return rate;
    }

    static float ComputeStatFactor(int runSpeed, int currentHealth, int maxHealth)
    {
        if (maxHealth <= 0)
            return runSpeed;

        float ratio = currentHealth / (maxHealth * HealthPenaltyThreshold);
        if (ratio < 1f)
            return ratio * (runSpeed + StatFactorOffset) - StatFactorOffset;

        return runSpeed;
    }

    static RunSpeedLimits ComputeRunLimits(int runSpeed, int currentHealth, int maxHealth)
    {
        float statFactor = ComputeStatFactor(runSpeed, currentHealth, maxHealth);
        return new RunSpeedLimits(
            Mathf.Clamp(statFactor * RunForwardSlope + RunForwardBase, RunForwardMin, RunForwardMax),
            Mathf.Clamp(statFactor * RunBackwardSlope + RunBackwardBase, RunBackwardMin, RunBackwardMax),
            Mathf.Clamp(RunStrafeBase + statFactor * RunStrafeSlope, RunStrafeMin, RunStrafeMax));
    }

    void Awake()
    {
        _controller = GetComponent<CharacterController>();
    }

    public void Warp(Vector3 position, Quaternion rotation)
    {
        bool wasEnabled = _controller.enabled;
        _controller.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        _controller.enabled = wasEnabled;
        _verticalVelocity = GroundStickVelocity;
        _currentSpeed = 0f;
    }

    public void ApplyMovementStatus(CharMovementStatus status)
    {
        ClearPath();
        _state = ToMovementState(status.ModeId);
        _lastSpeedMode = ToMovementState(status.LastSpeedMode);

        StopAllFlags();

        // FwdState: 1=stopped, 2=moving. FwdDir: 0=none, 1=forward, 2=reverse
        if (status.FwdState == AxisMoving)
        {
            if (status.FwdDir == DirForward)
                _flags |= MovementFlags.Forward;
            else if (status.FwdDir == DirReverse)
                _flags |= MovementFlags.Backward;
        }

        // StrafeState: 1=none, 2=strafing. StrafeDir: 0, 3=left, 4=right
        if (status.StrafeState == StrafeActive)
        {
            if (status.StrafeDir == DirLeft)
                _flags |= MovementFlags.StrafeLeft;
            else if (status.StrafeDir == DirRight)
                _flags |= MovementFlags.StrafeRight;
        }

        // TurnState: 1=none, 4=turning. TurnDir: 0, 3=left, 4=right
        if (status.TurnState == TurnActive)
        {
            if (status.TurnDir == DirLeft)
                _flags |= MovementFlags.TurnLeft;
            else if (status.TurnDir == DirRight)
                _flags |= MovementFlags.TurnRight;
        }

        // JumpState: 1=none, 3=jumping
        if (status.JumpState == JumpActive)
            _flags |= MovementFlags.Jump;
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
        _flags = flags;

        if ((flags & MovementFlags.MouseTurn) != 0)
            transform.rotation = Quaternion.Euler(0f, rotation.eulerAngles.y, 0f);
    }

    public void ApplyAction(MovementAction action)
    {
        ClearPath();

        switch (action)
        {
            case MovementAction.ForwardStart:
                _flags |= MovementFlags.Forward;
                break;
            case MovementAction.ForwardStop:
                _flags &= ~MovementFlags.Forward;
                break;
            case MovementAction.BackwardStart:
                _flags |= MovementFlags.Backward;
                break;
            case MovementAction.BackwardStop:
                _flags &= ~MovementFlags.Backward;
                break;
            case MovementAction.StrafeLeftStart:
                _flags |= MovementFlags.StrafeLeft;
                break;
            case MovementAction.StrafeLeftStop:
                _flags &= ~MovementFlags.StrafeLeft;
                break;
            case MovementAction.StrafeRightStart:
                _flags |= MovementFlags.StrafeRight;
                break;
            case MovementAction.StrafeRightStop:
                _flags &= ~MovementFlags.StrafeRight;
                break;
            case MovementAction.TurnLeftStart:
                _flags |= MovementFlags.TurnLeft;
                break;
            case MovementAction.TurnLeftStop:
                _flags &= ~MovementFlags.TurnLeft;
                break;
            case MovementAction.TurnRightStart:
                _flags |= MovementFlags.TurnRight;
                break;
            case MovementAction.TurnRightStop:
                _flags &= ~MovementFlags.TurnRight;
                break;
            case MovementAction.JumpStart:
                _flags |= MovementFlags.Jump;
                break;
            case MovementAction.JumpStop:
                _flags &= ~MovementFlags.Jump;
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
        float desiredSpeed;
        if (HasPath)
        {
            Vector3 desiredDirection = ComputePathDesiredDirection(dt);
            desiredSpeed = desiredDirection.sqrMagnitude > 1e-6f ? _runLimits.Forward : 0f;
            if (desiredDirection.sqrMagnitude > 1e-6f)
                _moveDirection = desiredDirection.normalized;
        }
        else
        {
            Vector3 desiredVelocity = ComputeActionDesiredVelocity(dt);
            desiredSpeed = desiredVelocity.magnitude;
            if (desiredSpeed > 1e-6f)
                _moveDirection = desiredVelocity / desiredSpeed;
        }

        float rate = desiredSpeed > _currentSpeed ? _acceleration : _deceleration;
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, desiredSpeed, rate * dt);

        Vector3 move = _moveDirection * _currentSpeed;

        if (_controller.isGrounded)
        {
            if (_verticalVelocity < 0f)
                _verticalVelocity = GroundStickVelocity;
        }
        else
        {
            _verticalVelocity += Gravity * dt;
        }

        move.y = _verticalVelocity;
        _controller.Move(move * dt);
    }

    Vector3 ComputePathDesiredDirection(float dt)
    {
        while (HasPath)
        {
            Vector3 target = _path[_pathIndex];
            Vector3 toTarget = target - transform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;
            bool isFinal = _pathIndex >= _path.Count - 1;

            if (!isFinal && distance <= WaypointArrivalRadius)
            {
                _pathIndex++;
                continue;
            }

            if (isFinal)
            {
                float stopDistance = (_currentSpeed * _currentSpeed) / (2f * Mathf.Max(_deceleration, 0.01f));
                bool shouldBrake = distance <= Mathf.Max(WaypointArrivalRadius, stopDistance);

                if (shouldBrake && _currentSpeed <= SpeedStopEpsilon && distance <= WaypointArrivalRadius)
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
                return dir;
            }

            Vector3 midDir = toTarget / Mathf.Max(distance, 1e-4f);
            RotateToward(midDir, dt);
            return midDir;
        }

        return Vector3.zero;
    }

    Vector3 ComputeActionDesiredVelocity(float dt)
    {
        float turn = 0f;
        if ((_flags & MovementFlags.TurnLeft) != 0)
            turn -= 1f;
        if ((_flags & MovementFlags.TurnRight) != 0)
            turn += 1f;
        if (turn != 0f)
            transform.Rotate(0f, turn * _turnRateDegrees * dt, 0f);

        Vector3 planar = Vector3.zero;
        if ((_flags & MovementFlags.Forward) != 0)
            planar += transform.forward * _runLimits.Forward;
        if ((_flags & MovementFlags.Backward) != 0)
            planar -= transform.forward * _runLimits.Backward;
        if ((_flags & MovementFlags.StrafeRight) != 0)
            planar += transform.right * _runLimits.Strafe;
        if ((_flags & MovementFlags.StrafeLeft) != 0)
            planar -= transform.right * _runLimits.Strafe;

        return planar;
    }

    void RotateToward(Vector3 direction, float dt)
    {
        if (direction.sqrMagnitude < 1e-6f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            PathTurnRateDegrees * dt);
    }

    void StopAllFlags()
    {
        _flags = MovementFlags.None;
    }

    void EnterMovementState(MovementState state)
    {
        if (_state == MovementState.Walk || _state == MovementState.Run)
            _lastSpeedMode = _state;

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
}
