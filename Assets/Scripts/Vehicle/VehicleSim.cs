using System;

namespace LostEden.Vehicles
{
    /// <summary>
    /// Stock <c>Vehicle_t</c> (<c>Vehicle.dll</c>, exported; ctor <c>1000ce2f</c> ordinal 34,
    /// <c>Run</c> <c>1000e849</c> ordinal 201, physics step <c>FUN_1000e3d3</c>).
    ///
    /// This is the shared motion core: in stock, the camera and every character are the same kind of
    /// object moved by this one integrator, differing only in the steering hooks they override
    /// (<c>CameraVehicle_t</c> and <c>CharVehicle_t</c> are siblings under <c>DummyVehicle_t</c>).
    /// See <c>Docs/Camera.md</c> §3-§4.
    ///
    /// No Unity dependency so the recovered maths can be asserted from plain unit tests; a
    /// <c>MonoBehaviour</c> binds an instance to a transform and supplies surface alignment.
    /// </summary>
    public class VehicleSim
    {
        // ---- stock statics -------------------------------------------------

        /// <summary><c>Vehicle_t::s_vGravityAccel</c> (<c>1001938c</c>).</summary>
        public const float GravityAccel = -20f;

        /// <summary>
        /// Vertical velocity is clamped to this magnitude every step it is integrated
        /// (<c>1000e54a</c>). Note this is a clamp on the gravity accumulator alone, not on speed.
        /// </summary>
        public const float MaxFallSpeed = 50f;

        /// <summary>
        /// A frame longer than this is dropped whole — no integration at all (<c>1000e3e6</c>).
        /// </summary>
        public const float MaxFrameTime = 4f;

        /// <summary>Speed below which the vehicle counts as stopped (<c>1000e5a9</c>).</summary>
        public const float MovingSpeedEpsilon = 0.001f;

        /// <summary>A turn axis shorter than this is ignored (<c>1000e78f</c>).</summary>
        public const float TurnEpsilon = 0.0001f;

        /// <summary>
        /// The step the rest of the engine sees, stock <c>Vehicle_t::s_vDeltaTimeNow</c>
        /// (<c>1001a148</c>, a private static). Set at the top of every sub-step.
        /// </summary>
        public static float DeltaTimeNow { get; private set; }

        // ---- tunables, with the constructor's defaults ----------------------
        // Offsets are Vehicle_t instance offsets; defaults are from the ctor at 1000ce2f, which
        // ends by calling the init helper FUN_1000c45e(mass 50, maxForce 2, maxVel 2, 0.01, 0.1).

        /// <summary>+0x34. <c>SetMass</c> (<c>1000a0a8</c>) floors this at 0.1.</summary>
        public float Mass
        {
            get => _mass;
            set => _mass = value <= 0f ? 0.1f : value;
        }
        float _mass = 50f;

        /// <summary>+0x38. The steering force is truncated to this before integration.</summary>
        public float MaxForce = 2f;

        /// <summary>+0x3c. Velocity is truncated to this after integration.</summary>
        public float MaxVel = 2f;

        /// <summary>
        /// +0x40, written by <c>SetBrakeDistance</c> (<c>1000a166</c>). The distance over which
        /// <see cref="SteeringArrive"/> scales its desired speed down — the arrive damping.
        /// </summary>
        public float SlowingDistance = 0.1f;

        /// <summary>
        /// +0x48. The radius inside which <see cref="SteeringArrive"/> stops outright, when the
        /// caller does not pass one.
        /// </summary>
        public float HaltRadius;

        /// <summary>
        /// +0x4c, written by <c>SetRadius</c> (<c>1000a159</c>). The body half-extent, also used as
        /// the offset the camera's occlusion probe is pushed out by.
        /// <c>EnsureSurfaceAlignment</c> overwrites it from the surface each step (<c>1000d1f7</c>).
        /// </summary>
        public float NearProbeOffset = 0.01f;

        /// <summary>
        /// +0x104, the sub-step ceiling. Per-vehicle: <c>Vehicle_t</c> and so <c>CharVehicle_t</c>
        /// default to 0.4 s (ctor <c>1000ce2f</c>), <c>CameraVehicle_t</c> lowers it to
        /// <see cref="CameraSubStep"/> (ctor <c>1001d54f</c>).
        ///
        /// This bounds frame-rate divergence, it does not remove it — the integrator is plain
        /// semi-implicit Euler, and at 30 fps and above the 0.4 s cap never binds at all. See
        /// Docs/Camera.md §4.1 for the measured spread.
        /// </summary>
        public float MaxSubStep = 0.4f;

        /// <summary><c>CameraVehicle_t</c>'s tighter cap — at least 20 Hz (<c>1001d54f</c>).</summary>
        public const float CameraSubStep = 0.05f;

        // ---- state ---------------------------------------------------------

        /// <summary>+0x58..+0x60.</summary>
        public Vec3 Position;

        /// <summary>+0x64..+0x6c.</summary>
        public Vec3 Velocity;

        /// <summary>+0x80..+0x8c.</summary>
        public Quat BodyRotation = Quat.Identity;

        /// <summary>+0x94..+0x9c, the force the longitudinal channel accumulates into.</summary>
        public Vec3 SteerForce;

        /// <summary>+0x54, the gravity accumulator, kept apart from <see cref="Velocity"/>.</summary>
        public float VerticalVelocity;

        /// <summary>+0xcc, <c>|Velocity|</c> as of the last force integration.</summary>
        public float Speed { get; protected set; }

        /// <summary>+0xd0..+0xd8.</summary>
        public Vec3 PreviousPosition { get; protected set; }

        /// <summary>
        /// +0x50. <c>EnableFalling</c> (<c>1000c394</c>) / <c>DisableFalling</c> (<c>1000c3b7</c>).
        /// </summary>
        public bool FallingEnabled;

        /// <summary>
        /// +0x51. <c>EnableSurfaceHug</c> (<c>10009f49</c>: <c>mov byte [ecx+0x51], 1</c>) /
        /// <c>DisableSurfaceHug</c> (<c>10009f4e</c>). When on and the vehicle is not airborne, the
        /// integrator pins vertical motion to zero every step — that is what keeps a walker stuck to
        /// the ground. The camera turns it <b>off</b>, which is how it is free to move vertically.
        /// </summary>
        public bool SurfaceHug = true;

        /// <summary>+0x52. Airborne right now, which is what gates gravity (<c>1000e4b7</c>).</summary>
        public bool Airborne;

        /// <summary><c>Speed &gt; </c><see cref="MovingSpeedEpsilon"/> (<c>1000e5a9</c>).</summary>
        public bool IsMoving => Speed > MovingSpeedEpsilon;

        // ---- virtual hooks, one per stock vftable slot ----------------------

        /// <summary>vftable +0x48. <c>Run</c> returns immediately when this is false.</summary>
        protected virtual bool IsRunEnabled() => true;

        /// <summary>
        /// vftable +0x4c. The longitudinal channel: fills <paramref name="force"/> and returns how
        /// the integrator should read it. Only <see cref="SteeringResult.Force"/> is integrated.
        /// </summary>
        protected virtual SteeringResult CalcSteering(out Vec3 force)
        {
            force = Vec3.Zero;
            return SteeringResult.None;
        }

        /// <summary>
        /// vftable +0x50. The lateral channel: a velocity applied straight to position. Only
        /// <see cref="SteeringResult.Lateral"/> is honoured.
        /// </summary>
        protected virtual SteeringResult CalcLateralSteering(out Vec3 lateral)
        {
            lateral = Vec3.Zero;
            return SteeringResult.None;
        }

        /// <summary>
        /// vftable +0x54. The turn channel: an axis whose length is the angular rate. Only
        /// <see cref="SteeringResult.Turn"/> is honoured.
        /// </summary>
        protected virtual SteeringResult CalcTurnSteering(out Vec3 turn)
        {
            turn = Vec3.Zero;
            return SteeringResult.None;
        }

        /// <summary>
        /// <c>Vehicle_t::EnsureSurfaceAlignment</c> (<c>1000d1aa</c>) — the ground clamp and
        /// collision gate. A false return <b>breaks the sub-step loop</b>, which is how stock stops a
        /// vehicle that has been blocked partway through a frame.
        ///
        /// Not yet recovered (Docs/Camera.md §8); the default keeps the vehicle where the integrator
        /// put it so the maths above can be tested on its own.
        /// </summary>
        protected virtual bool EnsureSurfaceAlignment(Vec3 previousPosition, bool onlyOnce) => true;

        /// <summary>vftable +0x58. Called when a blocked move gives up.</summary>
        protected virtual void OnHalt() { }

        /// <summary>
        /// <c>Vehicle_t::Halt</c> (<c>1000a688</c>) — zeroes the motion state. Called by the
        /// integrator when the longitudinal channel returns <see cref="SteeringResult.Halt"/>.
        /// </summary>
        public virtual void Halt()
        {
            Velocity = Vec3.Zero;
            SteerForce = Vec3.Zero;
            Speed = 0f;
        }

        /// <summary>
        /// <c>Vehicle_t::DisableFalling</c> (<c>1000c3b7</c>) — clears the vertical motion and takes
        /// the vehicle out of the air. Note it does <b>not</b> touch <see cref="SurfaceHug"/>.
        /// </summary>
        public void DisableFalling()
        {
            Velocity.Y = 0f;
            VerticalVelocity = 0f;
            Airborne = false;
            Speed = Velocity.Length;
        }

        /// <summary><c>Vehicle_t::DisableSurfaceHug</c> (<c>10009f4e</c>).</summary>
        public void DisableSurfaceHug() => SurfaceHug = false;

        /// <summary><c>Vehicle_t::EnableSurfaceHug</c> (<c>10009f49</c>).</summary>
        public void EnableSurfaceHug() => SurfaceHug = true;

        /// <summary>
        /// <c>Vehicle_t::SetVel</c> (<c>1000a4b1</c>). Sets the velocity <b>and</b> recomputes
        /// <see cref="Speed"/> — assigning <see cref="Velocity"/> on its own leaves the stored speed
        /// stale, and the integrator gates translation on the stored speed, not on the vector.
        /// </summary>
        public void SetVel(Vec3 velocity)
        {
            Velocity = velocity;
            Speed = velocity.Length;
        }

        /// <summary>
        /// <c>Vehicle_t::SetRelPos</c> (<c>1000e21d</c>) — put the vehicle at a position <b>now</b>,
        /// with collision, bypassing steering entirely. This is how stock does anything that has to
        /// track input one-to-one, mouse-look orbit above all: the camera is moved, not steered at.
        /// </summary>
        public void SetRelPos(Vec3 position)
        {
            if (position.Equals(Position))
                return;

            Vec3 previous = Position;
            Position = position;
            PreviousPosition = position;    // stock stores the new position here, not the old
            EnsureSurfaceAlignment(previous, false);
        }

        // ---- the tick ------------------------------------------------------

        /// <summary>
        /// <c>Vehicle_t::Run(float)</c> (<c>1000e849</c>), physics path only. Returns whether the
        /// vehicle moved. The pending-relative-move and functional-path branches of stock's
        /// <c>Run</c> are not ported yet (Docs/Camera.md §4).
        /// </summary>
        public bool Run(float dt)
        {
            if (!IsRunEnabled())
                return false;

            return Step(dt);
        }

        /// <summary>
        /// <c>FUN_1000e3d3</c> — the sub-stepping physics loop. The step is capped at
        /// <see cref="MaxSubStep"/> and the loop runs as many times as it takes to consume
        /// <paramref name="dt"/>. That bounds how far a long frame can diverge; it does not make the
        /// result frame-rate independent (Docs/Camera.md §4.1).
        /// </summary>
        protected bool Step(float dt)
        {
            // A frame this long is dropped entirely — stock does not try to catch up (1000e3e6).
            if (dt > MaxFrameTime || dt <= 0f)
                return false;

            bool moved = false;
            float elapsed = 0f;

            do
            {
                float step = dt - elapsed;
                if (step > MaxSubStep)
                    step = MaxSubStep;

                DeltaTimeNow = step;

                Vec3 stepStartPosition = Position;

                // 1. Longitudinal steering into the force accumulator.
                SteeringResult longitudinal = CalcSteering(out Vec3 force);
                SteerForce = force;
                if (longitudinal == SteeringResult.Halt)
                {
                    SteerForce = Vec3.Zero;
                    Halt();
                }
                else if (longitudinal != SteeringResult.Force)
                {
                    SteerForce = Vec3.Zero;
                }

                // 2. Gravity. Only accumulates while airborne; when grounded and falling is enabled,
                //    both the accumulator and the vertical component of velocity are cleared.
                if (Airborne)
                {
                    VerticalVelocity += GravityAccel * step;
                }
                else if (SurfaceHug)
                {
                    VerticalVelocity = 0f;
                    Velocity.Y = 0f;
                }

                // 3. Force -> velocity. Runs when the steering asked for it or gravity is in play.
                if (longitudinal == SteeringResult.Force || Airborne)
                {
                    Vec3 f = SteerForce;
                    Vec3.Truncate(ref f, MaxForce);

                    Velocity += (f * step) / Mass;

                    if (VerticalVelocity > MaxFallSpeed)
                        VerticalVelocity = MaxFallSpeed;
                    else if (VerticalVelocity < -MaxFallSpeed)
                        VerticalVelocity = -MaxFallSpeed;

                    Vec3 v = Velocity;
                    Speed = Vec3.Truncate(ref v, MaxVel);
                    Velocity = v;
                }

                bool moving = IsMoving;
                Vec3 velocityThisStep = Velocity;

                // 4. Lateral steering, applied straight to position.
                SteeringResult lateralResult = CalcLateralSteering(out Vec3 lateral);
                if (lateralResult == SteeringResult.Halt)
                {
                    lateral = Vec3.Zero;
                }
                else if (lateralResult == SteeringResult.Lateral)
                {
                    if (!moving)
                    {
                        Vec3.Truncate(ref lateral, MaxVel);
                    }
                    else
                    {
                        // Strafing redirects, it never adds speed: scale the velocity and the
                        // lateral vector together so their sum keeps the current speed (1000e690).
                        float combined = (velocityThisStep + lateral).Length;
                        if (combined > 0f)
                        {
                            float k = Speed / combined;
                            velocityThisStep *= k;
                            lateral *= k;
                        }
                    }

                    Position += lateral * step;
                    moved = true;
                }

                // 5. Translate.
                if (moving || VerticalVelocity != 0f)
                {
                    Position += velocityThisStep * step;
                    Position.Y += VerticalVelocity * step;
                    moved = true;
                }

                // 6. Turn steering. Rotates the body while standing still, the velocity while moving
                //    — so a moving vehicle curves its path rather than sliding sideways (1000e7c6).
                SteeringResult turnResult = CalcTurnSteering(out Vec3 turn);
                if (turnResult == SteeringResult.Halt)
                {
                    turn = Vec3.Zero;
                }
                else if (turnResult == SteeringResult.Turn)
                {
                    float rate = turn.Length;
                    if (rate > TurnEpsilon)
                    {
                        Vec3 axis = turn / rate;
                        float angle = rate * step;
                        Quat rotation = Quat.FromAxisAngle(axis, angle);

                        if (Velocity.X == 0f && Velocity.Z == 0f)
                            BodyRotation = (rotation * BodyRotation).Normalized;
                        else
                            Velocity = rotation * Velocity;
                    }

                    moved = true;
                }

                PreviousPosition = stepStartPosition;

                if (!EnsureSurfaceAlignment(stepStartPosition, false))
                    break;

                elapsed += step;
            }
            while (elapsed < dt);

            return moved;
        }

        // ---- steering behaviours -------------------------------------------

        /// <summary>
        /// <c>Vehicle_t::SteeringHalt</c> (<c>1000a2de</c>, ordinal 227). Asks the integrator to stop
        /// this channel.
        /// </summary>
        public SteeringResult SteeringHalt(out Vec3 steer)
        {
            steer = Vec3.Zero;
            return SteeringResult.Halt;
        }

        /// <summary>
        /// <c>Vehicle_t::SteeringForward</c> (<c>1000ca73</c>, ordinal 226) — full max force along
        /// the body's forward. Note it returns the force already at <see cref="MaxForce"/>, so the
        /// integrator's clamp is exactly saturated.
        /// </summary>
        public SteeringResult SteeringForward(out Vec3 steer)
        {
            steer = GetBodyForward() * MaxForce;
            return SteeringResult.Force;
        }

        /// <summary><c>Vehicle_t::SteeringReverse</c> (<c>1000cab3</c>, ordinal 228).</summary>
        public SteeringResult SteeringReverse(out Vec3 steer)
        {
            steer = GetBodyForward() * -MaxForce;
            return SteeringResult.Force;
        }

        /// <summary>
        /// <c>Vehicle_t::GetBodyForward</c> (<c>1000c5f3</c>) — the body's forward, i.e. the
        /// reference forward taken through <see cref="BodyRotation"/>.
        /// </summary>
        public Vec3 GetBodyForward() => BodyRotation * Vec3.ReferenceForward;

        /// <summary>
        /// <c>Vehicle_t::SteeringSeek</c> (<c>1000a87c</c>, ordinal 229) — go at it flat out. Unlike
        /// <see cref="SteeringArrive"/> there is no slow-down and no brake distance, and the result
        /// is scaled by <see cref="MaxForce"/> rather than by mass, so it saturates the integrator's
        /// clamp immediately.
        /// </summary>
        public SteeringResult SteeringSeek(Vec3 target, out Vec3 steer)
        {
            steer = Vec3.Zero;

            Vec3 toTarget = target - Position;
            float len = toTarget.Length;
            if (len == 0f)
                return SteeringResult.None;

            Vec3 desired = (toTarget / len) * MaxVel;
            steer = (desired - Velocity) * MaxForce;
            return SteeringResult.Force;
        }

        /// <summary>
        /// <c>Vehicle_t::SteeringArrive</c> (<c>1000ab28</c>, ordinal 223) — the behaviour the camera
        /// steers with. Slows down inside <see cref="SlowingDistance"/> and halts inside
        /// <paramref name="haltRadius"/>.
        ///
        /// The <c>* Mass * 4</c> is the whole acceleration model: aim to reach the desired velocity
        /// in 0.25 s, then convert that acceleration to a force. The integrator's
        /// <see cref="MaxForce"/> clamp is what actually limits how sharply the vehicle responds.
        /// </summary>
        /// <param name="haltRadius">0 means "use <see cref="HaltRadius"/>".</param>
        public SteeringResult SteeringArrive(Vec3 target, out Vec3 steer, float haltRadius = 0f)
        {
            if (haltRadius == 0f)
                haltRadius = HaltRadius;

            Vec3 toTarget = target - Position;
            float d2 = toTarget.LengthSquared;

            // Note stock compares the *squared* distance against both the squared halt radius and
            // the literal 0.01 — the second is a squared-space epsilon, i.e. 0.1 m (1000ab7c).
            if (d2 < haltRadius * haltRadius || d2 < 0.01f)
                return SteeringHalt(out steer);

            float d = (float)Math.Sqrt(d2);

            // The divisor is the SLOWING distance (+0x40), which UpdateMotionConstraints keeps at
            // maxVel * 0.3. Dividing by a small constant instead makes this saturate at maxVel at
            // every distance, so the camera charges its goal flat out and overshoots — a visible
            // wobble while walking.
            float speed = (d / SlowingDistance) * MaxVel;
            if (speed > MaxVel)
                speed = MaxVel;

            Vec3 desired = toTarget * (speed / d);
            steer = (desired - Velocity) * (Mass * 4f);

            // A force this large means the state has blown up; stock declines to steer (1000ac6e).
            return steer.Length <= 1e7f ? SteeringResult.Force : SteeringResult.None;
        }
    }
}
