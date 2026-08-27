using System.Collections.Generic;
using UnityEngine;
using MovementAction = AOSharp.Common.GameData.MovementAction;

[RequireComponent(typeof(CharacterController))]
public class CharacterMotor : MonoBehaviour
{
    const float DefaultWalkSpeed = 1.5f;
    const float DefaultTurnRateDegrees = 90f;
    const float DefaultAcceleration = 10f;
    const float DefaultDeceleration = 14f;
    const float Gravity = -20f;
    const float GroundStickVelocity = -2f;
    const float WaypointArrivalRadius = 0.5f;
    const float SpeedStopEpsilon = 0.05f;

    [SerializeField] float _walkSpeed = DefaultWalkSpeed;
    [SerializeField] float _turnRateDegrees = DefaultTurnRateDegrees;
    [SerializeField] float _acceleration = DefaultAcceleration;
    [SerializeField] float _deceleration = DefaultDeceleration;

    CharacterController _controller;
    float _verticalVelocity;
    float _currentSpeed;
    Vector3 _moveDirection = Vector3.forward;

    bool _forward;
    bool _backward;
    bool _strafeLeft;
    bool _strafeRight;
    bool _turnLeft;
    bool _turnRight;

    readonly List<Vector3> _path = new();
    int _pathIndex = -1;

    bool HasPath => _pathIndex >= 0 && _pathIndex < _path.Count;

    public float CurrentSpeed => _currentSpeed;
    public bool IsMoving => _currentSpeed > SpeedStopEpsilon
        || _forward || _backward || _strafeLeft || _strafeRight
        || HasPath;

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

    public void ApplyAction(MovementAction action)
    {
        ClearPath();

        switch (action)
        {
            case MovementAction.ForwardStart:
                _forward = true;
                break;
            case MovementAction.ForwardStop:
                _forward = false;
                break;
            case MovementAction.BackwardStart:
                _backward = true;
                break;
            case MovementAction.BackwardStop:
                _backward = false;
                break;
            case MovementAction.StrafeLeftStart:
                _strafeLeft = true;
                break;
            case MovementAction.StrafeLeftStop:
                _strafeLeft = false;
                break;
            case MovementAction.StrafeRightStart:
                _strafeRight = true;
                break;
            case MovementAction.StrafeRightStop:
                _strafeRight = false;
                break;
            case MovementAction.TurnLeftStart:
                _turnLeft = true;
                break;
            case MovementAction.TurnLeftStop:
                _turnLeft = false;
                break;
            case MovementAction.TurnRightStart:
                _turnRight = true;
                break;
            case MovementAction.TurnRightStop:
                _turnRight = false;
                break;
            case MovementAction.FullStop:
                StopAllFlags();
                break;
        }
    }

    void Update()
    {
        float dt = Time.deltaTime;
        Vector3 desiredDirection = HasPath
            ? ComputePathDesiredDirection(dt)
            : ComputeActionDesiredDirection(dt);

        float desiredSpeed = desiredDirection.sqrMagnitude > 1e-6f ? _walkSpeed : 0f;
        if (desiredDirection.sqrMagnitude > 1e-6f)
            _moveDirection = desiredDirection.normalized;

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

    Vector3 ComputeActionDesiredDirection(float dt)
    {
        float turn = 0f;
        if (_turnLeft)
            turn -= 1f;
        if (_turnRight)
            turn += 1f;
        if (turn != 0f)
            transform.Rotate(0f, turn * _turnRateDegrees * dt, 0f);

        Vector3 move = Vector3.zero;
        if (_forward)
            move += transform.forward;
        if (_backward)
            move -= transform.forward;
        if (_strafeRight)
            move += transform.right;
        if (_strafeLeft)
            move -= transform.right;

        if (move.sqrMagnitude > 1e-6f)
            return move.normalized;

        return Vector3.zero;
    }

    void RotateToward(Vector3 direction, float dt)
    {
        if (direction.sqrMagnitude < 1e-6f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            _turnRateDegrees * dt);
    }

    void StopAllFlags()
    {
        _forward = false;
        _backward = false;
        _strafeLeft = false;
        _strafeRight = false;
        _turnLeft = false;
        _turnRight = false;
    }
}
