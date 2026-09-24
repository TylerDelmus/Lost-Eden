using System;
using System.Collections.Generic;
using LostEden.Vehicles;
using LostEden.Vehicles.Surfaces;
using SmokeLounge.AOtomation.Messaging.GameData;
using UnityEngine;
using MovementAction = AOSharp.Common.GameData.MovementAction;
using MovementState = AOSharp.Common.GameData.MovementState;

/// <summary>
/// Binds a <see cref="CharVehicleSim"/> to a transform — the character's counterpart to
/// <see cref="N3Camera"/>. In stock both the camera and every character are the same kind of object
/// under <c>DummyVehicle_t</c>, so both sit on the same integrator.
/// See <c>Docs/Movement.md</c> §4 and §7.
///
/// <para>
/// This <b>replaces</b> <c>CharacterMotor</c>, which was the previous developer's invention. All
/// motion now comes from the reversed vehicle: four input axes, the per-state speed curve, and
/// ground contact through <c>Vehicle_t::EnsureSurfaceAlignment</c> against an <see cref="ISurface"/>.
/// <b>There is no <c>CharacterController</c> and no collider.</b>
/// </para>
///
/// <para>
/// <b>Two regions below are NOT ported</b> and are carried over from the deleted file only so the
/// game keeps working: the animation-clip naming and the jump impulse. Both are marked, and both
/// need their own reversing pass — see <c>Docs/Movement.md</c> §9.
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
    [SerializeField] float _mass = 50f;
    [SerializeField] float _radius = 0.5f;

    /// <summary>
    /// The transform the surface's coordinates are expressed in — the terrain root. The rendered
    /// terrain bakes its offsets into the mesh vertices relative to this. Null means the scene root.
    /// </summary>
    [SerializeField] Transform _surfaceRoot;

    CharVehicleSim _sim;
    MovementFlags _flags;
    MovementState _state = MovementState.Run;
    MovementState _lastSpeedMode = MovementState.Run;
    VelocityLimits _runLimits;
    int _jumpStrength;
    int _jumpAgility;
    int _jumpGmLevel;
    bool _jumpArmed = true;

    readonly List<Vector3> _path = new();
    int _pathIndex = -1;

    public event Action JumpStarted;
    public event Action JumpLanded;

    /// <summary>The vehicle itself.</summary>
    public CharVehicleSim Vehicle => _sim;

    public bool HasSurface => _sim != null && _sim.Surface != null;

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

    public MovementFlags MovementFlags => _flags;

    public MovementState State => _state;

    public float CurrentSpeed => _sim != null ? _sim.Speed : 0f;

    public float DesiredSpeed => _sim != null ? _sim.MaxVel : 0f;

    public float MaxForce => _sim != null ? _sim.MaxForce : 0f;

    /// <summary>
    /// The active speed cap. <see cref="N3Camera"/> reads this for its catch-up constraint
    /// (<c>Docs/Camera.md</c> §5.3), so it must be the vehicle's real <c>MaxVel</c>.
    /// </summary>
    public float LocomotionMaxSpeed => _sim != null ? _sim.MaxVel : 0f;

    public bool IsMoving => CurrentSpeed > 0.001f || (_flags & TranslationFlags) != 0 || HasPath;

    bool IsTranslating => (_flags & TranslationFlags) != 0;

    // ---- lifecycle -------------------------------------------------------

    void Awake()
    {
        if (_sim == null)
            BuildSim(_isNpcVehicle);
    }

    /// <summary>
    /// Which of `CharVehicle_t`'s two subclasses this body is. False is `PlayerVehicle_t` (the four
    /// input axes); true is `NPCVehicle_t`, which has no strafe and no turn channel and follows a path
    /// instead. See Docs/Movement.md §3.2.
    /// </summary>
    public bool IsNpcVehicle => _isNpcVehicle;

    bool _isNpcVehicle;

    /// <summary>The NPC vehicle, or null when this body is a player's.</summary>
    public NpcVehicleSim Npc => _sim as NpcVehicleSim;

    /// <summary>
    /// Pick the subclass. Called from <c>Character.Apply</c> once the spawn message's
    /// <c>IsNpc</c> flag is known — <c>Awake</c> runs before that, so the default is the player's and
    /// this rebuilds if the flag says otherwise.
    ///
    /// <para>
    /// Rebuilding is safe at spawn because <c>Apply</c> warps the body immediately afterwards, and on
    /// later updates the kind does not change so this is a no-op.
    /// </para>
    /// </summary>
    public void SelectVehicleKind(bool isNpc)
    {
        if (_sim != null && isNpc == _isNpcVehicle)
            return;

        BuildSim(isNpc);
    }

    void BuildSim(bool isNpc)
    {
        _isNpcVehicle = isNpc;

        // The factory constants are shared: the NPC block at 1005796f has the same shape and the same
        // visible constants as the player factory at 10057826.
        _sim = isNpc ? new NpcVehicleSim() : new CharVehicleSim();
        _sim.Mass = _mass;
        _sim.MaxForce = 10f;
        _sim.MaxVel = 1f;
        _sim.NearProbeOffset = _radius;
        _sim.SlowingDistance = 1.5f;
        _sim.MovementState = ToVehicleState(_state);

        _sim.EnableFalling();
        _sim.DisableSurfaceHug();

        // DummyVehicle_t::UseSurfaceNormal (N3 100011b7), which Gamecode calls for character
        // vehicles at 1006eb9d / 1006ec56 / 1006ee00. This aligns the body to the ground, which is
        // what tilts its forward along a slope -- and that tilt is what lets the swept solver see an
        // uphill move and refuse it. Without it, holding forward climbs anything.
        _sim.UseSurfaceNormal();

        _sim.UpdateMotionConstraints();

        _runLimits = new VelocityLimits(_sim.MaxVel, _sim.MaxVel, _sim.StrafeSpeed(_sim.MovementState));

        PushTransformToSim();
    }

    /// <summary>
    /// Supply the world to collide against. Until this is called the vehicle free-falls, which is
    /// stock's behaviour when <c>GetSurface</c> returns null.
    /// </summary>
    public void SetSurface(ISurface surface, Transform surfaceRoot = null)
    {
        if (_sim == null)
            Awake();

        _sim.Surface = surface;
        if (surfaceRoot != null)
            _surfaceRoot = surfaceRoot;
    }

    void Update()
    {
        if (_sim == null)
            return;

        float dt = Time.deltaTime;

        PushTransformToSim();

        // Stock drives the guide from the AI tick, not from Run. There is no AI here -- the path comes
        // from the server -- so the frame is the tick.
        Npc?.AdvanceGuide(dt);

        if (HasPath)
            SteerAlongPath(dt);

        bool wasAirborne = _sim.Airborne;
        _sim.Run(dt);

        if (!_jumpArmed && wasAirborne && !_sim.Airborne)
            CompleteLanding();

        PullSimToTransform();
    }

    void PushTransformToSim()
    {
        _sim.Position = ToSurfaceSpace(transform.position).ToVec3();

        // Only the heading goes back in. The vehicle's own body rotation carries the surface tilt
        // (orientation mode 1), and re-reading a level transform into it each frame would throw that
        // tilt away every step.
        if (!_headingOnly)
            _sim.BodyRotation = transform.rotation.ToQuat();
    }

    void PullSimToTransform()
    {
        // The vehicle's body rotation is TILTED to the ground in orientation mode 1 -- that is stock
        // (GetBodyRot returns +0x80, which FUN_1000c616 builds with LookRotation(forward, normal)).
        // But the visible character does NOT bank: in the real client a character stays upright on
        // any slope, because the dynel's own rotation is not the vehicle's. So the transform gets the
        // HEADING only, and the tilt stays inside the sim where the steering uses it.
        Vec3 forward = _sim.GetBodyForward();
        Quaternion rotation = transform.rotation;
        if (Mathf.Abs(forward.X) > 1e-5f || Mathf.Abs(forward.Z) > 1e-5f)
            rotation = Quaternion.LookRotation(new Vector3(forward.X, 0f, forward.Z).normalized, Vector3.up);

        transform.SetPositionAndRotation(FromSurfaceSpace(_sim.Position.ToUnity()), rotation);
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

    // ---- input: flags to the four axes ------------------------------------

    /// <summary>
    /// Translate the input layer's flags into the vehicle's four axes — the point where our input
    /// vocabulary meets stock's. Stock's own input path calls the axis setters directly
    /// (<c>100717ef</c>, <c>1007180f</c>, <c>100717ff</c>, <c>10071840</c>).
    ///
    /// <para>
    /// The camera yaw is <b>not</b> applied to the body: stock never snaps a character's heading to
    /// the camera, it applies a delta through <c>VehicleForwardUpdate</c>
    /// (<c>Docs/Camera.md</c> §5.9). <see cref="N3Camera"/> does that via
    /// <see cref="ApplyYawDelta"/>.
    /// </para>
    /// </summary>
    public void SetInputs(MovementFlags flags, Quaternion cameraYaw)
    {
        // An NPC body has no input axes to drive -- NPCVehicle_t's lateral and turn channels are
        // `xor eax,eax; ret 4` and its longitudinal reads a path, not +0x360. Nothing should be
        // feeding player input to one, so say so rather than writing values that do nothing.
        if (_isNpcVehicle)
        {
            Debug.LogWarning($"[Vehicle] SetInputs on the NPC vehicle of '{name}' -- ignored.", this);
            return;
        }

        ClearPath();

        if (_state == MovementState.Sit)
        {
            SetFlags(MovementFlags.None);
            return;
        }

        bool jumpRising = (flags & MovementFlags.Jump) != 0 && (_flags & MovementFlags.Jump) == 0;

        SetFlags(flags);

        if (jumpRising)
            TryStartJump();
    }

    void SetFlags(MovementFlags flags)
    {
        _flags = flags;
        ApplyFlagsToAxes();
    }

    void ApplyFlagsToAxes()
    {
        // An NPC body has no input axes -- NPCVehicle_t's lateral and turn channels are
        // `xor eax,eax; ret 4` and its longitudinal reads a Path_t, not +0x360. The flags are still
        // stored (the animation reads them), but translating them into axis writes would be
        // meaningless AND harmful: the release branch below halts, which would fight the path guide
        // every time the server reported the NPC stopped.
        if (_isNpcVehicle)
            return;

        if (_sim == null)
            return;

        // +0x360 is a GATE, not a speed: the speed comes from MaxVel.
        float drive = 0f;
        if ((_flags & MovementFlags.Forward) != 0)
            drive += 1f;
        if ((_flags & MovementFlags.Backward) != 0)
            drive -= 1f;

        // Direction selects the forward or reverse speed curve (FUN_10070a37: 2 is reverse).
        int direction = drive < 0f ? 2 : 1;
        if (_sim.CurveDirection != direction)
        {
            _sim.CurveDirection = direction;
            _sim.UpdateMotionConstraints();
        }

        // The three stock command handlers, each read in full from disassembly. Every one of them
        // pairs the drive with SetDirection, and the release handler also halts:
        //
        //   forward   1006ef8d:  SetDirection(1);  SetForwardDrive(1)
        //   backward  1006f122:  SetForwardDrive(-1);  SetDirection(-1)
        //   release   1006f23a:  SetForwardDrive(0);  Halt();  SetDirection(1)
        //
        // Neither half is optional.
        //
        // The halt: with the drive at zero the longitudinal channel returns None, the integrator
        // skips the force->velocity step entirely (Docs/Camera.md §4.2 step 3) and never touches
        // Velocity — so without it the body coasts at its last speed forever. That was the "keeps
        // sliding after releasing the key" bug.
        //
        // The direction: it is what tells the orientation update the body is travelling BACKWARDS,
        // so the body keeps facing the way it came from instead of turning to look down its own
        // velocity. Without it, mode 1 faces the body backwards, SteeringReverse then pushes it the
        // other way, and the character oscillates on the spot instead of backing up. SetDirection
        // halts by itself when the value changes (FUN_1000a688), which is what makes a reversal
        // start from rest — so the forward handler needs no halt of its own.
        if (drive != _sim.ForwardDrive)
        {
            if (drive > 0f)
            {
                _sim.SetDirection(1);
                _sim.SetForwardDrive(drive);
            }
            else if (drive < 0f)
            {
                _sim.SetForwardDrive(drive);
                _sim.SetDirection(-1);
            }
            else
            {
                _sim.SetForwardDrive(0f);
                _sim.Halt();
                _sim.SetDirection(1);
            }
        }

        // +0x364 is a SPEED, and SetStrafe keeps only the sign of its argument.
        float strafe = 0f;
        if ((_flags & MovementFlags.StrafeRight) != 0)
            strafe += 1f;
        if ((_flags & MovementFlags.StrafeLeft) != 0)
            strafe -= 1f;
        _sim.SetStrafe(strafe);

        // +0x368 is radians per second about world Y.
        float turn = 0f;
        if ((_flags & MovementFlags.TurnRight) != 0)
            turn += 1f;
        if ((_flags & MovementFlags.TurnLeft) != 0)
            turn -= 1f;
        _sim.SetTurnRate(turn * GetTurnRateRadians());
    }

    /// <summary>
    /// Turn the character by a yaw delta — the camera's right-drag in Lock mode. A delta applied to
    /// the body's facing, never an absolute heading.
    ///
    /// <para>
    /// It goes through <see cref="VehicleSim.SetRelRot"/> and <b>must</b>. Writing the body rotation
    /// on its own only works while the body is standing still: orientation mode 1 rebuilds the
    /// rotation from the cached forward when stopped, but from the <b>velocity</b> when moving
    /// (<c>1000c88c</c>), so a running character's new heading was thrown away on the same frame.
    /// <c>Vehicle_t::SetRelRot</c> (<c>1000d11d</c>) is stock's answer: it sets the rotation and
    /// re-aims the velocity along the new facing, <c>bodyForward * |velocity| * Direction</c>. The
    /// <c>Direction</c> factor is what keeps a backpedalling character turning the right way.
    /// </para>
    /// </summary>
    public void ApplyYawDelta(float degrees)
    {
        if (_state == MovementState.Sit || Mathf.Approximately(degrees, 0f))
            return;

        transform.Rotate(0f, degrees, 0f);
        if (_sim != null)
            _sim.SetRelRot(Quat.LookRotation(transform.forward.ToVec3(), _sim.SurfaceNormal));
    }

    public void ApplyAction(MovementAction action)
    {
        ClearPath();

        switch (action)
        {
            case MovementAction.ForwardStart: SetFlags(_flags | MovementFlags.Forward); break;
            case MovementAction.ForwardStop: SetFlags(_flags & ~MovementFlags.Forward); break;
            case MovementAction.BackwardStart: SetFlags(_flags | MovementFlags.Backward); break;
            case MovementAction.BackwardStop: SetFlags(_flags & ~MovementFlags.Backward); break;
            case MovementAction.StrafeLeftStart: SetFlags(_flags | MovementFlags.StrafeLeft); break;
            case MovementAction.StrafeLeftStop: SetFlags(_flags & ~MovementFlags.StrafeLeft); break;
            case MovementAction.StrafeRightStart: SetFlags(_flags | MovementFlags.StrafeRight); break;
            case MovementAction.StrafeRightStop: SetFlags(_flags & ~MovementFlags.StrafeRight); break;
            case MovementAction.TurnLeftStart: SetFlags(_flags | MovementFlags.TurnLeft); break;
            case MovementAction.TurnLeftStop: SetFlags(_flags & ~MovementFlags.TurnLeft); break;
            case MovementAction.TurnRightStart: SetFlags(_flags | MovementFlags.TurnRight); break;
            case MovementAction.TurnRightStop: SetFlags(_flags & ~MovementFlags.TurnRight); break;
            case MovementAction.JumpStart:
                SetFlags(_flags | MovementFlags.Jump);
                TryStartJump(requireGrounded: false);
                break;
            case MovementAction.JumpStop: SetFlags(_flags & ~MovementFlags.Jump); break;
            case MovementAction.FullStop: SetFlags(MovementFlags.None); break;
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
        ClearPath();
        EnterMovementState(ToMovementState(status.ModeId));
        _lastSpeedMode = ToMovementState(status.LastSpeedMode);

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

        SetFlags(flags);
    }

    // ---- movement state --------------------------------------------------

    /// <summary>
    /// Maps the network's <c>MovementState</c> onto stock's vehicle state
    /// (<c>FUN_10070a2f</c>). <b>The correspondence is not fully recovered</b> — only that 7 is Fly
    /// and that 1, 8 and 9 refuse forward drive. See <c>Docs/Movement.md</c> §9.
    /// </summary>
    static int ToVehicleState(MovementState state) => (int)state;

    void EnterMovementState(MovementState state)
    {
        if (_state == MovementState.Walk || _state == MovementState.Run)
            _lastSpeedMode = _state;

        if (state == MovementState.Sit)
        {
            SetFlags(MovementFlags.None);
            ClearPath();
        }

        _state = state;

        if (_sim != null)
        {
            _sim.MovementState = ToVehicleState(state);
            _sim.UpdateMotionConstraints();
            _runLimits = new VelocityLimits(_sim.MaxVel, _sim.MaxVel, _sim.StrafeSpeed(_sim.MovementState));
            ApplyFlagsToAxes();
        }
    }

    void LeaveMovementState()
        => EnterMovementState(_lastSpeedMode is MovementState.Walk or MovementState.Run
            ? _lastSpeedMode
            : MovementState.Run);

    static MovementState ToMovementState(uint modeId)
        => Enum.IsDefined(typeof(MovementState), (int)modeId) ? (MovementState)modeId : MovementState.Run;

    /// <summary>
    /// The run-speed stat, which drives the whole curve (<c>FUN_1006f2fc</c>).
    ///
    /// <para>
    /// The health penalty the previous file applied is <b>dropped</b>: it was invented, and stock's
    /// stat lookup has not been reversed far enough to say whether one exists.
    /// </para>
    /// </summary>
    public void UpdateRunLimitsFromStats(int runSpeed, int currentHealth, int maxHealth)
    {
        if (_sim == null)
            return;

        _sim.RunSpeedStat = runSpeed;
        _sim.UpdateMotionConstraints();
        _runLimits = new VelocityLimits(_sim.MaxVel, _sim.MaxVel, _sim.StrafeSpeed(_sim.MovementState));
        ApplyFlagsToAxes();
    }

    public void UpdateJumpStatsFromStats(int strength, int agility, int gmLevel)
    {
        _jumpStrength = strength;
        _jumpAgility = agility;
        _jumpGmLevel = gmLevel;
    }

    public void Halt()
    {
        _sim?.Halt();
    }

    /// <summary>
    /// Place the vehicle without steering — stock's <c>SetRelPos</c> path, which runs collision but
    /// bypasses the integrator.
    /// </summary>
    public void Warp(Vector3 position, Quaternion rotation, bool resetVelocity = true)
    {
        if (_sim == null)
            Awake();

        transform.SetPositionAndRotation(position, rotation);
        _sim.Position = ToSurfaceSpace(position).ToVec3();

        // SetRelRot so a warp that keeps its velocity carries it into the new facing instead of
        // holding the old world-space direction. Stock's teleport is SetRelPosRot (1000e2af), which
        // is UNREAD — this is SetRelRot's verified behaviour applied to the rotation half.
        _sim.SetRelRot(Quat.LookRotation(
            (rotation * Vector3.forward).ToVec3(), _sim.SurfaceNormal));

        if (resetVelocity)
        {
            _sim.Halt();
            _jumpArmed = true;
            SetFlags(MovementFlags.None);
        }
    }

    /// <summary>
    /// Was a hook for the old collider-streaming wait. With collision coming from the heightmap there
    /// is nothing to stream, so this only re-seats the vehicle at the spawn point.
    /// </summary>
    public void RequestSurfacePriorityForSpawn(Vector3 worldPosition)
    {
        if (_sim == null)
            Awake();

        transform.position = worldPosition;
        PushTransformToSim();
    }

    // ---- paths -----------------------------------------------------------

    /// <summary>
    /// The server's waypoint list, from <c>FollowTargetMessage.PathInfo</c>.
    ///
    /// <para>
    /// On an NPC body this fills the reversed <c>Path_t</c> and restarts the guide, so
    /// <c>NPCVehicle_t</c>'s longitudinal channel steers at it with <c>SteeringDirArrive</c> — stock's
    /// model. On a player's body it falls back to the carried-over follower below, which is the
    /// previous developer's and drives the input axes instead.
    /// </para>
    /// </summary>
    public void SetPath(IReadOnlyList<Vector3> waypoints)
    {
        ClearPath();
        SetFlags(MovementFlags.None);

        if (waypoints == null || waypoints.Count == 0)
            return;

        NpcVehicleSim npc = Npc;
        if (npc != null)
        {
            npc.Path.Clear();
            for (int i = 0; i < waypoints.Count; i++)
                npc.Path.AddWaypoint(ToSurfaceSpace(waypoints[i]).ToVec3());
            npc.RestartPath();
            return;
        }

        for (int i = 0; i < waypoints.Count; i++)
            _path.Add(waypoints[i]);

        _pathIndex = 0;
    }

    public void ClearPath()
    {
        _path.Clear();
        _pathIndex = -1;
        Npc?.Path.Clear();
    }

    /// <summary>
    /// Follows a waypoint list by pointing the body at the next one and driving forward. Stock's
    /// equivalent is the follow-target branch of the longitudinal channel
    /// (<c>SteeringDirArrive</c> at <c>10071537</c>), which is not ported.
    /// </summary>
    void SteerAlongPath(float dt)
    {
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
                    _sim.SetForwardDrive(0f);
                    _sim.SetTurnRate(0f);
                    _sim.Halt();
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
            _sim.SetRelRot(transform.rotation.ToQuat());
            _sim.SetForwardDrive(1f);
            return;
        }
    }

    // =====================================================================
    // UNPORTED GLUE — carried over from the deleted CharacterMotor so the game
    // keeps working. None of this is reverse-engineered; it is the previous
    // developer's model and needs its own pass. Docs/Movement.md §8.
    // =====================================================================

    /// <summary>
    /// UNPORTED. Stock's keyboard turn is scaled from a +/-0.02 rad rate by
    /// <c>MouseTurnSensitivity</c> (<c>100229ff</c>); these two numbers are the previous
    /// developer's and are not stock.
    /// </summary>
    float GetTurnRateRadians()
    {
        MovementConfig config = Config;
        float moving = config != null ? config.TurnRateRadiansMoving : 1.5f;
        float stopped = config != null ? config.TurnRateRadiansStopped : 3.5f;
        return IsMoving ? moving : stopped;
    }

    /// <summary>
    /// UNPORTED. Stock jumps through a CharacterAction, not a vertical impulse computed from stats.
    /// This formula is the previous developer's, retained only so jumping still functions.
    /// </summary>
    bool TryStartJump(bool requireGrounded = true)
    {
        if (_sim == null || !_jumpArmed || _state == MovementState.Sit)
            return false;
        if (requireGrounded && _sim.Airborne)
            return false;

        MovementConfig config = Config;
        float cap = config != null ? config.JumpStatCap : 800f;
        float perPool = config != null ? config.JumpHeightPerStatPool : 200f;
        float baseHeight = config != null ? config.JumpHeightBase : 1f;
        float floor = config != null ? config.JumpHeightFloor : 0.5f;

        float str = _jumpStrength;
        float agi = _jumpAgility;
        if (str + agi > cap && _jumpGmLevel == 0)
        {
            str = cap;
            agi = 0f;
        }

        float height = (str + agi) / perPool + baseHeight;
        if (height < floor)
            height = floor;

        _sim.VerticalVelocity = Mathf.Sqrt(2f * height * Mathf.Abs(VehicleSim.GravityAccel));
        _sim.BeginFalling();
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

    /// <summary>UNPORTED. Animation clip selection — not a vehicle concern in stock.</summary>
    public bool SuppressLocomotionPlay
    {
        get
        {
            int mode = (int)_state;
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
        bool forward = HasPath || (_flags & MovementFlags.Forward) != 0;
        if (!forward)
            return 0;

        return _state == MovementState.Walk ? AnimKindIds.JumpLandWalk : AnimKindIds.JumpLandRun;
    }

    /// <summary>UNPORTED. Animation clip selection.</summary>
    public int GetStrafeOverlayKind()
    {
        if ((_flags & (MovementFlags.Forward | MovementFlags.Backward)) == 0)
            return 0;

        if ((_flags & MovementFlags.StrafeLeft) != 0)
            return AnimKindIds.WalkLeft;
        if ((_flags & MovementFlags.StrafeRight) != 0)
            return AnimKindIds.WalkRight;
        return 0;
    }

    /// <summary>UNPORTED. Animation clip selection.</summary>
    public bool TryGetLocomotionKind(AnimHolder holder, out int kind)
    {
        kind = 0;
        if (holder == null || _state == MovementState.Sit)
            return false;

        int mode = (int)_state;
        if (mode == 8 || mode == 9)
            return false;

        bool translating = IsTranslating || HasPath;

        switch (_state)
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
            if ((_flags & MovementFlags.TurnLeft) != 0 && _state == MovementState.Walk)
            {
                kind = AnimKindIds.TurnLeft;
                return true;
            }

            if ((_flags & MovementFlags.TurnRight) != 0 && _state == MovementState.Walk)
            {
                kind = AnimKindIds.TurnRight;
                return true;
            }

            kind = holder.Idle;
            return kind != 0;
        }

        if (HasPath || (_flags & MovementFlags.Forward) != 0)
        {
            kind = _state == MovementState.Walk ? holder.WalkForward : holder.RunForward;
            return kind != 0;
        }

        if ((_flags & MovementFlags.Backward) != 0)
        {
            kind = _state == MovementState.Walk ? AnimKindIds.WalkBack : AnimKindIds.RunBack;
            return true;
        }

        if ((_flags & MovementFlags.StrafeLeft) != 0)
        {
            kind = AnimKindIds.WalkLeft;
            return true;
        }

        if ((_flags & MovementFlags.StrafeRight) != 0)
        {
            kind = AnimKindIds.WalkRight;
            return true;
        }

        kind = _state == MovementState.Walk ? holder.WalkForward : holder.RunForward;
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

        if (_state == MovementState.Walk)
            return walkBase;

        if (HasPath || (_flags & MovementFlags.Forward) != 0)
            return runForwardBase;

        if ((_flags & MovementFlags.Backward) != 0)
            return runBackwardBase;

        if ((_flags & (MovementFlags.StrafeLeft | MovementFlags.StrafeRight)) != 0)
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
    /// See Docs/Movement.md §3.2.
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
