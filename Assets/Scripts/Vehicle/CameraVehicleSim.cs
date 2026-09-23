using System;

namespace LostEden.Vehicles
{
    /// <summary>
    /// Stock <c>CameraVehicle_t</c> (<c>N3.dll</c>, ctor <c>1001d440</c> ordinal 3; vftable
    /// <c>1003df6c</c>). The camera's steering brain — it decides where the camera wants to be and
    /// hands a force to <see cref="VehicleSim"/>, which moves it.
    ///
    /// See <c>Docs/Camera.md</c> §5. Unity-free; a <c>MonoBehaviour</c> supplies the look target and
    /// the line-of-sight query.
    /// </summary>
    public class CameraVehicleSim : VehicleSim
    {
        /// <summary>
        /// The working follow distance is never allowed below this (<c>Update</c>, <c>1001e59a</c>).
        /// The previous implementation used 1.0; stock is 0.9, and the figure shows up again in
        /// <c>CameraVehicleFixedThird_t::VetoForward</c>.
        /// </summary>
        public const float MinFollowDistance = 0.9f;

        /// <summary>+0x198, <c>PreferredCamDist</c>. The ctor's default before prefs load.</summary>
        public const float DefaultFollowDistance = 5f;

        /// <summary>
        /// Halt radius <c>CameraVehicleFixedThird_t::CalcSteering</c> passes to
        /// <c>SteeringCamArrive</c> (<c>1003d618</c> = 0.01), so the camera effectively never halts.
        /// </summary>
        public const float ArriveHaltRadius = 0.01f;

        /// <summary>
        /// <c>UpdateMotionConstraints</c> reaches max speed in this long — max force is
        /// <c>mass * maxVel / 0.3</c> (<c>1001e6b6</c>).
        /// </summary>
        public const float ForceReachTime = 0.3f;

        /// <summary>Below this the camera never slows down (<c>1001e6a0</c>).</summary>
        public const float MinCatchUpSpeed = 2f;

        /// <summary>Speed ceiling while catching up to a distant target (<c>1001e68a</c>).</summary>
        public const float MaxCatchUpSpeed = 80f;

        /// <summary>Beyond this distance the camera starts speeding up (<c>1001e666</c>).</summary>
        public const float CatchUpDistance = 6f;

        /// <summary>
        /// The driver derives the camera's base speed from the character's <b>max</b> speed times
        /// this (<c>10022432</c>), so a faster character gets a faster camera.
        /// </summary>
        public const float BaseSpeedFromCharacterSpeed = 6f;

        /// <summary>Ceiling on that derived base speed (<c>1002241c</c>).</summary>
        public const float MaxBaseSpeed = 16f;

        /// <summary>Subtracted before the square root in the catch-up curve (<c>1001e671</c>).</summary>
        public const float CatchUpFalloff = 5f;

        /// <summary>
        /// <c>SteeringCamArrive</c> halts outright when a step is more than this many times the
        /// running average step — a hitch guard (<c>1001dc7c</c>).
        /// </summary>
        public const float HitchStepRatio = 10f;

        // ---- factory settings (FixedThird factory 1001faa1) -----------------

        /// <summary>
        /// The values the stock factory applies after construction (<c>1001faa1</c>). Mass 20 — not
        /// the <c>Vehicle_t</c> default of 50 — and notably falling and surface hug both off, so the
        /// camera has no gravity and is free to move vertically.
        /// </summary>
        public void ApplyStockCameraSettings()
        {
            Mass = 20f;
            Velocity = Vec3.Zero;
            MaxForce = 600f;
            MaxVel = 15f;
            // NOTE: stock's factory calls SetRadius(0.7), which writes +0x4c — but
            // EnsureSurfaceAlignment overwrites that same field from the surface on EVERY call
            // (1000d1f7), and it is called every sub-step, so 0.7 survives only until the first
            // physics step and is never what the occlusion probe actually sees. This port does not
            // implement EnsureSurfaceAlignment, so leaving it at 0.7 pins the probe 0.7 m beyond
            // the camera and makes occlusion fire far more eagerly than stock. Keep the
            // constructor's 0.01 until the surface query is recovered (Docs/Camera.md §8).
            SlowingDistance = 15f;    // SetBrakeDistance(15) writes +0x40
            MaxSubStep = CameraSubStep;     // 0.05, from the CameraVehicle_t ctor (1001d54f)
            DisableFalling();
            DisableSurfaceHug();
        }

        // ---- target ---------------------------------------------------------

        /// <summary>
        /// Whether <c>GetSurface()</c> returns anything. Stock gates the occlusion solve on it
        /// (<c>1001f3d1</c>); <c>CameraVehicleFirstPerson_t::GetSurface</c> returns null
        /// (<c>10007572</c>), which is why first person never runs it.
        /// </summary>
        public bool HasSurface = true;

        /// <summary>+0x160, the monitored target's world position.</summary>
        public Vec3 TargetPosition;

        /// <summary>+0x16c, the monitored target's rotation.</summary>
        public Quat TargetRotation = Quat.Identity;

        /// <summary>+0x180, the eye offset in the target's local space (head height and so on).</summary>
        public Vec3 EyeTargetLocalPos;

        /// <summary>+0x198, the working follow distance, clamped by <see cref="Update"/>.</summary>
        public float FollowDistance = DefaultFollowDistance;

        /// <summary>
        /// +0x1b8, the zoom rate; non-zero diverts steering to <see cref="ZoomSteer"/>. Positive
        /// zooms <b>in</b> (toward the character). Set through <see cref="Forward"/>.
        /// </summary>
        public float ZoomRate;

        /// <summary>Closest the camera may zoom — stock compares 0.49, i.e. 0.7 squared (<c>1001dba8</c>).</summary>
        public const float MinZoomDistance = 0.7f;

        /// <summary>Furthest the camera may zoom — stock compares 625.0, i.e. 25 squared (<c>1001db9a</c>).</summary>
        public const float MaxZoomDistance = 25f;

        /// <summary>
        /// The driver stops a zoom once less than this much distance is left to cover
        /// (<c>1002296a</c>).
        /// </summary>
        public const float ZoomStopThreshold = 0.3f;

        /// <summary>
        /// Below this distance the driver stops servicing an inward zoom entirely
        /// (<c>10022838</c>).
        /// </summary>
        public const float ZoomNearGate = 0.8f;

        /// <summary>
        /// <c>n3Camera_t +0x204</c> — how much distance the pending zoom still has to cover. This is
        /// a <b>distance remaining</b>, not a speed: the driver decrements it by the distance the
        /// camera actually travelled each frame.
        /// </summary>
        public float ZoomPending { get; private set; }

        /// <summary><c>n3Camera_t +0x208</c> — the camera's distance last frame.</summary>
        float _previousZoomDistance;

        /// <summary>
        /// Stock never needs this: its driver runs every frame from the moment the camera exists, so
        /// <c>+0x208</c> always holds last frame's distance by the time anyone scrolls. Here a zoom
        /// can be requested before the first tick, and an unseeded zero would be folded in as a huge
        /// bogus delta.
        /// </summary>
        bool _zoomDistanceSeeded;

        /// <summary>
        /// <c>CameraVehicle_t::Forward(float)</c> (<c>1001d73e</c>) — despite the name this is the
        /// zoom control; it just stores the rate. The driver calls it through vftable <c>+0x1c</c>
        /// (<c>10022975</c>).
        /// </summary>
        public virtual void Forward(float rate) => ZoomRate = rate;

        /// <summary>Queue a zoom of <paramref name="delta"/> metres. Positive zooms in.</summary>
        public void RequestZoom(float delta) => ZoomPending += delta;

        /// <summary>
        /// Orbit only re-seats the goal when the camera is slower than this (<c>1002158d</c>,
        /// comparing <c>Speed</c> against the double at <c>1003e2e0</c> = 0.02).
        /// </summary>
        public const float OrbitResyncSpeed = 0.02f;

        /// <summary>Zoom speed factor, <c>N3 1005c03c</c> = 3.0 (read-only, never written).</summary>
        public const float ZoomSpeed = 3f;

        /// <summary>Mode 3 clamps the zoomed distance to this floor (<c>100227a1</c>).</summary>
        public const float ZoomDirectMinDistance = 0.78f;

        /// <summary>Mode 3 clamps the zoomed distance to this ceiling (<c>100227b4</c>).</summary>
        public const float ZoomDirectMaxDistance = 25f;

        /// <summary>Above this much remaining, zoom moves proportionally rather than at a fixed rate.</summary>
        public const float ZoomProportionalThreshold = 1f;

        /// <summary>Lets <see cref="UpdateZoom"/> reset the pending zoom from a subclass.</summary>
        protected void ClearZoomPending() => ZoomPending = 0f;

        /// <summary>Lets a subclass spend down the pending zoom.</summary>
        protected void SpendZoomPending(float amount) => ZoomPending -= amount;

        /// <summary>
        /// Below this distance <c>CameraVehicleFixedThird_t::VetoForward</c> starts blending the
        /// camera's facing toward the character's own (<c>1001f26e</c>).
        /// </summary>
        public const float VetoBlendDistance = 0.9f;

        /// <summary>
        /// <c>CameraVehicle_t::VetoForward</c> (<c>1001df85</c>) — which way the camera faces:
        /// straight at the look target, or the reference forward if it is sitting on top of it.
        ///
        /// The vetoes are not position constraints, despite the name. They are called from the body
        /// orientation update (<c>Vehicle.dll FUN_1000c616</c> orientation mode 3, which is the
        /// camera's) and decide only the rotation.
        /// </summary>
        public virtual bool VetoForward(out Vec3 forward)
        {
            forward = GetLookTargetPos() - Position;

            if (forward.IsZero)
                forward = Vec3.ReferenceForward;
            else
                forward = forward / forward.Length;

            return true;
        }

        /// <summary>
        /// <c>CameraVehicle_t::VetoUpAlignment</c> (<c>1001dfde</c>) — world up, orthogonalised
        /// against the forward, falling back to the reference forward when the camera looks straight
        /// up or down. Note it <b>discards</b> the surface normal the caller passed in: the camera is
        /// never banked by the ground.
        /// </summary>
        public virtual bool VetoUpAlignment(out Vec3 up)
        {
            up = Vec3.ReferenceUp;

            VetoForward(out Vec3 forward);
            if (forward.IsZero)
                return true;

            // Looking straight up or down leaves no usable up; stock swaps in the reference forward.
            if (forward.X == 0f && forward.Z == 0f)
                up = Vec3.ReferenceForward;

            up -= forward * Vec3.Dot(up, forward);

            float len = up.Length;
            if (len != 0f)
                up = up / len;

            return true;
        }

        /// <summary>
        /// <c>CameraVehicle_t::ForcedUpdate(bool)</c> (<c>1001e5ac</c>) — adopt the camera's current
        /// position as its preferred one. <c>true</c> also adopts the distance, which is how a
        /// finished zoom is committed.
        /// </summary>
        public virtual void ForcedUpdate(bool recalcDistance) { }

        /// <summary>
        /// The driver's zoom state machine for camera modes <b>other than 3</b>
        /// (<c>10022808</c>-<c>1002298d</c>): it steers the zoom through <see cref="ZoomSteer"/>,
        /// folding the distance actually covered back into <see cref="ZoomPending"/> each frame.
        ///
        /// Mode 3 — the one Lost Eden runs — does not come through here; it moves the camera
        /// directly, see <c>CameraVehicleFixedThirdSim.UpdateZoom</c>.
        /// </summary>
        public virtual void UpdateZoom(float dt)
        {
            float distance = (GetLookTargetPos() - Position).Length;

            if (!_zoomDistanceSeeded)
            {
                _previousZoomDistance = distance;
                _zoomDistanceSeeded = true;
            }

            // An inward zoom is not serviced at all once the camera is inside the near gate.
            if (distance < ZoomNearGate && ZoomPending > 0f)
                return;

            if (ZoomPending != 0f)
            {
                bool wasPositive = ZoomPending > 0f;
                ZoomPending = (distance - _previousZoomDistance) + ZoomPending;

                if ((ZoomPending > 0f) == wasPositive
                    && Math.Abs(ZoomPending) >= ZoomStopThreshold)
                {
                    Forward(ZoomPending);
                }
                else
                {
                    ZoomPending = 0f;
                    Forward(0f);
                    Halt();
                    ForcedUpdate(true);
                }
            }

            _previousZoomDistance = distance;
        }

        /// <summary>
        /// <c>CameraVehicle_t::ZoomSteer</c> (<c>1001db64</c>). Seeks a point placed along the
        /// camera-to-head direction, scaled by twice the rate — so a positive rate puts the goal past
        /// the character and the camera closes in, a negative one puts it behind the camera and it
        /// backs off.
        ///
        /// Stock compares <b>squared</b> distances against 0.49 and 625, which is why the limits come
        /// out as exactly 0.7 m and 25 m.
        /// </summary>
        public SteeringResult ZoomSteer(float rate, out Vec3 steer)
        {
            Vec3 lookTarget = GetLookTargetPos();
            Vec3 toLook = lookTarget - Position;
            float d2 = toLook.LengthSquared;

            bool canZoomIn = d2 > MinZoomDistance * MinZoomDistance && rate > 0f;
            bool canZoomOut = d2 < MaxZoomDistance * MaxZoomDistance && rate < 0f;

            if (!canZoomIn && !canZoomOut)
                return SteeringHalt(out steer);

            return SteeringSeek(lookTarget + toLook * (rate + rate), out steer);
        }

        /// <summary>
        /// <c>GetLookTargetPos</c> (<c>1001d890</c>) — the point the camera looks at and orbits:
        /// the target's position plus its local eye offset rotated into world space.
        /// </summary>
        public Vec3 GetLookTargetPos() => TargetPosition + TargetRotation * EyeTargetLocalPos;

        /// <summary>
        /// <c>CameraVehicle_t::Update</c> (<c>1001e54f</c>) — measures how far the camera actually is
        /// from what it is looking at and adopts that as the follow distance, floored at
        /// <see cref="MinFollowDistance"/>.
        ///
        /// <b>This is not a per-frame tick, despite the name.</b> The only caller is the camera
        /// driver at <c>10022445</c>, immediately after it has forcibly moved the camera with
        /// <c>SetRelPosIgnoreCollision</c> — it re-syncs the stored distance to where the camera was
        /// just put. Calling it every frame would feed the camera's current distance back into the
        /// goal that determines that distance, and the camera would collapse onto the character.
        /// </summary>
        public virtual void ResyncFollowDistance()
        {
            float d = (GetLookTargetPos() - Position).Length;
            FollowDistance = d < MinFollowDistance ? MinFollowDistance : d;
        }

        /// <summary>
        /// <c>UpdateMotionConstraints(float)</c> (<c>1001e602</c>). Re-derives the speed and force
        /// limits every tick from how far behind the camera is, so it catches up after a teleport or
        /// a zone-in instead of crawling.
        ///
        /// <c>maxForce</c> is <c>mass * maxVel / 0.3</c> and <c>brakeDistance</c> is
        /// <c>maxVel * 0.3</c> — both fall out of "reach top speed in 0.3 s".
        /// </summary>
        /// <param name="baseSpeed">The configured camera speed; stock passes the preference.</param>
        public void UpdateMotionConstraints(float baseSpeed)
        {
            // The distance is the CAMERA's own distance from what it is looking at — stock calls
            // vftable +0x10, which is Vehicle_t::GetGlobalPos (1001e60d). Measuring the character's
            // body instead yields the constant eye-offset length, which silently disables the
            // catch-up curve and lets the camera trail metres behind a walking character.
            float distance = (GetLookTargetPos() - Position).Length;

            if (distance > CatchUpDistance)
            {
                float boost = (float)Math.Sqrt(distance - CatchUpFalloff) + 1f;
                float boosted = boost * baseSpeed;
                baseSpeed = boosted <= MaxCatchUpSpeed ? boosted : MaxCatchUpSpeed;
            }

            if (baseSpeed < MinCatchUpSpeed)
                baseSpeed = MinCatchUpSpeed;

            MaxVel = baseSpeed;
            MaxForce = (Mass * baseSpeed) / ForceReachTime;
            SlowingDistance = (baseSpeed * baseSpeed * Mass) / MaxForce;   // == baseSpeed * 0.3
        }

        /// <summary>
        /// One frame of the stock camera driver (<c>FUN_10022345</c>, <c>n3Camera_t</c>'s Run), in
        /// the order it does it: re-stamp mass, derive the base speed from how fast the character is
        /// moving, re-derive the motion constraints, then integrate.
        ///
        /// Stock also re-stamps brake distance 8 and max force 1800 here (<c>10022403</c>,
        /// <c>1002240f</c>); both are dead stores, overwritten moments later by
        /// <see cref="UpdateMotionConstraints"/>, so they are not reproduced.
        /// </summary>
        /// <param name="characterMaxSpeed">
        /// The followed character's <b>maximum</b> speed, not its current one. Stock reads the
        /// control dynel's vehicle and takes <c>Vehicle_t +0x3c</c> — max velocity — at
        /// <c>10022404</c> (<c>mov ecx,[edi+0x50]; fld [ecx+0x3c]</c>). Passing the current speed
        /// makes the camera crawl at the 2 m/s floor whenever the character stands still, which
        /// shows up as an orbit that takes ten seconds to catch up with a mouse flick.
        /// </param>
        public virtual void Tick(float dt, float characterMaxSpeed)
        {
            Mass = 20f;

            float baseSpeed = characterMaxSpeed * BaseSpeedFromCharacterSpeed;
            if (baseSpeed > MaxBaseSpeed)
                baseSpeed = MaxBaseSpeed;

            UpdateMotionConstraints(baseSpeed);
            UpdateZoom(dt);
            Run(dt);
        }

        /// <summary>
        /// +0x1c0..+0x1c8, the obstacle-avoidance nudge <c>CalculateSensorSteerDir</c>
        /// (<c>1001d955</c>) computes. <b>Seam.</b> Stock derives it from a locality query
        /// (<c>FUN_1002046f</c>) that has not been read; zero means "no obstacle", which makes
        /// Trail behave like a plain arrive.
        /// </summary>
        public Vec3 SensorSteerDir;

        /// <summary>+0x1bc. Set when the sensors report the view blocked (<c>UpdateSensors</c>).</summary>
        public bool IsBlind;

        /// <summary>
        /// +0x1a4, the direct-control throttle <see cref="DoDirectControl"/> reads. Zero unless a
        /// direct camera-fly control is active.
        /// </summary>
        public float DirectControlThrottle;

        /// <summary>
        /// <c>CameraVehicle_t::DoDirectControl</c> (<c>1001d6e5</c>) — when a direct throttle is
        /// set, drive the camera forward or back and skip the normal steering entirely.
        /// </summary>
        protected SteeringResult DoDirectControl(out Vec3 steer)
        {
            if (DirectControlThrottle == 0f)
            {
                steer = Vec3.Zero;
                return SteeringResult.None;
            }

            return DirectControlThrottle < 0f
                ? SteeringReverse(out steer)
                : SteeringForward(out steer);
        }

        /// <summary>
        /// <c>CameraVehicle_t::CalcSteering</c> (<c>1001e797</c>) — the <b>Trail</b> brain
        /// (<see cref="CameraViewMode.Trail"/>), in stock's priority order: a bound attractor wins,
        /// then a pending zoom, then direct control, then arrive at the desired point.
        ///
        /// The desired point is the look target pushed back along the current camera direction by
        /// the follow distance, raised by 0.4 when the camera is below the look target
        /// (<c>1001eb24</c>) or the sensors report something within 0.4 (<c>1001eb0a</c>), plus the
        /// sensor nudge.
        /// </summary>
        protected override SteeringResult CalcSteering(out Vec3 force)
        {
            if (ZoomRate != 0f)
                return ZoomSteer(ZoomRate, out force);

            SteeringResult direct = DoDirectControl(out force);
            if (direct != SteeringResult.None)
                return direct;

            Vec3 lookTarget = GetLookTargetPos();
            Vec3 toCamera = Position - lookTarget;

            float len = toCamera.Length;
            Vec3 dir = len > 1e-6f
                ? toCamera / len
                : TargetRotation * Vec3.ReferenceForward * -1f;

            Vec3 desired = lookTarget + dir * FollowDistance;

            // Below the look target: lift the goal (1001eb3c).
            if (Position.Y < lookTarget.Y)
                desired.Y += 0.4f;

            if (!IsBlind)
                desired += SensorSteerDir;

            return SteeringCamArrive(desired, out force, ArriveHaltRadius);
        }

        // ---- SteeringCamArrive ----------------------------------------------

        float _smoothedStep;
        bool _smoothedStepSeeded;

        /// <summary>
        /// <c>CameraVehicle_t::SteeringCamArrive</c> (<c>1001dc46</c>) — <c>SteeringArrive</c> with
        /// two camera-specific additions:
        ///
        /// <para>A <b>hitch guard</b>: it keeps a running half-and-half average of the step size and
        /// halts outright if the current step is more than ten times it, so a loading spike parks
        /// the camera instead of slinging it. It measures the <b>sub-step</b>, not the frame, so with
        /// the camera's 0.05 s cap an ordinary frame-rate drop never trips it — only a collapse from
        /// a high baseline does.</para>
        ///
        /// <para>A <b>swing-around</b>: when the camera is closer to what it is looking at than to
        /// where it is trying to get, the target is over a metre away, and the two planar directions
        /// are nearly collinear (|sin| &lt; 0.4) and pointing the same way, it pushes the goal
        /// sideways so the camera arcs around the character rather than straight through them.</para>
        /// </summary>
        public SteeringResult SteeringCamArrive(Vec3 target, out Vec3 steer, float haltRadius)
        {
            float step = DeltaTimeNow;

            if (!_smoothedStepSeeded)
            {
                _smoothedStep = step;
                _smoothedStepSeeded = true;
            }

            if (_smoothedStep * HitchStepRatio < step)
                return SteeringHalt(out steer);

            _smoothedStep = _smoothedStep * 0.5f + step * 0.5f;

            Vec3 lookTarget = GetLookTargetPos();

            // Both compared flat — the swing-around is a planar decision (1001dc f2).
            Vec3 toLook = lookTarget - Position;
            toLook.Y = 0f;
            Vec3 toTarget = target - Position;
            toTarget.Y = 0f;

            if (!toLook.IsZero && !toTarget.IsZero)
            {
                float lookLen = toLook.Length;
                float targetLen = toTarget.Length;

                Vec3 lookDir = toLook / lookLen;
                Vec3 targetDir = toTarget / targetLen;

                // Y of the cross product is the signed sine between the two planar directions.
                float sin = Vec3.Cross(lookDir, targetDir).Y;

                if (lookLen < targetLen
                    && targetLen > 1f
                    && Math.Abs(sin) < 0.4f
                    && Vec3.Dot(lookDir, targetDir) > 0f)
                {
                    // Perpendicular to the look direction, in the ground plane.
                    var perpendicular = new Vec3(toLook.Z, 0f, -toLook.X);
                    target = lookTarget + perpendicular * (SwingScale(sin) * 0.5f);
                }
            }

            return SteeringArrive(target, out steer, haltRadius);
        }

        /// <summary>
        /// <c>FUN_1001ec3a</c>, the scale the swing-around applies to the perpendicular. Not yet
        /// recovered (Docs/Camera.md §8); the sign of the sine is what decides which way the camera
        /// arcs, so this keeps that and leaves the magnitude at one.
        /// </summary>
        protected virtual float SwingScale(float sin) => sin < 0f ? -1f : 1f;
    }
}
