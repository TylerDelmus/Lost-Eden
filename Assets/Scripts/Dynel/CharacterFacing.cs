using UnityEngine;
using MovementState = AOSharp.Common.GameData.MovementState;

/// <summary>
/// Rotates the character's visual mesh toward its direction of travel without touching the
/// authoritative transform heading.
///
/// Why the transform is off limits: <see cref="PlayerController"/> sends
/// <c>transform.eulerAngles.y</c> to the server as CharDCMove.Heading, and
/// <see cref="CharacterMotor.ComputeActionPlanarVelocity"/> builds desired velocity from
/// <c>transform.forward</c> / <c>transform.right</c>. Rotating the transform would both desync
/// combat facing and feed back into the movement basis (turn -> new forward -> turn further).
///
/// The yaw is applied to <see cref="VisualDynel.FacingRoot"/>, a persistent pivot that sits
/// between the dynel transform and the CatMesh visual root. That keeps it clear of the remote
/// render-offset smoothing, which owns the visual root's own local rotation, and it survives
/// appearance rebuilds that destroy and recreate the visual.
///
/// The clamped target is published back to the motor as
/// <see cref="CharacterMotor.VisualYawOffset"/>, so locomotion clip selection consumes the
/// residual the mesh could not absorb: fully rotated plays the forward clip, clamped plays
/// the strafe or backpedal clip.
///
/// Runs on remote characters too — their flags arrive via ApplyMovementStatus / ApplyAction.
/// </summary>
[RequireComponent(typeof(CharacterMotor))]
[RequireComponent(typeof(VisualDynel))]
[DefaultExecutionOrder(-50)]
public sealed class CharacterFacing : MonoBehaviour
{
    [Header("Enable")]
    [Tooltip("Off reproduces the original behaviour exactly: mesh square to the transform, "
        + "clip picked from raw movement flags.")]
    [SerializeField] bool _faceTravelDirection = true;

    [Header("Yaw Limits (degrees)")]
    [Tooltip("Out of combat. 179 = fully face the way you travel; backpedal becomes turn-and-run.")]
    [SerializeField, Range(0f, 179f)] float _freeMaxYaw = 179f;

    [Tooltip("While fighting a target. Small — the strafe and backpedal clips carry the rest, "
        + "so AO's must-face-target combat still reads correctly.")]
    [SerializeField, Range(0f, 179f)] float _combatMaxYaw = 25f;

    [Header("Smoothing (seconds to settle)")]
    [Tooltip("Slower out of combat so a 180 backpedal sweeps instead of snapping.")]
    [SerializeField] float _freeSmoothTime = 0.18f;
    [SerializeField] float _combatSmoothTime = 0.10f;

    CharacterMotor _motor;
    VisualDynel _visual;
    Character _character;
    Transform _pivot;

    float _yaw;
    float _yawVelocity;

    /// <summary>Currently applied cosmetic yaw, in character space.</summary>
    public float Yaw => _yaw;

    public bool FaceTravelDirection
    {
        get => _faceTravelDirection;
        set => _faceTravelDirection = value;
    }

    void Awake()
    {
        _motor = GetComponent<CharacterMotor>();
        _visual = GetComponent<VisualDynel>();
        _character = GetComponent<Character>();

        // Created once, up front — the pivot must outlive visual rebuilds.
        _pivot = _visual.FacingRoot;
    }

    void OnDisable()
    {
        _yaw = 0f;
        _yawVelocity = 0f;

        if (_motor != null)
            _motor.VisualYawOffset = 0f;

        if (_pivot != null)
            _pivot.localRotation = Quaternion.identity;
    }

    void Update()
    {
        if (_pivot == null || _motor == null)
            return;

        bool inCombat = _character != null && _character.FightingTarget != null;
        float maxYaw = inCombat ? _combatMaxYaw : _freeMaxYaw;
        float smoothTime = inCombat ? _combatSmoothTime : _freeSmoothTime;

        float desired = _faceTravelDirection ? ComputeDesiredYaw() : 0f;
        float target = Mathf.Clamp(desired, -maxYaw, maxYaw);

        _yaw = Mathf.SmoothDampAngle(_yaw, target, ref _yawVelocity, smoothTime);
        _pivot.localRotation = Quaternion.Euler(0f, _yaw, 0f);

        // Publish the settled target, not the in-flight yaw: clip choice should be decided once
        // rather than flickering through run -> strafe -> back while the mesh sweeps around.
        _motor.VisualYawOffset = target;
    }

    /// <summary>
    /// Planar movement intent in character space, taken from movement flags rather than measured
    /// velocity — flags are free of slope, collision and acceleration noise.
    ///
    /// TurnLeft / TurnRight are excluded on purpose: those rotate the transform itself, so they
    /// are already reflected in the basis this angle is measured against.
    /// </summary>
    float ComputeDesiredYaw()
    {
        if (_motor.State == MovementState.Sit)
            return 0f;

        // Server pathing already rotates the transform toward each waypoint (RotateToward).
        if (_motor.IsFollowingPath)
            return 0f;

        MovementFlags flags = _motor.MovementFlags;

        float x = 0f;
        float z = 0f;
        if ((flags & MovementFlags.Forward) != 0)
            z += 1f;
        if ((flags & MovementFlags.Backward) != 0)
            z -= 1f;
        if ((flags & MovementFlags.StrafeRight) != 0)
            x += 1f;
        if ((flags & MovementFlags.StrafeLeft) != 0)
            x -= 1f;

        if (x == 0f && z == 0f)
            return 0f;

        // Clamped off exact 180 so SmoothDampAngle resolves a deterministic sweep direction.
        return Mathf.Clamp(Mathf.Atan2(x, z) * Mathf.Rad2Deg, -179f, 179f);
    }
}