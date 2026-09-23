using System;
using LostEden.Vehicles;
using UnityEngine;

/// <summary>
/// Stock <c>n3Camera_t</c> (<c>N3.dll</c>, vftable <c>1003e3c4</c>), and its per-frame driver
/// <c>FUN_10022345</c> which is reached from that vftable at <c>1003e3ec</c>.
///
/// This is the camera <i>entity</i>: it owns the view, takes the input verbs (mouse look, zoom,
/// orbit), holds the view mode at <c>+0x1ec</c>, dispatches the right vehicle for it
/// (<c>FUN_10020290</c>), and ticks it. The motion itself lives in the Unity-free
/// <see cref="CameraVehicleSim"/> hierarchy, the same split the effects port uses between a
/// <c>GfxControl*</c> binding and its <c>*Sim</c>.
///
/// See <c>Docs/Camera.md</c>.
/// </summary>
[DefaultExecutionOrder(20000)]
public class N3Camera : MonoBehaviour
{
    /// <summary>
    /// First-person pitch limit, <c>n3Camera_t::MouseCameraControl</c> (<c>100216cf</c>):
    /// 1.553343 rad = 89.0 degrees. Stock clamps the accumulated angle here, which is safe because
    /// first person keeps explicit yaw/pitch accumulators rather than re-deriving them from a
    /// direction vector.
    /// </summary>
    const float MaxPitchRadians = 1.553343f;

    /// <summary>
    /// Third-person pitch guard, <c>FUN_1002118c</c>: the orbit refuses any pitch that would push
    /// the camera direction's Y past this. Stock uses 0.9999, i.e. about 89.2 degrees of elevation.
    /// </summary>
    const float PitchLimitY = 0.9999f;

    [Header("Look")]
    [SerializeField]
    [Tooltip("Orbit sensitivity, per axis. The effective rate is 0.1 x this in degrees per mouse " +
             "pixel, because InputController pre-scales the raw delta by 0.05 and then by its own " +
             "_lookSensitivity of 2 — so 5 here is 0.5 deg/pixel, which matches the real client at " +
             "its Sensitivity 100. Stock's own values live on n3Camera_t at +0x22c and +0x230 and " +
             "are applied to an already-scaled delta, so they do not convert directly. The two " +
             "axes are equal because stock's are (+0x230 defaults to -1.0, where the sign is the " +
             "invert-Y flag and the magnitude is the sensitivity; see 100200c3).")]
    Vector2 _lookSensitivity = new Vector2(5f, 5f);

    [SerializeField]
    [Tooltip("Whether holding the RIGHT button also pitches the camera. Stock gates this behind the " +
             "RMBMouseLook1st / RMBMouseLook3rd DistributedValues (Gamecode N3Msg_MouseMovement, " +
             "10019768) and passes only the vertical delta through: MouseCameraControl(0.0, dy). " +
             "Both default ON in the live client, which is why a right drag there turns the " +
             "character with the horizontal and pitches the camera with the vertical. The defaults " +
             "are not in these DLLs — the only reference to either name is the read above.")]
    bool _rightDragPitchesCamera = true;

    [Header("Zoom")]
    [SerializeField]
    [Tooltip("Metres of distance queued per wheel notch. The 0.7 m and 25 m limits are stock's " +
             "and live in the vehicle, not here.")]
    float _zoomStep = 4f;

    [SerializeField]
    [UnityEngine.Serialization.FormerlySerializedAs("_aimLayerMask")]
    [Tooltip("What blocks the camera's line of sight. Defaults to the Ground layer.")]
    LayerMask _collisionMask;

    [SerializeField]
    internal Camera Camera;

    [Header("View")]
    [SerializeField]
    [Tooltip("Which of stock's four camera modes to use. Changing this while playing rebuilds the " +
             "vehicle immediately, so the four can be compared side by side.")]
    CameraViewMode _viewMode = DefaultViewMode;

    CameraVehicleSim _vehicle;

    /// <summary>What <see cref="_vehicle"/> was actually built for, so inspector edits are noticed.</summary>
    CameraViewMode _activeMode;

    /// <summary>
    /// Mode used until a stored preference exists. Stock persists the mode as the int pref
    /// "PreferredCameraMode" (N3 10020032) and never writes mode 0, so the game never starts in
    /// first person; its fresh-install value is not recovered (Docs/Camera.md §8).
    /// </summary>
    const CameraViewMode DefaultViewMode = CameraViewMode.Lock;

    const string ViewModePref = "PreferredCameraMode";

    /// <summary>The live view mode. Setting it rebuilds the vehicle, as stock's dispatch does.</summary>
    public CameraViewMode ViewMode
    {
        get => _viewMode;
        set => SetViewMode(value);
    }

    Transform _followRoot;
    Attractor _headAttractor;
    Character _pendingTarget;
    Character _target;
    bool _detached;

    Vector3 _lookInput;
    float _zoomDelta;
    bool _leftMouseHeld;
    bool _rightClickHeld;
    /// <summary>
    /// Turn the character by a right-drag's horizontal delta, the way stock's
    /// <c>n3Dynel_t::VehicleForwardUpdate</c> (<c>N3 10004ebd</c>) does: a quaternion about world up
    /// composed into the body rotation, not an absolute heading.
    ///
    /// This happens inline, in the same frame as the camera's own pitch, because stock does both
    /// inside one call — <c>N3Msg_MouseMovement</c> pitches the camera at <c>10019836</c> and turns
    /// the character at <c>10019a85</c>. Deferring the turn to the next frame leaves the camera a
    /// frame ahead of the body, which reads as the two fighting each other.
    /// </summary>
    void TurnCharacter(float yawDegrees)
    {
        if (yawDegrees == 0f || _target == null || _target.Motor == null)
            return;

        _target.Motor.ApplyYawDelta(yawDegrees);

        // The vehicle's goal is built from the target's rotation, so re-read it before the tick
        // places the camera; otherwise Lock snaps behind where the character was.
        SyncTargetPose();
    }

    /// <summary>
    /// Fired when a pending character target is fully resolved (head attractor found and pose snapped).
    /// </summary>
    public event Action TargetAttached;

    /// <summary>The live vehicle, for tests and debug overlays.</summary>
    internal CameraVehicleSim Vehicle => _vehicle;

    /// <summary>Non-null in the three third-person modes.</summary>
    CameraVehicleFixedThirdSim _third;

    /// <summary>Non-null in first person.</summary>
    CameraVehicleFirstPersonSim _first;

    float _firstPersonYaw;
    float _firstPersonPitch;

    void Awake()
    {
        _activeMode = _viewMode;
        BuildVehicle();
    }

    /// <summary>
    /// Stock's FUN_10020290: each mode gets its own vehicle. Modes 2 and 3 are the same class
    /// separated by the +0x214 flag.
    /// </summary>
    void BuildVehicle()
    {
        Vec3 position = _vehicle != null ? _vehicle.Position : Vec3.Zero;
        Vec3 eye = _vehicle != null ? _vehicle.EyeTargetLocalPos : Vec3.Zero;
        float follow = _vehicle != null ? _vehicle.FollowDistance : 0f;

        if (_viewMode == CameraViewMode.FirstPerson)
        {
            var fp = new CameraVehicleFirstPersonSim();
            fp.ApplyStockCameraSettings();
            fp.HasSurface = false;        // GetSurface returns null (10007572)
            _first = fp;
            _third = null;
            _vehicle = fp;
            _firstPersonYaw = 0f;
            _firstPersonPitch = 0f;
        }
        else
        {
            var third = new CameraVehicleFixedThirdSim
            {
                // 2 -> false (steers), 3 -> true (rigid). 100202a9 / 100202b1.
                Frozen = _viewMode == CameraViewMode.Lock,
                LineOfSight = HasLineOfSight,
            };
            third.ApplyStockDefaults();
            if (follow > 0f)
                third.FollowDistance = follow;
            _third = third;
            _first = null;
            _vehicle = third;
        }

        _vehicle.EyeTargetLocalPos = eye;
        _vehicle.Position = position;
        _vehicle.SetVel(Vec3.Zero);

        if (_followRoot != null)
            SyncTargetPose();
    }

    /// <summary>Switch view mode, rebuilding the vehicle and persisting the preference.</summary>
    public void SetViewMode(CameraViewMode mode)
    {
        if (_viewMode == mode && _activeMode == mode)
            return;

        _viewMode = mode;
        ApplyViewMode();
    }

    void ApplyViewMode()
    {
        _activeMode = _viewMode;
        BuildVehicle();

        if (_followRoot != null)
        {
            PlaceAtRest();
            ApplyToTransform();
        }

        // Stock only writes the pref for non-zero modes (10020043), so first person is never sticky.
        // Nothing reads it back yet — the inspector field is authoritative until a settings UI exists.
        if (_viewMode != CameraViewMode.FirstPerson)
            PlayerPrefs.SetInt(ViewModePref, (int)_viewMode);
    }

    /// <summary>
    /// n3Camera_t::ToggleCameraView (1002190e) — flip between first person and the last third-person
    /// mode, which is what the stored preference holds.
    /// </summary>
    public void ToggleCameraView()
    {
        if (_viewMode == CameraViewMode.FirstPerson)
        {
            var stored = (CameraViewMode)PlayerPrefs.GetInt(ViewModePref, (int)DefaultViewMode);
            SetViewMode(stored == CameraViewMode.FirstPerson ? DefaultViewMode : stored);
        }
        else
        {
            _lastThirdPerson = _viewMode;
            SetViewMode(CameraViewMode.FirstPerson);
        }
    }

    /// <summary>n3Camera_t::IsFirstPerson (10020071).</summary>
    public bool IsFirstPerson => _viewMode == CameraViewMode.FirstPerson;

    CameraViewMode _lastThirdPerson = DefaultViewMode;

    int OcclusionMask => _collisionMask.value != 0 ? _collisionMask.value : GameLayers.GroundMask;

    bool HasLineOfSight(Vec3 from, Vec3 to)
    {
        Vector3 a = from.ToUnity();
        Vector3 b = to.ToUnity();
        Vector3 delta = b - a;
        float distance = delta.magnitude;
        if (distance < 1e-4f)
            return true;

        return !Physics.Raycast(a, delta / distance, distance, OcclusionMask,
            QueryTriggerInteraction.Ignore);
    }

    internal void SetInputs(ActorInput playerInput)
    {
        _lookInput = playerInput.LookInput;
        _zoomDelta = playerInput.ZoomDelta;
        _leftMouseHeld = playerInput.LeftClickHeld;
        _rightClickHeld = playerInput.RightClickHeld;
    }

    internal void SetTarget(Character character)
    {
        _pendingTarget = character;
        TryResolveTarget();
    }

    internal void ClearTarget()
    {
        _followRoot = null;
        _headAttractor = null;
        _pendingTarget = null;
        _target = null;
    }

    void TryResolveTarget()
    {
        if (_pendingTarget == null)
            return;

        if (!_pendingTarget.TryGetAttractor(AttractorPlace.Head, out _headAttractor))
            return;

        _target = _pendingTarget;
        _followRoot = _pendingTarget.transform;
        _pendingTarget = null;
        _detached = false;

        // Stock sets the eye offset once (SetEyeTargetLocalPos), it does not re-read the head bone
        // every frame — otherwise the walk animation's head bob would drive the whole camera.
        Vector3 headLocal = _followRoot.InverseTransformPoint(_headAttractor.transform.position);
        _vehicle.EyeTargetLocalPos = new Vec3(0f, headLocal.y, 0f);

        SyncTargetPose();
        PlaceAtRest();
        ApplyToTransform();

        TargetAttached?.Invoke();
    }

    /// <summary>Put the camera where its mode says it belongs, without steering in from wherever.</summary>
    void PlaceAtRest()
    {
        if (_third != null)
        {
            _third.SetPreferredOffset(CameraVehicleFixedThirdSim.DefaultPreferredOffset);
            _third.RecalcOptimalPos();
            _third.Position = _third.GetOptimalPos();
        }
        else
        {
            _first.SetRotAngles(_firstPersonPitch, _firstPersonYaw);
        }

        _vehicle.SetVel(Vec3.Zero);
    }

    void SyncTargetPose()
    {
        _vehicle.TargetPosition = _followRoot.position.ToVec3();
        _vehicle.TargetRotation = _followRoot.rotation.ToQuat();
    }

    void LateUpdate()
    {
        if (_detached)
            return;

        if (_followRoot == null)
        {
            TryResolveTarget();
            return;
        }

        // Picking a different mode in the inspector takes effect immediately.
        if (_viewMode != _activeMode)
            ApplyViewMode();

        SyncTargetPose();

        // A surface is only bound once there is something to occlude against; first person never
        // has one.
        _vehicle.HasSurface = _third != null && OcclusionMask != 0;

        ApplyZoom();
        ApplyOrbit();

        // Max speed, not current — stock reads the character vehicle's Vehicle_t +0x3c (10022404).
        float characterMaxSpeed = _target != null && _target.Motor != null
            ? _target.Motor.LocomotionMaxSpeed
            : 0f;

        _vehicle.Tick(Time.deltaTime, characterMaxSpeed);
        ApplyToTransform();
    }

    /// <summary>
    /// Queues a zoom the way stock does: a <b>distance to cover</b>, not a rate. The vehicle's own
    /// state machine (<c>CameraVehicleSim.UpdateZoom</c>) spends it down over the following frames
    /// and commits the new follow distance when it finishes.
    ///
    /// Positive zooms in, which is why the wheel's sign is flipped here — scrolling up zooms in.
    /// </summary>
    void ApplyZoom()
    {
        // First person has nothing to zoom — the camera is at the eye.
        if (_zoomDelta == 0f || _third == null)
            return;

        _third.RequestZoom(Mathf.Sign(_zoomDelta) * _zoomStep);
    }

    /// <summary>
    /// Orbit, the way stock does it (<c>FUN_1002118c</c>, <c>10021524</c>): compute the new camera
    /// position, <b>move the camera straight there</b> with <c>SetRelPos</c>, and only then sync the
    /// preferred direction with <c>UpdateHeadingToPos</c> and <c>ForcedUpdate(false)</c>.
    ///
    /// Mouse look is one-to-one and instant. It is <b>not</b> steered — steering is only how the
    /// camera follows the character, which is where the rubber-band feel belongs. Setting the goal
    /// and letting the vehicle arrive at it makes a flick drift for seconds afterwards.
    ///
    /// No yaw/pitch is stored between frames on purpose — the direction lives in the target's local
    /// frame inside the vehicle, so stay-behind keeps working when the character turns.
    /// </summary>
    void ApplyOrbit()
    {
        if (!_leftMouseHeld && !_rightClickHeld)
            return;

        if (Mathf.Approximately(_lookInput.x, 0f) && Mathf.Approximately(_lookInput.y, 0f))
            return;

        // The look input is a mouse *delta*, not a rate, so it must not be scaled by deltaTime —
        // doing so makes sensitivity depend on frame rate and shrinks it by ~60x.
        float yawDelta = _lookInput.x * _lookSensitivity.x;
        float pitchDelta = _lookInput.y * _lookSensitivity.y;

        // Right drag is stock's mouse-look, and its horizontal turns the CHARACTER, not the camera.
        // n3EngineClientAnarchy_t::N3Msg_MouseMovement (Gamecode 100196b3) calls
        // MouseCameraControl(0.0, dy) — yaw hardcoded to zero — and routes the horizontal delta to
        // the character as a local heading update (n3Dynel_t::VehicleForwardUpdate). The camera
        // swings round on its own because it is stay-behind. Left drag orbits the camera freely and
        // leaves the character alone, which is why standing still and left-dragging moves nothing.
        float characterYaw = 0f;
        if (_rightClickHeld)
        {
            characterYaw = yawDelta;
            yawDelta = 0f;

            // Stock only pitches the camera on a right drag when RMBMouseLook3rd (or ...1st) is
            // set; otherwise the right button moves the character and nothing else.
            if (!_rightDragPitchesCamera)
                pitchDelta = 0f;
        }

        // Stock's order, and it is load-bearing: the camera moves against the body's CURRENT
        // rotation (10019836), and only then does the body turn (10019a85). Turning first would
        // leave the camera's offset pointing at where the character used to face, and the Lock
        // re-seat below would then store that stale bearing — the camera drifts off the character's
        // back a little more with every frame of the drag.
        if (yawDelta != 0f || pitchDelta != 0f)
        {
            if (_first != null)
                OrbitFirstPerson(yawDelta, pitchDelta);
            else
                OrbitThirdPerson(yawDelta, pitchDelta);
        }

        TurnCharacter(characterYaw);
    }

    /// <summary>
    /// The third-person orbit proper — <c>FUN_1002118c</c>. Takes the deltas already split between
    /// camera and character by <see cref="ApplyOrbit"/>.
    /// </summary>
    void OrbitThirdPerson(float yawDelta, float pitchDelta)
    {
        // Stock rotates the camera's CURRENT offset from the look target (1002119f:
        // offset = position - GetLookTargetPos()), not the preferred direction at the preferred
        // distance. That matters in Rubber: while walking the camera lags behind its ideal spot, so
        // rebuilding the position at FollowDistance yanks it ~1 m closer on the first mouse movement
        // and the steering then drifts it back out — orbit and rubber appearing to fight.
        Vector3 lookTargetNow = _third.GetLookTargetPos().ToUnity();
        Vector3 offset = _third.Position.ToUnity() - lookTargetNow;
        if (offset.sqrMagnitude < 1e-8f)
            return;

        Vector3 dir = offset.normalized;
        float currentDistance = offset.magnitude;

        // Yaw about world up (left drag only).
        if (yawDelta != 0f)
            dir = Quaternion.AngleAxis(yawDelta, Vector3.up) * dir;

        // Pitch about the horizontal axis perpendicular to the look direction.
        //
        // Stock does NOT clamp this to an angle. FUN_1002118c adds the pitch delta to the
        // direction's Y and, if the result would exceed PitchLimitY in magnitude, zeroes the delta
        // and applies no pitch at all. Refusing the movement rather than limiting it is what makes
        // it pole-safe: the direction can never overshoot past vertical, wrap over the top (which
        // flips the pitch axis and inverts every subsequent input), or land exactly on the pole
        // (where the axis degenerates and both pitch AND yaw become no-ops, locking the camera).
        //
        // Deviation: stock tests `dir.y + delta`, mixing a unit component with an angle in radians
        // — a cheap approximation. This tests the actual resulting Y, which is the same rule
        // applied exactly and does not depend on the sign convention of the axis.
        if (pitchDelta != 0f)
        {
            Vector3 pitchAxis = Vector3.Cross(Vector3.up, dir);
            if (pitchAxis.sqrMagnitude > 1e-8f)
            {
                Vector3 candidate = Quaternion.AngleAxis(pitchDelta, pitchAxis.normalized) * dir;
                if (Mathf.Abs(candidate.y) <= PitchLimitY)
                    dir = candidate;
            }
        }

        // Stock clamps the orbited offset to the 25 m ceiling and nothing else (1002151a), so the
        // camera keeps whatever distance it currently has.
        float distance = Mathf.Min(currentDistance, CameraVehicleSim.MaxZoomDistance);
        Vec3 newPosition = (lookTargetNow + dir * distance).ToVec3();

        _third.SetRelPos(newPosition);

        // What happens next depends on the mode (10021560 onward), and getting it wrong ratchets
        // the camera outward every drag.
        if (_viewMode == CameraViewMode.Lock)
        {
            // Lock re-seats the goal on every drag (10021572).
            _third.UpdateHeadingToPos(newPosition, false);
            _third.ForcedUpdate(false);
            return;
        }

        // Trail and Rubber: while the camera is actually moving, stock does nothing further — the
        // goal is left alone and the steering pulls the camera back toward it (1002159e). Only once
        // it has nearly stopped does it resync the distance and re-seat.
        //
        // Re-seating while moving is wrong twice over: UpdateHeadingToPos divides by FollowDistance
        // rather than by the offset's own length (1001f6a3), so a lagging camera writes a preferred
        // direction longer than one, the goal moves out to wherever the camera currently is, and the
        // next drag repeats it.
        if (_third.Speed < CameraVehicleSim.OrbitResyncSpeed)
        {
            _third.ResyncFollowDistance();
            _third.ForcedUpdate(false);
        }
    }

    /// <summary>
    /// First person has no orbit — the camera is at the eye. Mouse look accumulates yaw and pitch
    /// and hands them to <c>SetRotAngles(pitch, yaw)</c> (<c>1001ef0d</c>), which is exactly what
    /// stock's <c>MouseCameraControl</c> does for mode 0 (<c>1002171b</c>). Pitch is clamped to
    /// stock's +/-1.553343 rad.
    /// </summary>
    void OrbitFirstPerson(float yawDelta, float pitchDelta)
    {
        _firstPersonYaw += yawDelta * Mathf.Deg2Rad;

        _firstPersonPitch = Mathf.Clamp(
            _firstPersonPitch + pitchDelta * Mathf.Deg2Rad, -MaxPitchRadians, MaxPitchRadians);

        _first.SetRotAngles(_firstPersonPitch, _firstPersonYaw);
    }

    /// <summary>
    /// Position straight from the vehicle; rotation from the veto chain, which is what stock's body
    /// orientation update does for orientation mode 3 (<c>Vehicle.dll 1000c754</c>):
    /// <c>VetoUpAlignment</c> then <c>VetoForward</c>, then a look rotation from the two.
    /// </summary>
    void ApplyToTransform()
    {
        transform.position = _vehicle.Position.ToUnity();

        _vehicle.VetoUpAlignment(out Vec3 up);
        _vehicle.VetoForward(out Vec3 forward);

        Vector3 f = forward.ToUnity();
        Vector3 u = up.ToUnity();
        if (f.sqrMagnitude > 1e-8f && Vector3.Cross(f, u).sqrMagnitude > 1e-8f)
            transform.rotation = Quaternion.LookRotation(f, u);
    }

    internal void SetFreePose(Vector3 position, Vector3 eulerAngles)
    {
        ClearTarget();
        _detached = true;
        transform.SetPositionAndRotation(position, Quaternion.Euler(eulerAngles));
    }

    /// <summary>
    /// Pitch and yaw of where the camera is looking, in degrees. <c>PlayerController</c> uses the
    /// yaw to steer the character.
    /// </summary>
    public Vector2 GetViewAngles()
    {
        Vector3 forward = transform.forward;
        Vector3 flat = new Vector3(forward.x, 0f, forward.z);

        float yaw = flat.sqrMagnitude > 1e-8f
            ? Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg
            : transform.eulerAngles.y;

        float pitch = -Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;

        return new Vector2(pitch, yaw);
    }
}
