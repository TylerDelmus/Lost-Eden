using System;
using LostEden.Vehicles;
using Xunit;

namespace LostEden.Vehicle.Tests;

/// <summary>
/// Orbit (left drag) against the steering.
/// Mirrors what <c>N3Camera.ApplyOrbit</c> does, which is stock's <c>FUN_1002118c</c>
/// (Docs/Camera.md §5.5).
/// </summary>
public class CameraOrbitTests
{
    static CameraVehicleFixedThirdSim Camera(bool locked = false)
    {
        var cam = new CameraVehicleFixedThirdSim { Frozen = locked };
        cam.ApplyStockDefaults();
        cam.HasSurface = false;
        cam.EyeTargetLocalPos = new Vec3(0f, 1.8f, 0f);
        cam.TargetPosition = Vec3.Zero;
        cam.Position = new Vec3(0f, 1.8f, -4.743f);
        cam.RecalcOptimalPos();          // N3Camera.ApplyViewMode seeds this
        return cam;
    }

    static void Walk(CameraVehicleFixedThirdSim cam, float speed, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            cam.TargetPosition += new Vec3(0f, 0f, speed / 60f);
            cam.Tick(1f / 60f, 5f);
        }
    }

    static float Distance(CameraVehicleFixedThirdSim cam)
        => (cam.Position - cam.GetLookTargetPos()).Length;

    /// <summary>
    /// The orbit N3Camera performs. Which offset is rotated depends on the mode: Lock substitutes the
    /// vehicle's <c>OptimalPos</c> (<c>+0x1ec</c>) for the camera's own position (<c>1002122f</c>),
    /// the other third-person modes keep the camera's current offset (the <c>jne</c> at
    /// <c>1002122d</c> skips the substitution).
    /// </summary>
    static void Orbit(CameraVehicleFixedThirdSim cam, float yawDegrees, bool locked)
    {
        Vec3 lookTarget = cam.GetLookTargetPos();
        Vec3 offset = locked && !cam.GetOptimalPos().IsZero
            ? cam.GetOptimalPos() - lookTarget
            : cam.Position - lookTarget;
        float distance = offset.Length;
        if (distance < 1e-4f)
            return;

        float r = yawDegrees * MathF.PI / 180f;
        float cos = MathF.Cos(r), sin = MathF.Sin(r);
        var rotated = new Vec3(
            offset.X * cos + offset.Z * sin,
            offset.Y,
            -offset.X * sin + offset.Z * cos);

        if (distance > CameraVehicleSim.MaxZoomDistance)
            rotated = rotated / distance * CameraVehicleSim.MaxZoomDistance;

        Vec3 target = lookTarget + rotated;
        cam.SetRelPos(target);

        if (locked)
        {
            // No ForcedUpdate: stock's is gated on the driver's third argument being non-zero
            // (1002157d) and MouseCameraControl passes a hard 0.0 (10021727).
            cam.UpdateHeadingToPos(target, false);
            return;
        }

        if (cam.Speed < CameraVehicleSim.OrbitResyncSpeed)
        {
            cam.ResyncFollowDistance();
            cam.ForcedUpdate(false);
        }
    }

    /// <summary>
    /// Rotating at the <i>preferred</i> distance instead of the current one yanks a lagging camera
    /// inward the instant you touch the mouse. In Trail and Rubber stock rotates the camera's current
    /// offset (<c>1002122d</c> skips Lock's substitution).
    /// </summary>
    [Fact]
    public void Orbit_WhileWalking_DoesNotChangeTheDistance()
    {
        var cam = Camera();
        Walk(cam, 4f, 400);

        float before = Distance(cam);
        Assert.True(before > cam.FollowDistance + 0.5f,
            $"the camera should be lagging for this test to mean anything: {before}");

        Orbit(cam, 15f, locked: false);

        Assert.Equal(before, Distance(cam), 3);
    }

    /// <summary>
    /// And repeated drags must not walk the distance outward. Re-seating the goal while the camera
    /// is moving does exactly that, because <c>UpdateHeadingToPos</c> divides by the follow distance
    /// rather than the offset's own length (<c>1001f6a3</c>).
    /// </summary>
    [Fact]
    public void Orbit_RepeatedWhileWalking_DoesNotRatchetTheDistanceOutward()
    {
        var cam = Camera();
        Walk(cam, 4f, 400);
        float baseline = Distance(cam);

        for (int drag = 0; drag < 8; drag++)
        {
            Orbit(cam, 15f, locked: false);
            Walk(cam, 4f, 120);
        }

        Assert.True(MathF.Abs(Distance(cam) - baseline) < 0.2f,
            $"distance drifted from {baseline} to {Distance(cam)} over 8 drags");
    }

    [Fact]
    public void Orbit_WhileWalking_ActuallyRotatesTheCamera()
    {
        var cam = Camera();
        Walk(cam, 4f, 400);
        Vec3 before = cam.Position - cam.GetLookTargetPos();

        Orbit(cam, 30f, locked: false);
        Vec3 after = cam.Position - cam.GetLookTargetPos();

        float dot = Vec3.Dot(before, after) / (before.Length * after.Length);
        float angle = MathF.Acos(Math.Clamp(dot, -1f, 1f)) * 180f / MathF.PI;
        Assert.True(angle > 20f, $"expected roughly a 30 degree swing, got {angle}");
    }

    [Fact]
    public void Orbit_WhileStopped_HoldsItsDistanceAcrossDrags()
    {
        var cam = Camera();
        Walk(cam, 4f, 300);
        Walk(cam, 0f, 400);            // stop and settle

        Assert.True(cam.Speed < CameraVehicleSim.OrbitResyncSpeed,
            $"expected the camera to have stopped, speed {cam.Speed}");

        float baseline = Distance(cam);
        for (int drag = 0; drag < 4; drag++)
        {
            Orbit(cam, 20f, locked: false);
            Walk(cam, 0f, 60);
        }

        Assert.Equal(baseline, Distance(cam), 2);
    }

    [Fact]
    public void Orbit_InLockMode_AlsoHoldsItsDistance()
    {
        var cam = Camera(locked: true);
        Walk(cam, 4f, 300);
        float baseline = Distance(cam);

        for (int drag = 0; drag < 4; drag++)
        {
            Orbit(cam, 20f, locked: true);
            Walk(cam, 4f, 60);
        }

        Assert.True(MathF.Abs(Distance(cam) - baseline) < 0.2f,
            $"lock drifted from {baseline} to {Distance(cam)}");
    }


    // ---- the LateUpdate order (Lock, walking while dragging) --------------
    //
    // The reported bug: holding W and left-dragging in Lock slowly zoomed the camera out with no
    // scroll input. PreferredDirection must stay a UNIT vector -- UpdateHeadingToPos divides by
    // FollowDistance rather than by the offset's own length (1001f6a3), so any offset measured at a
    // different range rescales it, and next frame's goal is built from that direction.

    /// <summary>
    /// One N3Camera.LateUpdate, in order. <paramref name="syncBeforeOrbit"/> reproduces the old
    /// ordering, which measured the offset against a look target that had already advanced past the
    /// point the camera was last placed at.
    /// </summary>
    static void Frame(CameraVehicleFixedThirdSim cam, Vec3 target, float yawDegrees, bool syncBeforeOrbit)
    {
        if (syncBeforeOrbit)
            cam.TargetPosition = target;

        Orbit(cam, yawDegrees, locked: true);

        if (!syncBeforeOrbit)
            cam.TargetPosition = target;

        cam.Tick(1f / 60f, 6f);
    }

    static float WorstUnitError(bool syncBeforeOrbit, float yawPerFrame)
    {
        var cam = Camera(locked: true);
        var target = Vec3.Zero;
        float worst = 0f;

        for (int f = 0; f < 600; f++)
        {
            target.Z += 6f / 60f;                 // walking forward at the run speed
            Frame(cam, target, yawPerFrame, syncBeforeOrbit);
            worst = MathF.Max(worst, MathF.Abs(cam.PreferredDirection.Length - 1f));
        }

        return worst;
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1.5f)]
    [InlineData(-3f)]
    public void Orbit_InLock_WhileWalking_KeepsThePreferredDirectionUnitLength(float yawPerFrame)
    {
        Assert.Equal(0f, WorstUnitError(syncBeforeOrbit: false, yawPerFrame), 4);
    }

    [Fact]
    public void Orbit_InLock_WhileWalking_NeverRatchetsOutward()
    {
        // The reported symptom was a zoom OUT, so that is the direction to pin. Inward lag is stock:
        // DecideSnap eases only a tenth of an outward gap per call (1001f537), so a camera the
        // character is closing on settles a little inside its goal and recovers when the drag stops.
        var cam = Camera(locked: true);
        var target = Vec3.Zero;
        float follow = cam.FollowDistance;

        for (int f = 0; f < 600; f++)
        {
            target.Z += 6f / 60f;
            Frame(cam, target, 1.5f, syncBeforeOrbit: false);
            Assert.True(Distance(cam) <= follow + 0.01f,
                $"frame {f}: camera pushed out to {Distance(cam)} from {follow}");
        }
    }

    [Fact]
    public void Orbit_InLock_ReturnsToItsFollowDistanceOnceTheDragStops()
    {
        var cam = Camera(locked: true);
        var target = Vec3.Zero;
        float follow = cam.FollowDistance;

        for (int f = 0; f < 300; f++)
        {
            target.Z += 6f / 60f;
            Frame(cam, target, 1.5f, syncBeforeOrbit: false);
        }

        // keep walking, stop dragging
        for (int f = 0; f < 300; f++)
        {
            target.Z += 6f / 60f;
            cam.TargetPosition = target;
            cam.Tick(1f / 60f, 6f);
        }

        Assert.Equal(follow, Distance(cam), 2);
        Assert.Equal(1f, cam.PreferredDirection.Length, 4);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1.5f)]
    public void TheOldLateUpdateOrderIsWhatRatchetedTheCameraOut(float yawPerFrame)
    {
        // Documents the defect so the ordering cannot quietly regress: syncing the target pose
        // BEFORE the orbit drives PreferredDirection far off unit length.
        Assert.True(WorstUnitError(syncBeforeOrbit: true, yawPerFrame) > 0.5f);
    }

    // ---- the pitch pole (FUN_1002118c's Y guard) -------------------------

    const float PitchLimitY = 0.9999f;

    /// <summary>Mirrors N3Camera.ApplyOrbit's direction maths.</summary>
    static Vec3 Step(Vec3 dir, float yawDegrees, float pitchDegrees)
    {
        if (yawDegrees != 0f)
        {
            float r = yawDegrees * MathF.PI / 180f;
            float c = MathF.Cos(r), sn = MathF.Sin(r);
            dir = new Vec3(dir.X * c + dir.Z * sn, dir.Y, -dir.X * sn + dir.Z * c);
        }

        if (pitchDegrees != 0f)
        {
            Vec3 axis = Vec3.Cross(Vec3.ReferenceUp, dir);
            if (axis.LengthSquared > 1e-8f)
            {
                Vec3 candidate = Quat.FromAxisAngle(
                    axis / axis.Length, pitchDegrees * MathF.PI / 180f) * dir;
                if (MathF.Abs(candidate.Y) <= PitchLimitY)
                    dir = candidate;
            }
        }

        return dir;
    }

    static float Elevation(Vec3 d) => MathF.Asin(Math.Clamp(d.Y, -1f, 1f)) * 180f / MathF.PI;
    static float Azimuth(Vec3 d) => MathF.Atan2(d.X, d.Z) * 180f / MathF.PI;

    static readonly Vec3 StockStart = new Vec3(0f, 0.3162f, -0.9487f);

    /// <summary>
    /// Pitching hard at the sky must stop short of vertical, not cross it. Crossing flips the pitch
    /// axis, which inverts every subsequent input, and landing exactly on the pole degenerates the
    /// axis so both pitch and yaw become no-ops — the camera locks and cannot be recovered.
    /// </summary>
    [Theory]
    [InlineData(-3f)]
    [InlineData(3f)]
    public void Pitch_CannotCrossThePole(float pitchPerStep)
    {
        Vec3 dir = StockStart;
        float startAzimuth = Azimuth(dir);

        for (int i = 0; i < 200; i++)
            dir = Step(dir, 0f, pitchPerStep);

        Assert.True(MathF.Abs(dir.Y) <= PitchLimitY,
            $"direction reached |y|={MathF.Abs(dir.Y)}, past the guard");
        Assert.True(MathF.Abs(Mathf_DeltaAngle(startAzimuth, Azimuth(dir))) < 1f,
            "azimuth flipped, so the direction wrapped over the top");
    }

    [Theory]
    [InlineData(-3f)]
    [InlineData(3f)]
    public void Pitch_CanAlwaysComeBackFromTheLimit(float pitchPerStep)
    {
        Vec3 dir = StockStart;
        for (int i = 0; i < 200; i++)
            dir = Step(dir, 0f, pitchPerStep);

        float atLimit = Elevation(dir);
        for (int i = 0; i < 10; i++)
            dir = Step(dir, 0f, -pitchPerStep);

        float moved = Elevation(dir) - atLimit;
        Assert.True(MathF.Abs(moved) > 25f,
            $"expected to pitch back ~30 degrees, moved {moved}");

        // Direction-agnostic: reversing the input must bring the camera back toward level, whether
        // it was driven at the sky or at the ground.
        Assert.True(MathF.Abs(Elevation(dir)) < MathF.Abs(atLimit) - 25f,
            $"pitched the wrong way: {atLimit} -> {Elevation(dir)}");
    }

    [Theory]
    [InlineData(-3f)]
    [InlineData(3f)]
    public void Yaw_StillWorksAtThePitchLimit(float pitchPerStep)
    {
        Vec3 dir = StockStart;
        for (int i = 0; i < 200; i++)
            dir = Step(dir, 0f, pitchPerStep);

        float before = Azimuth(dir);
        Vec3 yawed = Step(dir, 15f, 0f);

        Assert.Equal(15f, Mathf_DeltaAngle(before, Azimuth(yawed)), 2);
    }

    [Fact]
    public void Pitch_NeverReachesThePoleUnderMixedInput()
    {
        Vec3 dir = StockStart;
        float worst = 0f;

        for (int i = 0; i < 500; i++)
        {
            dir = Step(dir, 7f, -5f);
            worst = MathF.Max(worst, MathF.Abs(dir.Y));

            // The pitch axis must stay usable at every step, or the camera would lock.
            Assert.True(Vec3.Cross(Vec3.ReferenceUp, dir).LengthSquared > 1e-8f,
                $"pitch axis degenerated at step {i}");
        }

        Assert.True(worst <= PitchLimitY, $"max |y| was {worst}");
    }

    static float Mathf_DeltaAngle(float a, float b)
    {
        float d = (b - a) % 360f;
        if (d > 180f) d -= 360f;
        if (d < -180f) d += 360f;
        return d;
    }
}
