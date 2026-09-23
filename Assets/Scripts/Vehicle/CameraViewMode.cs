namespace LostEden.Vehicles
{
    /// <summary>
    /// Stock's camera mode, <c>n3Camera_t +0x1ec</c>, persisted as the integer preference
    /// <c>"PreferredCameraMode"</c> (<c>N3 10020032</c>). The values are stock's; the names are the
    /// ones the game's own options use.
    ///
    /// <c>FUN_10020290</c> is the dispatch that turns a mode into a vehicle, and it is exact:
    /// <code>
    /// mode 0 -> FUN_1001fb7e()           CameraVehicleFirstPerson_t
    /// mode 1 -> FUN_1001f9c9()           CameraVehicle_t   (the base, sensor-steered)
    /// mode 2 -> push 0; FUN_1001faa1(0)  CameraVehicleFixedThird_t(+0x214 = false)
    /// else   -> push 1; FUN_1001faa1(1)  CameraVehicleFixedThird_t(+0x214 = true)
    /// </code>
    ///
    /// The bool lands at <c>+0x214</c>, which <c>CameraVehicleFixedThird_t::CalcSteering</c> checks:
    /// when set it returns <see cref="SteeringResult.None"/> and does not steer at all, and
    /// <c>DecideSnap</c> — whose whole body is gated on the same flag — places the camera rigidly
    /// instead. So <b>2 is the springy one and 3 is the rigid one</b>.
    ///
    /// Note mode 0 is deliberately never written to the preference (<c>10020043</c>), so the game
    /// never starts you in first person.
    /// </summary>
    public enum CameraViewMode
    {
        /// <summary>Camera sits at the eye; rotation is the character's times the mouse angles.</summary>
        FirstPerson = 0,

        /// <summary>
        /// Third person, steered by <c>CameraVehicle_t</c>'s own brain with obstacle sensors. The
        /// only mode the driver runs <c>UpdateSensors</c> for (<c>1002245b</c>).
        /// </summary>
        Trail = 1,

        /// <summary>
        /// Third person, physically steered to the ideal spot by <c>SteeringCamArrive</c> — mass,
        /// max force and arrive damping, so it lags and settles. The springy one.
        /// </summary>
        Rubber = 2,

        /// <summary>
        /// Third person, <b>not</b> steered: <c>CalcSteering</c> returns None and <c>DecideSnap</c>
        /// places the camera directly each time the heading is recomputed. Rigid.
        /// </summary>
        Lock = 3,
    }
}
