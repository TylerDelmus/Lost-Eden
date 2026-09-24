using System;
using System.Collections.Generic;
using LostEden.Vehicles;
using Xunit;

namespace LostEden.Vehicle.Tests;

/// <summary>
/// Locks down the recovered camera brain (Docs/Camera.md §5).
/// </summary>
public class CameraVehicleSimTests
{
    static CameraVehicleFixedThirdSim MakeCamera()
    {
        var cam = new CameraVehicleFixedThirdSim();
        cam.ApplyStockDefaults();
        return cam;
    }

    // ---- factory settings (1001faa1) -------------------------------------

    [Fact]
    public void StockSettings_MatchTheFactory()
    {
        var cam = MakeCamera();

        Assert.Equal(20f, cam.Mass);
        Assert.Equal(600f, cam.MaxForce);
        Assert.Equal(15f, cam.MaxVel);
        // 0.01, not SetRadius's 0.7 — see ApplyStockCameraSettings.
        Assert.Equal(0.01f, cam.NearProbeOffset);
        Assert.Equal(0.05f, cam.MaxSubStep);
    }

    /// <summary>
    /// The camera has no gravity and does not stick to the ground — that is what lets it rise and
    /// fall freely as you pitch. Both come from the factory (1001faa1).
    /// </summary>
    [Fact]
    public void StockSettings_TurnOffFallingAndSurfaceHug()
    {
        var cam = MakeCamera();

        Assert.False(cam.Airborne);
        Assert.False(cam.SurfaceHug);
    }

    [Fact]
    public void WithSurfaceHugOff_VerticalVelocityIsNotPinned()
    {
        var cam = MakeCamera();
        cam.SetVel(new Vec3(0f, 3f, 0f));   // SetVel, not Velocity — the stored speed gates motion
        cam.HasSurface = false;
        cam.Frozen = true;                  // no steering, just integrate

        cam.Run(0.05f);

        Assert.True(cam.Position.Y > 0f, "camera should have risen, but surface hug pinned it");
    }

    /// <summary>
    /// The integrator gates translation on the stored speed (+0xcc), which only the force path and
    /// <c>SetVel</c> write — so assigning the velocity vector alone leaves the vehicle stationary.
    /// Surprising, but it is what stock does.
    /// </summary>
    [Fact]
    public void AssigningVelocityWithoutSetVelDoesNotMoveTheVehicle()
    {
        var cam = MakeCamera();
        cam.Velocity = new Vec3(0f, 3f, 0f);
        cam.HasSurface = false;
        cam.Frozen = true;

        cam.Run(0.05f);

        Assert.Equal(0f, cam.Position.Y, 6);
    }

    // ---- the default offset (1001f1d2) -----------------------------------

    [Fact]
    public void DefaultOffset_IsStoredAsADirectionAndADistance()
    {
        var cam = MakeCamera();

        Assert.Equal(MathF.Sqrt(1.5f * 1.5f + 4.5f * 4.5f), cam.FollowDistance, 4);
        Assert.Equal(1f, cam.PreferredDirection.Length, 5);
        Assert.Equal(0f, cam.PreferredDirection.X, 5);
        Assert.True(cam.PreferredDirection.Z < 0f, "the camera sits behind the character");
        Assert.True(cam.PreferredDirection.Y > 0f, "and above it");
    }

    // ---- GetLookTargetPos (1001d890) -------------------------------------

    [Fact]
    public void LookTarget_IsTargetPositionPlusTheEyeOffsetRotatedIntoWorld()
    {
        var cam = MakeCamera();
        cam.TargetPosition = new Vec3(10f, 0f, 5f);
        cam.EyeTargetLocalPos = new Vec3(0f, 1.8f, 0f);
        cam.TargetRotation = Quat.FromAxisAngle(Vec3.ReferenceUp, MathF.PI / 2f);

        Vec3 look = cam.GetLookTargetPos();

        // Rotating a purely vertical offset about Y leaves it unchanged.
        Assert.Equal(10f, look.X, 4);
        Assert.Equal(1.8f, look.Y, 4);
        Assert.Equal(5f, look.Z, 4);
    }

    [Fact]
    public void LookTarget_RotatesAHorizontalEyeOffsetWithTheTarget()
    {
        var cam = MakeCamera();
        cam.TargetPosition = Vec3.Zero;
        cam.EyeTargetLocalPos = new Vec3(0f, 0f, 1f);
        cam.TargetRotation = Quat.FromAxisAngle(Vec3.ReferenceUp, MathF.PI / 2f);

        Vec3 look = cam.GetLookTargetPos();

        Assert.Equal(1f, look.X, 4);
        Assert.Equal(0f, look.Z, 4);
    }

    // ---- Update (1001e54f) -----------------------------------------------

    [Fact]
    public void Resync_AdoptsTheMeasuredDistanceAsTheFollowDistance()
    {
        var cam = MakeCamera();
        cam.TargetPosition = Vec3.Zero;
        cam.EyeTargetLocalPos = Vec3.Zero;
        cam.Position = new Vec3(0f, 0f, -7f);

        cam.ResyncFollowDistance();

        Assert.Equal(7f, cam.FollowDistance, 4);
    }

    [Fact]
    public void Resync_ClampsTheFollowDistanceAtThePointNineMinimum()
    {
        var cam = MakeCamera();
        cam.TargetPosition = Vec3.Zero;
        cam.EyeTargetLocalPos = Vec3.Zero;
        cam.Position = new Vec3(0f, 0f, -0.2f);

        cam.ResyncFollowDistance();

        Assert.Equal(CameraVehicleSim.MinFollowDistance, cam.FollowDistance, 5);
    }

    /// <summary>
    /// The resync is not a per-frame tick. Driving it every frame feeds the camera's own distance
    /// back into the goal that sets that distance, and the camera walks itself onto the
    /// character's head.
    /// </summary>
    [Fact]
    public void Resync_CalledEveryFrameWouldCollapseTheCameraOntoTheCharacter()
    {
        var cam = MakeCamera();
        cam.TargetPosition = Vec3.Zero;
        cam.EyeTargetLocalPos = new Vec3(0f, 1.8f, 0f);
        cam.HasSurface = false;
        cam.Position = new Vec3(0f, 1.8f, 0f);

        for (int i = 0; i < 300; i++)
        {
            cam.ResyncFollowDistance();      // deliberately wrong
            cam.Tick(1f / 60f, 0f);
        }

        Assert.True((cam.Position - cam.GetLookTargetPos()).Length < 1.5f,
            "documents the failure mode; if this ever passes at a larger distance, re-read 1001e54f");
    }

    // ---- UpdateMotionConstraints (1001e602) ------------------------------

    [Fact]
    public void MotionConstraints_DeriveForceAndSlowingDistanceFromThePointThreeReachTime()
    {
        var cam = MakeCamera();
        cam.TargetPosition = Vec3.Zero;
        cam.EyeTargetLocalPos = Vec3.Zero;

        cam.UpdateMotionConstraints(10f);

        Assert.Equal(10f, cam.MaxVel, 4);
        Assert.Equal(20f * 10f / 0.3f, cam.MaxForce, 2);
        Assert.Equal(10f * 0.3f, cam.SlowingDistance, 3);
    }

    [Fact]
    public void MotionConstraints_NeverGoBelowTheMinimumSpeed()
    {
        var cam = MakeCamera();
        cam.TargetPosition = Vec3.Zero;
        cam.EyeTargetLocalPos = Vec3.Zero;

        cam.UpdateMotionConstraints(0.1f);

        Assert.Equal(CameraVehicleSim.MinCatchUpSpeed, cam.MaxVel, 4);
    }

    /// <summary>
    /// The catch-up curve keys off how far <b>the camera</b> is from its look target, so a camera
    /// that has fallen behind speeds up to close the gap.
    /// </summary>
    [Fact]
    public void MotionConstraints_SpeedUpWhenTheCameraHasFallenBehind()
    {
        var cam = MakeCamera();
        cam.TargetPosition = Vec3.Zero;
        cam.EyeTargetLocalPos = Vec3.Zero;
        cam.Position = new Vec3(0f, 0f, -30f);          // 30 m > the 6 m threshold

        cam.UpdateMotionConstraints(10f);

        // sqrt(30 - 5) + 1 = 6, so 60 — under the 80 ceiling.
        Assert.Equal(60f, cam.MaxVel, 3);
    }

    [Fact]
    public void MotionConstraints_DoNotSpeedUpWhenTheCameraIsWhereItShouldBe()
    {
        var cam = MakeCamera();
        cam.TargetPosition = Vec3.Zero;
        cam.EyeTargetLocalPos = Vec3.Zero;
        cam.Position = new Vec3(0f, 0f, -4.74f);        // inside the 6 m threshold

        cam.UpdateMotionConstraints(10f);

        Assert.Equal(10f, cam.MaxVel, 3);
    }

    [Fact]
    public void MotionConstraints_CapTheCatchUpSpeed()
    {
        var cam = MakeCamera();
        cam.TargetPosition = Vec3.Zero;
        cam.EyeTargetLocalPos = Vec3.Zero;
        cam.Position = new Vec3(0f, 0f, -500f);

        cam.UpdateMotionConstraints(15f);

        Assert.Equal(CameraVehicleSim.MaxCatchUpSpeed, cam.MaxVel, 3);
    }

    /// <summary>
    /// The camera's speed comes from the character's <b>max</b> speed, not its current one. Passing
    /// the current speed pins a standing player's camera to the 2 m/s floor, which made orbiting
    /// take seconds per flick.
    /// </summary>
    [Fact]
    public void Tick_UsesCharacterMaxSpeedSoAStandingPlayerStillGetsAFastCamera()
    {
        var cam = MakeCamera();
        cam.TargetPosition = Vec3.Zero;
        cam.EyeTargetLocalPos = Vec3.Zero;
        cam.Position = new Vec3(0f, 0f, -4.743f);
        cam.HasSurface = false;

        cam.Tick(1f / 60f, 5f);        // standing still, but a 5 m/s runner

        Assert.Equal(CameraVehicleSim.MaxBaseSpeed, cam.MaxVel, 3);
    }

    /// <summary>
    /// Mouse-look orbit is one-to-one and instant: stock moves the camera with <c>SetRelPos</c> and
    /// only then syncs the goal (<c>FUN_1002118c</c> at <c>10021524</c>). Setting the goal and
    /// letting the vehicle steer to it instead makes a flick drift for seconds afterwards.
    /// </summary>
    [Fact]
    public void Orbit_MovesTheCameraImmediatelyNotOverTime()
    {
        var cam = MakeCamera();
        cam.TargetPosition = Vec3.Zero;
        cam.EyeTargetLocalPos = Vec3.Zero;
        cam.HasSurface = false;
        cam.StayBehind = false;
        cam.SetPreferredOffset(new Vec3(0f, 0f, -4.743f));
        cam.Position = new Vec3(0f, 0f, -4.743f);

        for (int i = 0; i < 120; i++)
            cam.Tick(1f / 60f, 5f);

        // Swing 90 degrees round to -X, the way the binding does it.
        var swung = new Vec3(-4.743f, 0f, 0f);
        cam.SetRelPos(swung);
        cam.ForcedUpdate(false);

        // Already there, before a single further tick.
        Assert.Equal(-4.743f, cam.Position.X, 3);
        Assert.Equal(0f, cam.Position.Z, 3);

        // And it stays put rather than drifting back.
        for (int i = 0; i < 120; i++)
            cam.Tick(1f / 60f, 5f);

        Assert.True((cam.Position - swung).Length < 0.3f,
            $"expected to hold the new angle, drifted to {cam.Position}");
    }

    // ---- RecalcOptimalPos (1001f371) -------------------------------------

    [Fact]
    public void OptimalPos_IsTheLookTargetPlusTheDirectionTimesTheFollowDistance()
    {
        var cam = MakeCamera();
        cam.TargetPosition = Vec3.Zero;
        cam.EyeTargetLocalPos = Vec3.Zero;
        cam.StayBehind = false;
        cam.SetPreferredOffset(new Vec3(0f, 0f, -5f));

        cam.RecalcOptimalPos();

        Assert.Equal(0f, cam.OptimalPos.X, 4);
        Assert.Equal(-5f, cam.OptimalPos.Z, 4);
    }

    [Fact]
    public void OptimalPos_StayBehindRotatesTheOffsetWithTheTarget()
    {
        var cam = MakeCamera();
        cam.TargetPosition = Vec3.Zero;
        cam.EyeTargetLocalPos = Vec3.Zero;
        cam.StayBehind = true;
        cam.SetPreferredOffset(new Vec3(0f, 0f, -5f));
        cam.TargetRotation = Quat.FromAxisAngle(Vec3.ReferenceUp, MathF.PI / 2f);

        cam.RecalcOptimalPos();

        // Facing +X now, so "behind" is -X.
        Assert.Equal(-5f, cam.OptimalPos.X, 3);
        Assert.Equal(0f, cam.OptimalPos.Z, 3);
    }

    [Fact]
    public void Occlusion_ClearViewLeavesTheSentinelZero()
    {
        var cam = MakeCamera();
        cam.LineOfSight = (a, b) => true;

        cam.RecalcOptimalPos();

        Assert.True(cam.AdjustedPos.IsZero);
        Assert.Equal(0, cam.LastOcclusionIterations);
    }

    /// <summary>
    /// The headline behaviour: a wall behind the character pulls the camera in along the line to the
    /// head, rather than pushing it sideways.
    /// </summary>
    [Fact]
    public void Occlusion_BinarySearchesTheFurthestVisiblePointTowardsTheHead()
    {
        var cam = MakeCamera();
        cam.TargetPosition = Vec3.Zero;
        cam.EyeTargetLocalPos = Vec3.Zero;
        cam.StayBehind = false;
        cam.SetPreferredOffset(new Vec3(0f, 0f, -10f));

        // A wall 3 m behind the head: anything further than that is blocked.
        cam.LineOfSight = (from, to) => to.Length <= 3f;

        cam.RecalcOptimalPos();

        Assert.False(cam.AdjustedPos.IsZero);
        Assert.True(cam.LastOcclusionIterations > 0);
        Assert.True(cam.LastOcclusionIterations <= CameraVehicleFixedThirdSim.OcclusionSearchMaxIterations);

        // The probe is pushed NearProbeOffset beyond the candidate, so it settles that much inside
        // the 3 m wall.
        float distance = cam.AdjustedPos.Length;
        float expected = 3f - cam.NearProbeOffset;
        Assert.True(Math.Abs(distance - expected) < 0.2f,
            $"expected to settle near {expected} m (3 m wall minus the probe offset), got {distance}");
        Assert.True(cam.AdjustedPos.X == 0f && cam.AdjustedPos.Y == 0f,
            "the solve must stay on the line to the head, not slide sideways");
    }

    [Fact]
    public void Occlusion_FullyBlockedCollapsesTowardsTheLowEndOfTheBracket()
    {
        var cam = MakeCamera();
        cam.TargetPosition = Vec3.Zero;
        cam.EyeTargetLocalPos = Vec3.Zero;
        cam.StayBehind = false;
        cam.SetPreferredOffset(new Vec3(0f, 0f, -10f));
        cam.LineOfSight = (from, to) => false;

        cam.RecalcOptimalPos();

        // hi keeps collapsing onto lo = 0.01, so the camera ends up almost on the head.
        Assert.True(cam.AdjustedPos.Length < 0.3f,
            $"expected to collapse onto the head, got {cam.AdjustedPos.Length}");
    }

    [Fact]
    public void Occlusion_IsSkippedEntirelyWithoutASurface()
    {
        var cam = MakeCamera();
        cam.HasSurface = false;
        cam.LineOfSight = (a, b) => throw new InvalidOperationException("must not be queried");

        cam.RecalcOptimalPos();

        Assert.Equal(0, cam.LastOcclusionIterations);
    }

    // ---- UpdateHeadingToPos (1001f660) -----------------------------------

    [Fact]
    public void UpdateHeadingToPos_TurnsAWorldPositionIntoAPreferredDirection()
    {
        var cam = MakeCamera();
        cam.TargetPosition = Vec3.Zero;
        cam.EyeTargetLocalPos = Vec3.Zero;
        cam.StayBehind = false;
        cam.HasSurface = false;
        cam.FollowDistance = 5f;

        cam.UpdateHeadingToPos(new Vec3(5f, 0f, 0f), recalcDistance: false);

        Assert.Equal(1f, cam.PreferredDirection.X, 4);
        Assert.Equal(0f, cam.PreferredDirection.Z, 4);
        Assert.Equal(5f, cam.FollowDistance, 4);
        Assert.Equal(5f, cam.OptimalPos.X, 4);
    }

    [Fact]
    public void UpdateHeadingToPos_CanAdoptTheDistanceToo()
    {
        var cam = MakeCamera();
        cam.TargetPosition = Vec3.Zero;
        cam.EyeTargetLocalPos = Vec3.Zero;
        cam.StayBehind = false;
        cam.HasSurface = false;
        cam.FollowDistance = 5f;

        cam.UpdateHeadingToPos(new Vec3(0f, 0f, -12f), recalcDistance: true);

        Assert.Equal(12f, cam.FollowDistance, 4);
        Assert.Equal(1f, cam.PreferredDirection.Length, 4);
    }

    /// <summary>
    /// With stay-behind on, the direction is stored in the character's frame — so turning the
    /// character carries the camera round with them, which is the whole point.
    /// </summary>
    [Fact]
    public void UpdateHeadingToPos_StoresTheDirectionInTheTargetsFrame()
    {
        var cam = MakeCamera();
        cam.TargetPosition = Vec3.Zero;
        cam.EyeTargetLocalPos = Vec3.Zero;
        cam.StayBehind = true;
        cam.HasSurface = false;
        cam.FollowDistance = 5f;
        cam.TargetRotation = Quat.FromAxisAngle(Vec3.ReferenceUp, MathF.PI / 2f);

        // Character faces +X. Put the camera at world -X, i.e. directly behind them.
        cam.UpdateHeadingToPos(new Vec3(-5f, 0f, 0f), recalcDistance: false);

        // Stored locally that is "behind" = -Z.
        Assert.Equal(-1f, cam.PreferredDirection.Z, 3);
        Assert.Equal(0f, cam.PreferredDirection.X, 3);

        // Now spin the character to face -X; the camera's goal must follow to world +X.
        cam.TargetRotation = Quat.FromAxisAngle(Vec3.ReferenceUp, -MathF.PI / 2f);
        cam.RecalcOptimalPos();

        Assert.Equal(5f, cam.OptimalPos.X, 3);
    }

    [Fact]
    public void UpdateHeadingToPos_ClearsTheOcclusionSentinel()
    {
        var cam = MakeCamera();
        cam.TargetPosition = Vec3.Zero;
        cam.EyeTargetLocalPos = Vec3.Zero;
        cam.StayBehind = false;
        cam.HasSurface = true;
        cam.LineOfSight = (a, b) => true;

        cam.UpdateHeadingToPos(new Vec3(0f, 0f, -5f), recalcDistance: false);

        Assert.True(cam.AdjustedPos.IsZero);
    }

    // ---- zoom (ZoomSteer 1001db64, driver 10022808) ----------------------

    static CameraVehicleFixedThirdSim ZoomRig(float startDistance)
    {
        var cam = MakeCamera();
        cam.TargetPosition = Vec3.Zero;
        cam.EyeTargetLocalPos = Vec3.Zero;
        cam.HasSurface = false;
        cam.StayBehind = false;
        cam.SetPreferredOffset(new Vec3(0f, 0f, -startDistance));
        cam.Position = new Vec3(0f, 0f, -startDistance);
        return cam;
    }

    [Fact]
    public void ZoomSteer_PositiveRateSeeksPastTheHeadSoTheCameraClosesIn()
    {
        var cam = ZoomRig(10f);

        SteeringResult r = cam.ZoomSteer(1f, out Vec3 steer);

        Assert.Equal(SteeringResult.Force, r);
        Assert.True(steer.Z > 0f, $"expected a pull toward +Z (the head), got {steer}");
    }

    [Fact]
    public void ZoomSteer_NegativeRatePushesTheCameraOut()
    {
        var cam = ZoomRig(10f);

        SteeringResult r = cam.ZoomSteer(-1f, out Vec3 steer);

        Assert.Equal(SteeringResult.Force, r);
        Assert.True(steer.Z < 0f, $"expected a push toward -Z (away), got {steer}");
    }

    [Fact]
    public void ZoomSteer_RefusesToZoomInsideTheMinimumDistance()
    {
        var cam = ZoomRig(0.5f);   // inside 0.7

        Assert.Equal(SteeringResult.Halt, cam.ZoomSteer(1f, out _));
    }

    [Fact]
    public void ZoomSteer_RefusesToZoomOutBeyondTheMaximumDistance()
    {
        var cam = ZoomRig(30f);    // beyond 25

        Assert.Equal(SteeringResult.Halt, cam.ZoomSteer(-1f, out _));
    }

    [Fact]
    public void ZoomSteer_StillAllowsZoomingBackInFromBeyondTheMaximum()
    {
        var cam = ZoomRig(30f);

        Assert.Equal(SteeringResult.Force, cam.ZoomSteer(1f, out _));
    }

    /// <summary>
    /// A queued zoom is a distance to cover: the camera closes most of it, then the driver stops and
    /// commits the new follow distance through <c>ForcedUpdate(true)</c>.
    /// </summary>
    [Fact]
    public void Zoom_QueuedDistanceIsSpentAndThenCommitted()
    {
        var cam = ZoomRig(10f);

        cam.RequestZoom(4f);       // zoom in 4 m
        for (int i = 0; i < 300; i++)
            cam.Tick(1f / 60f, 0f);

        Assert.Equal(0f, cam.ZoomPending);
        Assert.Equal(0f, cam.ZoomRate);

        float distance = (cam.Position - cam.GetLookTargetPos()).Length;
        Assert.InRange(distance, 5.5f, 7f);
        // The commit means the follow distance now matches where the camera actually is.
        Assert.Equal(distance, cam.FollowDistance, 1);
    }

    [Fact]
    public void Zoom_OutwardAlsoCommits()
    {
        var cam = ZoomRig(5f);

        cam.RequestZoom(-5f);
        for (int i = 0; i < 300; i++)
            cam.Tick(1f / 60f, 0f);

        float distance = (cam.Position - cam.GetLookTargetPos()).Length;
        Assert.True(distance > 7f, $"expected to have backed off, distance {distance}");
        Assert.Equal(distance, cam.FollowDistance, 1);
    }

    [Fact]
    public void Zoom_StopsAtTheMinimumDistanceHowMuchEverIsQueued()
    {
        var cam = ZoomRig(5f);

        cam.RequestZoom(50f);      // absurd
        for (int i = 0; i < 600; i++)
            cam.Tick(1f / 60f, 0f);

        float distance = (cam.Position - cam.GetLookTargetPos()).Length;
        Assert.True(distance >= CameraVehicleSim.MinZoomDistance - 0.05f,
            $"expected to stop at the 0.7 m floor, got {distance}");
    }

    [Fact]
    public void Zoom_StopsAtTheMaximumDistanceHowMuchEverIsQueued()
    {
        var cam = ZoomRig(5f);

        cam.RequestZoom(-100f);
        for (int i = 0; i < 900; i++)
            cam.Tick(1f / 60f, 0f);

        float distance = (cam.Position - cam.GetLookTargetPos()).Length;
        Assert.True(distance <= CameraVehicleSim.MaxZoomDistance + 1.5f,
            $"expected to stop near the 25 m ceiling, got {distance}");
    }

    // ---- vetoes (1001df85 / 1001dfde / 1001f235) -------------------------

    [Fact]
    public void VetoForward_PointsAtTheLookTarget()
    {
        var cam = MakeCamera();
        cam.TargetPosition = Vec3.Zero;
        cam.EyeTargetLocalPos = Vec3.Zero;
        cam.Position = new Vec3(0f, 0f, -5f);

        cam.VetoForward(out Vec3 forward);

        Assert.Equal(1f, forward.Z, 4);
        Assert.Equal(1f, forward.Length, 4);
    }

    [Fact]
    public void VetoUpAlignment_IsWorldUpOrthogonalisedAgainstTheForward()
    {
        var cam = MakeCamera();
        cam.TargetPosition = Vec3.Zero;
        cam.EyeTargetLocalPos = Vec3.Zero;
        cam.Position = new Vec3(0f, 5f, -5f);   // looking down at 45 degrees

        cam.VetoForward(out Vec3 forward);
        cam.VetoUpAlignment(out Vec3 up);

        Assert.Equal(0f, Vec3.Dot(forward, up), 4);
        Assert.Equal(1f, up.Length, 4);
        Assert.True(up.Y > 0f, "up should still point broadly upward");
    }

    [Fact]
    public void VetoUpAlignment_HandlesLookingStraightDown()
    {
        var cam = MakeCamera();
        cam.TargetPosition = Vec3.Zero;
        cam.EyeTargetLocalPos = Vec3.Zero;
        cam.Position = new Vec3(0f, 5f, 0f);

        cam.VetoForward(out Vec3 forward);
        cam.VetoUpAlignment(out Vec3 up);

        Assert.Equal(1f, up.Length, 4);
        Assert.Equal(0f, Vec3.Dot(forward, up), 4);
    }

    /// <summary>
    /// Crowded in against the head, the facing blends toward the character's own so the view stops
    /// swinging wildly. Full blend at 0.4 m.
    /// </summary>
    [Fact]
    public void VetoForward_BlendsTowardTheCharacterFacingWhenCrowded()
    {
        var cam = MakeCamera();
        cam.TargetPosition = Vec3.Zero;
        cam.EyeTargetLocalPos = Vec3.Zero;
        cam.TargetRotation = Quat.Identity;      // character faces +Z

        // Far: pure look-at. Camera behind, so forward is +Z toward the head.
        cam.Position = new Vec3(0f, 0f, -5f);
        cam.VetoForward(out Vec3 far);
        Assert.Equal(1f, far.Z, 4);

        // Right on top of it: fully the character's facing.
        cam.Position = new Vec3(0.3f, 0f, 0f);
        cam.VetoForward(out Vec3 crowded);
        Assert.Equal(1f, crowded.Z, 3);
        Assert.Equal(0f, crowded.X, 3);
    }

    [Fact]
    public void VetoForward_IsUnblendedBeyondThePointNineThreshold()
    {
        var cam = MakeCamera();
        cam.TargetPosition = Vec3.Zero;
        cam.EyeTargetLocalPos = Vec3.Zero;
        cam.TargetRotation = Quat.Identity;
        cam.Position = new Vec3(1f, 0f, 0f);     // 1.0 m away, just outside 0.9

        cam.VetoForward(out Vec3 forward);

        Assert.Equal(-1f, forward.X, 4);         // straight back at the head, no blend
    }

    // ---- SteeringCamArrive (1001dc46) ------------------------------------

    /// <summary>
    /// The hitch guard halts when a step exceeds ten times the running average. Note it measures the
    /// <b>sub-step</b>, not the frame: the camera's 0.05 s cap means a 10 fps frame still arrives as
    /// 0.05 s steps, so the guard only fires when the frame rate collapses from a high baseline.
    /// Seeded here at ~300 fps so the 0.05 cap is more than ten times the average.
    /// </summary>
    [Fact]
    public void CamArrive_HaltsWhenTheStepJumpsTenfold()
    {
        var cam = MakeCamera();
        cam.TargetPosition = Vec3.Zero;
        cam.EyeTargetLocalPos = Vec3.Zero;
        cam.HasSurface = false;
        cam.Position = new Vec3(0f, 0f, -1f);

        for (int i = 0; i < 20; i++)
            cam.Run(1f / 300f);

        Vec3 before = cam.Position;
        cam.Run(0.5f);                     // sub-steps at the 0.05 cap, 15x the seeded average

        Assert.Equal(before.X, cam.Position.X, 4);
        Assert.Equal(before.Z, cam.Position.Z, 4);
    }

    /// <summary>
    /// And the converse: at an ordinary frame rate the guard stays out of the way, because
    /// sub-stepping has already bounded the step.
    /// </summary>
    [Fact]
    public void CamArrive_DoesNotHaltOnAnOrdinaryFrameRateDrop()
    {
        var cam = MakeCamera();
        cam.TargetPosition = Vec3.Zero;
        cam.EyeTargetLocalPos = Vec3.Zero;
        cam.HasSurface = false;
        cam.Position = new Vec3(0f, 0f, -1f);

        for (int i = 0; i < 20; i++)
            cam.Tick(1f / 60f, 0f);

        Vec3 before = cam.Position;
        cam.Tick(0.2f, 0f);                // 12 fps: sub-steps are 0.05, only 3x the average

        Assert.NotEqual(before.Z, cam.Position.Z, 4);
    }

    // ---- end to end -------------------------------------------------------

    /// <summary>
    /// The camera, left alone, settles behind the character at the preferred offset.
    /// </summary>
    [Fact]
    public void Camera_SettlesAtItsPreferredOffsetBehindTheCharacter()
    {
        var cam = MakeCamera();
        cam.TargetPosition = Vec3.Zero;
        cam.EyeTargetLocalPos = new Vec3(0f, 1.8f, 0f);
        cam.HasSurface = false;
        cam.Position = new Vec3(0f, 1.8f, 0f);   // start on the head

        for (int i = 0; i < 300; i++)            // 5 s at 60 fps
            cam.Tick(1f / 60f, 0f);              // character standing still

        Vec3 look = cam.GetLookTargetPos();
        Vec3 offset = cam.Position - look;

        Assert.True(offset.Z < -3f, $"expected to settle behind the head, offset was {offset}");
        Assert.True(offset.Y > 0.5f, $"and above it, offset was {offset}");
        Assert.True(cam.Speed < 1f, $"and to have settled, speed was {cam.Speed}");
        Assert.Equal(cam.FollowDistance, offset.Length, 1);
    }

    static float WalkAndMeasure(float walkSpeed, float frameTime, float seconds)
    {
        var cam = MakeCamera();
        cam.EyeTargetLocalPos = new Vec3(0f, 1.8f, 0f);
        cam.HasSurface = false;
        cam.TargetPosition = Vec3.Zero;
        cam.Position = new Vec3(0f, 1.8f, -4.5f);

        int frames = (int)MathF.Round(seconds / frameTime);
        for (int i = 0; i < frames; i++)
        {
            cam.TargetPosition += new Vec3(0f, 0f, walkSpeed * frameTime);
            cam.Tick(frameTime, walkSpeed);
        }

        return (cam.Position - cam.GetLookTargetPos()).Length;
    }

    /// <summary>
    /// Follows a walking character and stays at roughly its preferred distance. The bound is tight
    /// on purpose: a loose one hid a real bug where the catch-up curve measured the character's body
    /// rather than the camera's own position, and the camera trailed 22 m behind.
    /// </summary>
    [Theory]
    [InlineData(2f)]
    [InlineData(4f)]
    [InlineData(8f)]
    public void Camera_StaysNearItsPreferredDistanceWhileFollowing(float walkSpeed)
    {
        float distance = WalkAndMeasure(walkSpeed, 1f / 60f, 10f);

        Assert.InRange(distance, 4f, 7.5f);
    }


    /// <summary>
    /// The camera must hold a steady distance while the character walks, not surge and fall back.
    /// The slowing distance (+0x40) is what damps the arrive; reading it from the wrong field left
    /// it at a stale 0.1, which saturated the desired speed at maxVel and made the camera charge its
    /// goal every frame — visible as a wobble while moving forward.
    /// </summary>
    [Theory]
    [InlineData(2f)]
    [InlineData(4f)]
    [InlineData(8f)]
    public void Camera_HoldsASteadyDistanceWhileWalking(float walkSpeed)
    {
        var cam = MakeCamera();
        cam.EyeTargetLocalPos = new Vec3(0f, 1.8f, 0f);
        cam.HasSurface = false;
        cam.TargetPosition = Vec3.Zero;
        cam.Position = new Vec3(0f, 1.8f, -4.743f);

        float min = float.MaxValue, max = float.MinValue, previous = 0f, worstStep = 0f;

        for (int i = 0; i < 900; i++)
        {
            cam.TargetPosition += new Vec3(0f, 0f, walkSpeed / 60f);
            cam.Tick(1f / 60f, 5f);

            float d = (cam.Position - cam.GetLookTargetPos()).Length;
            if (i > 180)     // let it settle first
            {
                if (d < min) min = d;
                if (d > max) max = d;
                worstStep = MathF.Max(worstStep, MathF.Abs(d - previous));
            }
            previous = d;
        }

        Assert.True(max - min < 0.05f, $"distance oscillated between {min} and {max}");
        Assert.True(worstStep < 0.01f, $"distance jumped {worstStep} m in one frame");
    }

    /// <summary>Trailing grows with the character's speed, but only gently.</summary>
    [Fact]
    public void Camera_TrailsFurtherTheFasterTheCharacterMoves()
    {
        float slow = WalkAndMeasure(2f, 1f / 60f, 10f);
        float fast = WalkAndMeasure(8f, 1f / 60f, 10f);

        Assert.True(fast > slow, $"expected more trail at speed: {slow} then {fast}");
        Assert.True(fast - slow < 2.5f, $"but not much more: {slow} then {fast}");
    }

    /// <summary>The following distance barely moves with frame rate.</summary>
    [Fact]
    public void Camera_FollowDistanceIsStableAcrossFrameRates()
    {
        float at30 = WalkAndMeasure(4f, 1f / 30f, 10f);
        float at60 = WalkAndMeasure(4f, 1f / 60f, 10f);
        float at144 = WalkAndMeasure(4f, 1f / 144f, 10f);

        Assert.True(MathF.Abs(at144 - at30) < 0.5f,
            $"30fps {at30}, 60fps {at60}, 144fps {at144}");
    }
}
