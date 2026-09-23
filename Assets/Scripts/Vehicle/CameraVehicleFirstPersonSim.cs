using System;

namespace LostEden.Vehicles
{
    /// <summary>
    /// Stock <c>CameraVehicleFirstPerson_t</c> (<c>N3.dll</c>, ctor <c>1001ecaa</c> ordinal 1;
    /// vftable <c>1003e084</c>), built by <c>FUN_1001fb7e</c> for camera mode 0.
    ///
    /// It does <b>no steering at all</b> — <c>CalcSteering</c>, <c>CalcLateralSteering</c> and
    /// <c>CalcTurnSteering</c> are each literally <c>xor eax,eax; ret 4</c> (<c>1001ecf7</c>,
    /// <c>1001ecfc</c>, <c>1001ed01</c>), returning <see cref="SteeringResult.None"/>. The camera is
    /// placed directly at the eye and rotated, which is why first person has none of the lag the
    /// third-person modes have.
    ///
    /// Its <c>GetSurface</c> returns null (a shared <c>return 0</c> stub the linker folded with
    /// <c>n3VisualDynel_t::GetImpactAnim</c> at <c>10007572</c>), so it never runs the occlusion
    /// solve either.
    /// </summary>
    public class CameraVehicleFirstPersonSim : CameraVehicleSim
    {
        /// <summary>
        /// +0x1ec, the mouse angles as a quaternion — yaw about world up, then pitch about local
        /// right (<c>SetRotAngles</c>, <c>1001ef0d</c>).
        /// </summary>
        public Quat RotAngles { get; private set; } = Quat.Identity;

        /// <summary>The yaw last set, in radians. Kept so a delta can be added to it.</summary>
        public float Yaw { get; private set; }

        /// <summary>The pitch last set, in radians.</summary>
        public float Pitch { get; private set; }

        /// <summary>
        /// The camera's world rotation: the character's rotation composed with the mouse angles
        /// (<c>Update</c>, <c>1001ef7d</c>: <c>SetRelRot(combine(+0x1ec, +0x16c))</c>).
        /// </summary>
        public Quat ViewRotation => TargetRotation * RotAngles;

        /// <summary>
        /// <c>SetRotAngles(pitch, yaw)</c> (<c>1001ef0d</c>). Builds yaw about <c>(0,1,0)</c> and
        /// pitch about <c>(1,0,0)</c> and composes them, then re-places the camera.
        /// </summary>
        public void SetRotAngles(float pitchRadians, float yawRadians)
        {
            Pitch = pitchRadians;
            Yaw = yawRadians;

            Quat yaw = Quat.FromAxisAngle(Vec3.ReferenceUp, yawRadians);
            Quat pitch = Quat.FromAxisAngle(new Vec3(1f, 0f, 0f), pitchRadians);

            RotAngles = (yaw * pitch).Normalized;

            PlaceAtEye();
        }

        /// <summary>
        /// <c>SetEyeTargetLocalPos</c> (<c>1001ed86</c>) ends with
        /// <c>SetRelPosRot(GetLookTargetPos(), combine(+0x1ec, +0x16c))</c> — the camera is put
        /// <b>at</b> the look target, i.e. the eye, not behind it.
        /// </summary>
        public void PlaceAtEye()
        {
            SetRelPos(GetLookTargetPos());
            BodyRotation = ViewRotation;
        }

        /// <summary>
        /// <c>VetoForward</c> (<c>1001eddb</c>): the reference forward taken through the mouse
        /// angles and then the character's rotation.
        /// </summary>
        public override bool VetoForward(out Vec3 forward)
        {
            forward = ViewRotation * Vec3.ReferenceForward;
            return true;
        }

        /// <summary>First person never steers (<c>1001ecf7</c>).</summary>
        protected override SteeringResult CalcSteering(out Vec3 force)
        {
            force = Vec3.Zero;
            return SteeringResult.None;
        }

        /// <summary>First person has no surface, so no occlusion solve (<c>10007572</c>).</summary>
        public override void ForcedUpdate(bool recalcDistance) { }

        /// <summary>
        /// The driver still ticks the vehicle, but with nothing to steer the only thing that
        /// matters is keeping the camera glued to the eye as the character moves.
        /// </summary>
        public override void Tick(float dt, float characterMaxSpeed)
        {
            PlaceAtEye();
        }
    }
}
