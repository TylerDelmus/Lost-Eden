using UnityEngine;

[CreateAssetMenu(fileName = "MovementConfig", menuName = "Lost Eden/Movement Config")]
public sealed class MovementConfig : ScriptableObject
{
    [Header("Remote Visual Smoothing")]
    [Tooltip("Exp decay sharpness for remote mesh position catching up to motor pose.")]
    [SerializeField] float _remoteVisualPositionSharpness = 5f;
    [Tooltip("Exp decay sharpness for remote mesh yaw catching up to motor pose.")]
    [SerializeField] float _remoteVisualYawSharpness = 15f;
    [Tooltip("Planar distance (m) beyond which remote CharDCMove snaps instead of smoothing.")]
    [SerializeField] float _remoteVisualTeleportThreshold = 10f;

    [Header("Locomotion Animation")]
    [SerializeField] float _locomotionAnimBlendSeconds = 0.2f;
    [SerializeField] float _runPlaybackRateMax = 1.3f;
    [SerializeField] float _runPlaybackRateSpeedThreshold = 4f;

    [Header("Turning")]
    [SerializeField] float _turnRateRadiansStopped = 3.5f;
    [SerializeField] float _turnRateRadiansMoving = 1.5f;
    [SerializeField] float _pathTurnRateDegrees = 500f;

    [Header("Physics")]
    [SerializeField] float _mass = 50f;
    [SerializeField] float _forceReachTime = 0.5f;
    [SerializeField] float _gravity = -20f;
    [SerializeField] float _groundStickVelocity = -2f;
    [SerializeField] float _terminalVelocity = 50f;
    [SerializeField] float _speedStopEpsilon = 0.05f;
    [SerializeField] float _waypointArrivalRadius = 0.5f;

    [Header("Jump")]
    [SerializeField] float _jumpStatCap = 800f;
    [SerializeField] float _jumpHeightPerStatPool = 200f;
    [SerializeField] float _jumpHeightBase = 1f;
    [SerializeField] float _jumpHeightFloor = 0.5f;

    [Header("Run Speed (stat scaling)")]
    [SerializeField] float _healthPenaltyThreshold = 0.15f;
    [SerializeField] float _statFactorOffset = 1000f;
    [SerializeField] float _walkBaseVelocity = 1.5f;

    [SerializeField] float _runForwardSlope = 1f / 275f;
    [SerializeField] float _runForwardBase = 5f;
    [SerializeField] float _runForwardMin = 1.5f;
    [SerializeField] float _runForwardMax = 13f;

    [SerializeField] float _runBackwardSlope = 0.0025454545f;
    [SerializeField] float _runBackwardBase = 3f;
    [SerializeField] float _runBackwardMin = 1.05f;
    [SerializeField] float _runBackwardMax = 9.1f;

    [SerializeField] float _runStrafeBase = 2.5f;
    [SerializeField] float _runStrafeSlope = 0.5f / 275f;
    [SerializeField] float _runStrafeMin = 0.75f;
    [SerializeField] float _runStrafeMax = 6.5f;

    public float RemoteVisualPositionSharpness => _remoteVisualPositionSharpness;
    public float RemoteVisualYawSharpness => _remoteVisualYawSharpness;
    public float RemoteVisualTeleportThreshold => _remoteVisualTeleportThreshold;

    public float LocomotionAnimBlendSeconds => _locomotionAnimBlendSeconds;
    public float RunPlaybackRateMax => _runPlaybackRateMax;
    public float RunPlaybackRateSpeedThreshold => _runPlaybackRateSpeedThreshold;

    public float TurnRateRadiansStopped => _turnRateRadiansStopped;
    public float TurnRateRadiansMoving => _turnRateRadiansMoving;
    public float PathTurnRateDegrees => _pathTurnRateDegrees;

    public float Mass => _mass;
    public float ForceReachTime => _forceReachTime;
    public float Gravity => _gravity;
    public float GroundStickVelocity => _groundStickVelocity;
    public float TerminalVelocity => _terminalVelocity;
    public float SpeedStopEpsilon => _speedStopEpsilon;
    public float WaypointArrivalRadius => _waypointArrivalRadius;

    public float JumpStatCap => _jumpStatCap;
    public float JumpHeightPerStatPool => _jumpHeightPerStatPool;
    public float JumpHeightBase => _jumpHeightBase;
    public float JumpHeightFloor => _jumpHeightFloor;

    public float HealthPenaltyThreshold => _healthPenaltyThreshold;
    public float StatFactorOffset => _statFactorOffset;
    public float WalkBaseVelocity => _walkBaseVelocity;

    public float RunForwardSlope => _runForwardSlope;
    public float RunForwardBase => _runForwardBase;
    public float RunForwardMin => _runForwardMin;
    public float RunForwardMax => _runForwardMax;

    public float RunBackwardSlope => _runBackwardSlope;
    public float RunBackwardBase => _runBackwardBase;
    public float RunBackwardMin => _runBackwardMin;
    public float RunBackwardMax => _runBackwardMax;

    public float RunStrafeBase => _runStrafeBase;
    public float RunStrafeSlope => _runStrafeSlope;
    public float RunStrafeMin => _runStrafeMin;
    public float RunStrafeMax => _runStrafeMax;
}
