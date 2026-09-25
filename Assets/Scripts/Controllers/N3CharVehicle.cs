using System;
using System.Collections.Generic;
using LostEden.Vehicles;
using N3Lite;
using N3Lite.AORules;
using N3Lite.Surfaces;
using SmokeLounge.AOtomation.Messaging.GameData;
using UnityEngine;
using CharCore = N3Lite.N3CharVehicle;
using MovementAction = AOSharp.Common.GameData.MovementAction;
using MovementState = AOSharp.Common.GameData.MovementState;

/// <summary>
/// Binds an N3Lite <see cref="CharCore"/> — the character's movement — to a transform, and translates
/// the network's movement messages into it. The camera's counterpart is <see cref="N3Camera"/>.
/// See <c>N3Lite/docs/Movement.md</c> §4 and §7.
///
/// <para>
/// What lives here and not in N3Lite: the transform sync, the protocol mapping
/// (<see cref="MovementAction"/>, <see cref="CharMovementStatus"/>, <see cref="MovementState"/>), the
/// player-side path follower, and the animation queries. The movement state and stats — turned into a
/// <see cref="MovementProfile"/> by <see cref="CharMovementRules"/> — are <see cref="CharMovement"/>,
/// which the server shares. <b>There is no <c>CharacterController</c>
/// and no collider.</b>
/// </para>
///
/// <para>
/// <b>The animation-clip naming below is NOT ported</b> and is carried over from the deleted file only
/// so the game keeps working. It is marked and needs its own reversing pass — see
/// <c>N3Lite/docs/Movement.md</c> §9.
/// </para>
/// </summary>
public class N3CharVehicle : MonoBehaviour
{
    const string MovementConfigResourcePath = "MovementConfig";

    // CharMovementStatus field encodings, unchanged from the network layer.
    const byte AxisMoving = 2;
    const byte StrafeActive = 2;
    const byte TurnActive = 4;
    const byte JumpActive = 3;
    const byte DirForward = 1;
    const byte DirReverse = 2;
    const byte DirLeft = 3;
    const byte DirRight = 4;

    [SerializeField] MovementConfig _movementConfig;

    [Header("Body (the player factory's constants, 10057826)")]
    [SerializeField] float _mass = CharCore.DefaultMass;
    [SerializeField] float _radius = CharCore.DefaultRadius;

    /// <summary>
    /// The transform the surface's coordinates are expressed in — the terrain root. The rendered
    /// terrain bakes its offsets into the mesh vertices relative to this. Null means the scene root.
    /// </summary>
    [SerializeField] Transform _surfaceRoot;

    CharMovement _movement;
    CharCore _core;

    readonly List<Vector3> _path = new();
    int _pathIndex = -1;

    public event Action JumpStarted;
    public event Action JumpLanded;

    /// <summary>The movement core this component drives.</summary>
    public CharCore Core => _core;

    /// <summary>The movement state and stats around the core — the part the server shares.</summary>
    public CharMovement Movement => _movement;

    /// <summary>The vehicle itself.</summary>
    public CharVehicleSim Vehicle => _core?.Vehicle;

    public bool HasSurface => _core != null && _core.Surface != null;

    bool HasPath => _pathIndex >= 0 && _pathIndex < _path.Count;

    public MovementConfig Config
    {
        get
        {
            if (_movementConfig == null)
                _movementConfig = Resources.Load<MovementConfig>(MovementConfigResourcePath);
            return _movementConfig;
        }
    }

    public MovementFlags MovementFlags => _core.Flags;

    public MovementState State => _movement != null ? (MovementState)_movement.State : MovementState.Run;

    public float CurrentSpeed => _core != null ? _core.CurrentSpeed : 0f;

    public float DesiredSpeed => _core != null ? _core.MaxVel : 0f;

    public float MaxForce => _core != null ? _core.MaxForce : 0f;

    /// <summary>
    /// The active speed cap. <see cref="N3Camera"/> reads this for its catch-up constraint
    /// (<c>N3Lite/docs/Camera.md</c> §5.3), so it must be the vehicle's real <c>MaxVel</c>.
    /// </summary>
    public float LocomotionMaxSpeed => _core != null ? _core.MaxVel : 0f;

    public bool IsMoving => CurrentSpeed > 0.001f || _core.IsTranslating || HasPath;

    bool IsTranslating => _core.IsTranslating;

    // ---- lifecycle -------------------------------------------------------

    void Awake()
    {
        if (_core != null)
            return;

        _movement = new CharMovement(isNpc: false, _mass, _radius);
        _core = _movement.Core;

        // UNPORTED: the keyboard turn rates are the previous developer's, not stock. Stock scales a
        // +/-0.02 rad rate by MouseTurnSensitivity (100229ff).
        MovementConfig config = Config;
        if (config != null)
        {
            _core.TurnRateMoving = config.TurnRateRadiansMoving;
            _core.TurnRateStopped = config.TurnRateRadiansStopped;
        }

        _core.JumpStarted += () => JumpStarted?.Invoke();
        _core.JumpLanded += () => JumpLanded?.Invoke();
        _core.PathCleared += ClearPlayerPath;

        PushTransformToSim();
    }

    /// <summary>
    /// Which of <c>CharVehicle_t</c>'s two subclasses this body is. See N3Lite/docs/Movement.md §3.2.
    /// </summary>
    public bool IsNpcVehicle => _core.IsNpcVehicle;

    /// <summary>The NPC vehicle, or null when this body is a player's.</summary>
    public NpcVehicleSim Npc => _core?.Npc;

    /// <summary>
    /// Pick the subclass. Called from <c>Character.Apply</c> once the spawn message's
    /// <c>IsNpc</c> flag is known — <c>Awake</c> runs before that, so the default is the player's and
    /// this rebuilds if the flag says otherwise. <c>Apply</c> warps the body immediately afterwards.
    /// </summary>
    public void SelectVehicleKind(bool isNpc)
    {
        if (_core == null)
            Awake();

        _movement.SelectVehicleKind(isNpc);
    }

    /// <summary>
    /// Supply the world to collide against. Until this is called the vehicle free-falls, which is
    /// stock's behaviour when <c>GetSurface</c> returns null.
    /// </summary>
    public void SetSurface(ISurface surface, Transform surfaceRoot = null)
    {
        if (_core == null)
            Awake();

        _core.Surface = surface;
        if (surfaceRoot != null)
            _surfaceRoot = surfaceRoot;
    }

    // ---- the step, driven by VehicleSystem ------------------------------

    /// <summary>This body's slot in <see cref="VehicleSystem"/>, or -1 when not registered.</summary>
    internal int SystemIndex = -1;

    void OnEnable() => VehicleSystem.Register(this);

    void OnDisable() => VehicleSystem.Unregister(this);

    /// <summary>Pass 1, main thread: the transform into the sim, and the path glue that turns it.</summary>
    internal void BeginStep(float dt)
    {
        if (_core == null)
            return;

        PushTransformToSim();

        // The carried-over follower. Only a player's body has one (an NPC's path is the sim's own),
        // so this and the NPC guide never both act on the same body.
        if (HasPath)
            SteerAlongPath(dt);
    }

    /// <summary>Pass 2: the sim alone.</summary>
    internal void RunStep(float dt)
    {
        if (_core == null)
            return;

        _core.Tick(dt);
    }

    /// <summary>Pass 3, main thread: the result back onto the transform.</summary>
    internal void EndStep()
    {
        if (_core == null)
            return;

        PullSimToTransform();
    }

    void PushTransformToSim()
    {
        _core.Position = ToSurfaceSpace(transform.position).ToVec3();

        // Only the heading goes back in. The vehicle's own body rotation carries the surface tilt
        // (orientation mode 1), and re-reading a level transform into it each frame would throw that
        // tilt away every step.
        if (!_headingOnly)
            _core.Vehicle.BodyRotation = transform.rotation.ToQuat();
    }

    void PullSimToTransform()
    {
        // The vehicle's body rotation is TILTED to the ground in orientation mode 1 -- that is stock
        // (GetBodyRot returns +0x80, which FUN_1000c616 builds with LookRotation(forward, normal)).
        // But the visible character does NOT bank: in the real client a character stays upright on
        // any slope, because the dynel's own rotation is not the vehicle's. So the transform gets the
        // HEADING only, and the tilt stays inside the sim where the steering uses it.
        Vec3 forward = _core.Vehicle.GetBodyForward();
        Quaternion rotation = transform.rotation;
        if (Mathf.Abs(forward.X) > 1e-5f || Mathf.Abs(forward.Z) > 1e-5f)
            rotation = Quaternion.LookRotation(new Vector3(forward.X, 0f, forward.Z).normalized, Vector3.up);

        transform.SetPositionAndRotation(FromSurfaceSpace(_core.Position.ToUnity()), rotation);
    }

    /// <summary>
    /// True once the sim owns its own body rotation, so the level transform must not be pushed back
    /// into it. Orientation mode 1 tilts the body, and the transform is deliberately kept upright.
    /// </summary>
    bool _headingOnly = true;

    Vector3 ToSurfaceSpace(Vector3 world)
        => _surfaceRoot != null ? _surfaceRoot.InverseTransformPoint(world) : world;

    Vector3 FromSurfaceSpace(Vector3 local)
        => _surfaceRoot != null ? _surfaceRoot.TransformPoint(local) : local;

    // ---- input -----------------------------------------------------------

    /// <summary>
    /// The input layer's flags, into the core's four axes.
    ///
    /// <para>
    /// The camera yaw is <b>not</b> applied to the body: stock never snaps a character's heading to
    /// the camera, it applies a delta through <c>VehicleForwardUpdate</c>
    /// (<c>N3Lite/docs/Camera.md</c> §5.9). <see cref="N3Camera"/> does that via
    /// <see cref="ApplyYawDelta"/>.
    /// </para>
    /// </summary>
    public void SetInputs(MovementFlags flags, Quaternion cameraYaw)
    {
        // Nothing should be feeding player input to an NPC body, which has no input axes; say so
        // rather than writing values that do nothing.
        if (!_core.SetInputs(flags))
            Debug.LogWarning($"[Vehicle] SetInputs on the NPC vehicle of '{name}' -- ignored.", this);
    }

    /// <summary>
    /// Turn the character by a yaw delta — the camera's right-drag in Lock mode. A delta applied to
    /// the body's facing, never an absolute heading, and through <c>SetRelRot</c> so a running
    /// body's velocity turns with it.
    /// </summary>
    public void ApplyYawDelta(float degrees)
    {
        if (State == MovementState.Sit || Mathf.Approximately(degrees, 0f))
            return;

        transform.Rotate(0f, degrees, 0f);
        _core.SetRelRot(Quat.LookRotation(transform.forward.ToVec3(), _core.Vehicle.SurfaceNormal));
    }

    public void ApplyAction(MovementAction action)
    {
        _core.ClearPath();

        MovementFlags flags = _core.Flags;
        switch (action)
        {
            case MovementAction.ForwardStart: _core.SetFlags(flags | MovementFlags.Forward); break;
            case MovementAction.ForwardStop: _core.SetFlags(flags & ~MovementFlags.Forward); break;
            case MovementAction.BackwardStart: _core.SetFlags(flags | MovementFlags.Backward); break;
            case MovementAction.BackwardStop: _core.SetFlags(flags & ~MovementFlags.Backward); break;
            case MovementAction.StrafeLeftStart: _core.SetFlags(flags | MovementFlags.StrafeLeft); break;
            case MovementAction.StrafeLeftStop: _core.SetFlags(flags & ~MovementFlags.StrafeLeft); break;
            case MovementAction.StrafeRightStart: _core.SetFlags(flags | MovementFlags.StrafeRight); break;
            case MovementAction.StrafeRightStop: _core.SetFlags(flags & ~MovementFlags.StrafeRight); break;
            case MovementAction.TurnLeftStart: _core.SetFlags(flags | MovementFlags.TurnLeft); break;
            case MovementAction.TurnLeftStop: _core.SetFlags(flags & ~MovementFlags.TurnLeft); break;
            case MovementAction.TurnRightStart: _core.SetFlags(flags | MovementFlags.TurnRight); break;
            case MovementAction.TurnRightStop: _core.SetFlags(flags & ~MovementFlags.TurnRight); break;
            case MovementAction.JumpStart:
                _core.SetFlags(flags | MovementFlags.Jump);
                _core.TryStartJump();
                break;
            case MovementAction.JumpStop: _core.SetFlags(flags & ~MovementFlags.Jump); break;
            case MovementAction.FullStop: _core.SetFlags(MovementFlags.None); break;
            case MovementAction.SwitchToFrozen: EnterMovementState(MovementState.Rooted); break;
            case MovementAction.SwitchToWalk: EnterMovementState(MovementState.Walk); break;
            case MovementAction.SwitchToRun: EnterMovementState(MovementState.Run); break;
            case MovementAction.SwitchToSwim: EnterMovementState(MovementState.Swim); break;
            case MovementAction.SwitchToCrawl: EnterMovementState(MovementState.Crawl); break;
            case MovementAction.SwitchToSneak: EnterMovementState(MovementState.Sneak); break;
            case MovementAction.SwitchToFly: EnterMovementState(MovementState.Fly); break;
            case MovementAction.SwitchToSit: EnterMovementState(MovementState.Sit); break;
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

    public void ApplyMovementStatus(CharMovementStatus status)
    {
        _core.ClearPath();
        EnterMovementState(ToMovementState(status.ModeId));
        _movement.LastSpeedMode = (int)ToMovementState(status.LastSpeedMode);

        MovementFlags flags = MovementFlags.None;

        if (status.FwdState == AxisMoving)
        {
            if (status.FwdDir == DirForward) flags |= MovementFlags.Forward;
            else if (status.FwdDir == DirReverse) flags |= MovementFlags.Backward;
        }

        if (status.StrafeState == StrafeActive)
        {
            if (status.StrafeDir == DirLeft) flags |= MovementFlags.StrafeLeft;
            else if (status.StrafeDir == DirRight) flags |= MovementFlags.StrafeRight;
        }

        if (status.TurnState == TurnActive)
        {
            if (status.TurnDir == DirLeft) flags |= MovementFlags.TurnLeft;
            else if (status.TurnDir == DirRight) flags |= MovementFlags.TurnRight;
        }

        if (status.JumpState == JumpActive)
            flags |= MovementFlags.Jump;

        _core.SetFlags(flags);
    }

    // ---- movement state --------------------------------------------------

    void EnterMovementState(MovementState state) => _movement.EnterState((int)state);

    void LeaveMovementState() => _movement.LeaveState();

    static MovementState ToMovementState(uint modeId)
        => Enum.IsDefined(typeof(MovementState), (int)modeId) ? (MovementState)modeId : MovementState.Run;

    /// <summary>The owner's movement stats. See <see cref="CharMovement.SetStats"/>.</summary>
    public void SetMovementStats(MovementStats stats)
    {
        if (_core == null)
            Awake();

        _movement.SetStats(stats);
    }

    public void Halt()
    {
        _core?.Halt();
    }

    /// <summary>
    /// Place the vehicle without steering — stock's <c>SetRelPos</c> path, which runs collision but
    /// bypasses the integrator.
    /// </summary>
    public void Warp(Vector3 position, Quaternion rotation, bool resetVelocity = true)
    {
        if (_core == null)
            Awake();

        transform.SetPositionAndRotation(position, rotation);

        // SetRelRot so a warp that keeps its velocity carries it into the new facing instead of
        // holding the old world-space direction. Stock's teleport is SetRelPosRot (1000e2af), which
        // is UNREAD — this is SetRelRot's verified behaviour applied to the rotation half.
        _core.Warp(ToSurfaceSpace(position).ToVec3(), (rotation * Vector3.forward).ToVec3(), resetVelocity);
    }

    /// <summary>
    /// Was a hook for the old collider-streaming wait. With collision coming from the heightmap there
    /// is nothing to stream, so this only re-seats the vehicle at the spawn point.
    /// </summary>
    public void RequestSurfacePriorityForSpawn(Vector3 worldPosition)
    {
        if (_core == null)
            Awake();

        transform.position = worldPosition;
        PushTransformToSim();
    }

    // ---- paths -----------------------------------------------------------

    /// <summary>
    /// The server's waypoint list, from <c>FollowTargetMessage.PathInfo</c>.
    ///
    /// <para>
    /// On an NPC body the core follows it with the reversed <c>Path_t</c> and its guide — stock's
    /// model. On a player's body it falls back to the carried-over follower below, which is the
    /// previous developer's and drives the input axes instead.
    /// </para>
    /// </summary>
    public void SetPath(IReadOnlyList<Vector3> waypoints)
    {
        var surfaceWaypoints = new List<Vec3>(waypoints?.Count ?? 0);
        if (waypoints != null)
            for (int i = 0; i < waypoints.Count; i++)
                surfaceWaypoints.Add(ToSurfaceSpace(waypoints[i]).ToVec3());

        if (_core.SetPath(surfaceWaypoints))
            return;

        if (waypoints == null || waypoints.Count == 0)
            return;

        for (int i = 0; i < waypoints.Count; i++)
            _path.Add(waypoints[i]);

        _pathIndex = 0;
    }

    public void ClearPath() => _core.ClearPath();

    /// <summary>The player-side path, cleared whenever the core clears its own.</summary>
    void ClearPlayerPath()
    {
        _path.Clear();
        _pathIndex = -1;
    }

    /// <summary>
    /// UNPORTED. Follows a waypoint list by pointing the body at the next one and driving forward.
    /// Stock's equivalent is the follow-target branch of the longitudinal channel
    /// (<c>SteeringDirArrive</c> at <c>10071537</c>), which is not ported.
    /// </summary>
    void SteerAlongPath(float dt)
    {
        CharVehicleSim sim = _core.Vehicle;
        float arrival = Config != null ? Config.WaypointArrivalRadius : 0.5f;

        while (HasPath)
        {
            Vector3 target = _path[_pathIndex];
            Vector3 toTarget = target - transform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;

            if (distance <= arrival)
            {
                _pathIndex++;
                if (!HasPath)
                {
                    sim.SetForwardDrive(0f);
                    sim.SetTurnRate(0f);
                    sim.Halt();
                    return;
                }
                continue;
            }

            float turnRate = Config != null ? Config.PathTurnRateDegrees : 500f;
            Quaternion look = Quaternion.LookRotation(toTarget / distance, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnRate * dt);

            // SetRelRot, not a raw body rotation: this turns the body while it is *running* (the
            // drive goes to 1 on the next line), and mode 1 would otherwise rebuild the rotation from
            // the unchanged velocity and undo the turn. See ApplyYawDelta.
            sim.SetRelRot(transform.rotation.ToQuat());
            sim.SetForwardDrive(1f);
            return;
        }
    }

    // =====================================================================
    // UNPORTED GLUE — animation selection carried over from the deleted
    // CharacterMotor so the game keeps working. None of this is
    // reverse-engineered. N3Lite/docs/Movement.md §8.
    // =====================================================================

    /// <summary>UNPORTED. Animation clip selection — not a vehicle concern in stock.</summary>
    public bool SuppressLocomotionPlay
    {
        get
        {
            int mode = (int)State;
            return mode == 8 || mode == 9;
        }
    }

    /// <summary>UNPORTED. Animation clip selection.</summary>
    public int GetJumpTakeoffKind()
        => IsTranslating || HasPath || CurrentSpeed > 0.05f
            ? AnimKindIds.JumpForward
            : AnimKindIds.JumpStand;

    /// <summary>UNPORTED. Animation clip selection.</summary>
    public int GetJumpLandKind()
    {
        bool forward = HasPath || (MovementFlags & MovementFlags.Forward) != 0;
        if (!forward)
            return 0;

        return State == MovementState.Walk ? AnimKindIds.JumpLandWalk : AnimKindIds.JumpLandRun;
    }

    /// <summary>UNPORTED. Animation clip selection.</summary>
    public bool TryGetLocomotionKind(AnimHolder holder, out int kind)
    {
        kind = 0;
        MovementState state = State;
        MovementFlags flags = MovementFlags;
        if (holder == null || state == MovementState.Sit)
            return false;

        int mode = (int)state;
        if (mode == 8 || mode == 9)
            return false;

        bool translating = IsTranslating || HasPath;

        switch (state)
        {
            case MovementState.Swim:
                kind = translating ? AnimKindIds.Swim : AnimKindIds.IdleSwim;
                return true;
            case MovementState.Crawl:
                kind = translating ? AnimKindIds.Crawl : holder.CrawlIdle;
                return true;
            case MovementState.Sneak:
                kind = holder.Sneak;
                return true;
            case MovementState.Fly:
                kind = holder.Hover;
                return true;
        }

        if (!translating)
        {
            if ((flags & MovementFlags.TurnLeft) != 0 && state == MovementState.Walk)
            {
                kind = AnimKindIds.TurnLeft;
                return true;
            }

            if ((flags & MovementFlags.TurnRight) != 0 && state == MovementState.Walk)
            {
                kind = AnimKindIds.TurnRight;
                return true;
            }

            kind = holder.Idle;
            return kind != 0;
        }

        if (HasPath || (flags & MovementFlags.Forward) != 0)
        {
            kind = state == MovementState.Walk ? holder.WalkForward : holder.RunForward;
            return kind != 0;
        }

        if ((flags & MovementFlags.Backward) != 0)
        {
            kind = state == MovementState.Walk ? AnimKindIds.WalkBack : AnimKindIds.RunBack;
            return true;
        }

        if ((flags & MovementFlags.StrafeLeft) != 0)
        {
            kind = AnimKindIds.WalkLeft;
            return true;
        }

        if ((flags & MovementFlags.StrafeRight) != 0)
        {
            kind = AnimKindIds.WalkRight;
            return true;
        }

        kind = state == MovementState.Walk ? holder.WalkForward : holder.RunForward;
        return kind != 0;
    }

    /// <summary>UNPORTED. Animation playback-rate scaling.</summary>
    public float GetLocomotionBaseVelocity()
    {
        MovementConfig config = Config;
        float walkBase = config != null ? config.WalkBaseVelocity : 1.5f;
        float runForwardBase = config != null ? config.RunForwardBase : 5f;
        float runBackwardBase = config != null ? config.RunBackwardBase : 3f;
        float runStrafeBase = config != null ? config.RunStrafeBase : 2.5f;

        MovementFlags flags = MovementFlags;
        if (State == MovementState.Walk)
            return walkBase;

        if (HasPath || (flags & MovementFlags.Forward) != 0)
            return runForwardBase;

        if ((flags & MovementFlags.Backward) != 0)
            return runBackwardBase;

        if ((flags & (MovementFlags.StrafeLeft | MovementFlags.StrafeRight)) != 0)
            return runStrafeBase;

        return runForwardBase;
    }

    /// <summary>UNPORTED. Animation playback-rate scaling.</summary>
    public float ComputeLocomotionPlaybackRate(
        float desiredSpeed, float baseVelocity, int animSpeedStat, float calibration = 1f)
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

    void OnDrawGizmos()
    {
        DrawNpcPathGizmo();

        if (!HasPath || _path.Count == 0)
            return;

        Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.95f);
        Gizmos.DrawLine(transform.position, _path[_pathIndex]);
        for (int i = _pathIndex + 1; i < _path.Count; i++)
            Gizmos.DrawLine(_path[i - 1], _path[i]);
    }

    /// <summary>
    /// The reversed NPC path: the waypoint list from the server, split at the distance the
    /// <c>PathGuide_t</c> has consumed, plus the guide point itself — which is what the longitudinal
    /// channel actually steers at, so it is the thing worth seeing when an NPC misbehaves.
    /// See N3Lite/docs/Movement.md §3.2.
    /// </summary>
    void DrawNpcPathGizmo()
    {
        NpcVehicleSim npc = Npc;
        if (npc == null || npc.Path.Size == 0)
            return;

        var walked = new Color(0.45f, 0.30f, 0.10f, 0.7f);
        var ahead = new Color(1f, 0.62f, 0.10f, 0.95f);

        // How far along the guide has travelled; the polyline is drawn dim behind it and bright ahead.
        float consumed = npc.Guide.Time * npc.Guide.MaxSpeed;
        float walkedSoFar = 0f;

        Vector3 Point(int i) => FromSurfaceSpace(npc.Path.GetWaypoint(i).ToUnity());

        for (int i = 0; i < npc.Path.Size; i++)
        {
            Gizmos.color = walkedSoFar < consumed ? walked : ahead;
            Gizmos.DrawWireCube(Point(i), Vector3.one * 0.25f);

            if (i == 0)
                continue;

            float segment = npc.Path.GetSegLen(i - 1);
            Gizmos.color = walkedSoFar + segment <= consumed ? walked : ahead;
            Gizmos.DrawLine(Point(i - 1), Point(i));
            walkedSoFar += segment;
        }

        // The guide, and the line the body is steering along to reach it.
        Vector3 guide = FromSurfaceSpace(npc.Guide.GuidePos.ToUnity());
        Gizmos.color = new Color(0.25f, 1f, 0.45f, 1f);
        Gizmos.DrawSphere(guide, 0.22f);
        Gizmos.DrawLine(transform.position, guide);
    }
}
