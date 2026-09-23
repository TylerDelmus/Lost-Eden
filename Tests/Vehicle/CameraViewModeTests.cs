using System;
using LostEden.Vehicles;
using Xunit;

namespace LostEden.Vehicle.Tests;

/// <summary>
/// The four stock camera modes (Docs/Camera.md §5.11). The dispatch is <c>FUN_10020290</c>; modes 2
/// and 3 are the same class separated by the <c>+0x214</c> flag.
/// </summary>
public class CameraViewModeTests
{
    static CameraVehicleFixedThirdSim Third(bool locked)
    {
        var cam = new CameraVehicleFixedThirdSim { Frozen = locked };
        cam.ApplyStockDefaults();
        cam.HasSurface = false;
        cam.EyeTargetLocalPos = new Vec3(0f, 1.8f, 0f);
        cam.TargetPosition = Vec3.Zero;
        cam.Position = new Vec3(0f, 1.8f, -4.743f);
        return cam;
    }

    static float WalkAndMeasure(CameraVehicleSim cam, float speed, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            cam.TargetPosition += new Vec3(0f, 0f, speed / 60f);
            cam.Tick(1f / 60f, 5f);
        }
        return (cam.Position - cam.GetLookTargetPos()).Length;
    }

    [Fact]
    public void ModeValues_MatchTheStockDispatch()
    {
        Assert.Equal(0, (int)CameraViewMode.FirstPerson);
        Assert.Equal(1, (int)CameraViewMode.Trail);
        Assert.Equal(2, (int)CameraViewMode.Rubber);
        Assert.Equal(3, (int)CameraViewMode.Lock);
    }

    // ---- first person ----------------------------------------------------

    [Fact]
    public void FirstPerson_SitsExactlyAtTheEye()
    {
        var fp = new CameraVehicleFirstPersonSim();
        fp.ApplyStockCameraSettings();
        fp.HasSurface = false;
        fp.TargetPosition = new Vec3(10f, 0f, 5f);
        fp.EyeTargetLocalPos = new Vec3(0f, 1.8f, 0f);

        fp.SetRotAngles(0f, 0f);

        Vec3 eye = fp.GetLookTargetPos();
        Assert.Equal(eye.X, fp.Position.X, 4);
        Assert.Equal(eye.Y, fp.Position.Y, 4);
        Assert.Equal(eye.Z, fp.Position.Z, 4);
    }

    [Fact]
    public void FirstPerson_YawTurnsTheView()
    {
        var fp = new CameraVehicleFirstPersonSim();
        fp.ApplyStockCameraSettings();
        fp.HasSurface = false;
        fp.TargetPosition = Vec3.Zero;
        fp.EyeTargetLocalPos = Vec3.Zero;

        fp.SetRotAngles(0f, MathF.PI / 2f);
        fp.VetoForward(out Vec3 forward);

        Assert.Equal(1f, forward.X, 3);
        Assert.Equal(0f, forward.Z, 3);
    }

    [Fact]
    public void FirstPerson_ViewRotationComposesCharacterAndMouseAngles()
    {
        var fp = new CameraVehicleFirstPersonSim();
        fp.ApplyStockCameraSettings();
        fp.TargetPosition = Vec3.Zero;
        fp.EyeTargetLocalPos = Vec3.Zero;
        fp.TargetRotation = Quat.FromAxisAngle(Vec3.ReferenceUp, MathF.PI / 2f);

        fp.SetRotAngles(0f, MathF.PI / 2f);
        fp.VetoForward(out Vec3 forward);

        // Character faces +X, mouse adds another 90 degrees, so the view faces -Z.
        Assert.Equal(-1f, forward.Z, 3);
    }

    [Fact]
    public void FirstPerson_NeverSteers()
    {
        var fp = new CameraVehicleFirstPersonSim();
        fp.ApplyStockCameraSettings();
        fp.HasSurface = false;
        fp.TargetPosition = Vec3.Zero;
        fp.EyeTargetLocalPos = new Vec3(0f, 1.8f, 0f);
        fp.SetRotAngles(0f, 0f);

        for (int i = 0; i < 300; i++)
        {
            fp.TargetPosition += new Vec3(0f, 0f, 4f / 60f);
            fp.Tick(1f / 60f, 5f);
        }

        // Still exactly on the eye — no lag whatsoever.
        Assert.Equal(0f, (fp.Position - fp.GetLookTargetPos()).Length, 4);
        Assert.Equal(0f, fp.Speed, 4);
    }

    // ---- lock vs rubber ---------------------------------------------------

    /// <summary>
    /// Lock is rigid: it holds the preferred offset exactly, with no lag, because it does not steer
    /// at all — <c>DecideSnap</c> places it.
    /// </summary>
    [Fact]
    public void Lock_HoldsThePreferredOffsetExactlyWhileWalking()
    {
        var cam = Third(locked: true);

        float distance = WalkAndMeasure(cam, 4f, 600);

        Assert.Equal(cam.FollowDistance, distance, 2);
    }

    /// <summary>Rubber lags, because it is steered there under mass and force.</summary>
    [Fact]
    public void Rubber_LagsBehindThePreferredOffsetWhileWalking()
    {
        var cam = Third(locked: false);

        float distance = WalkAndMeasure(cam, 4f, 600);

        Assert.True(distance > cam.FollowDistance + 0.5f,
            $"expected rubber to trail; distance {distance} vs follow {cam.FollowDistance}");
    }

    [Fact]
    public void Lock_IsTighterThanRubber()
    {
        float locked = WalkAndMeasure(Third(locked: true), 4f, 600);
        float rubber = WalkAndMeasure(Third(locked: false), 4f, 600);

        Assert.True(locked < rubber,
            $"lock {locked} should sit closer than rubber {rubber}");
    }

    [Fact]
    public void Lock_DoesNotSteer()
    {
        var cam = Third(locked: true);

        WalkAndMeasure(cam, 4f, 300);

        Assert.Equal(0f, cam.Speed, 4);
    }

    /// <summary>
    /// DecideSnap is gated on the lock flag, so calling it in rubber mode must do nothing at all.
    /// </summary>
    [Fact]
    public void DecideSnap_IsANoOpInRubber()
    {
        var cam = Third(locked: false);
        cam.RecalcOptimalPos();
        Vec3 before = cam.Position;

        cam.DecideSnap();

        Assert.Equal(before, cam.Position);
    }

    // ---- trail -------------------------------------------------------------

    [Fact]
    public void Trail_FollowsAtRoughlyItsFollowDistance()
    {
        var cam = new CameraVehicleSim();
        cam.ApplyStockCameraSettings();
        cam.HasSurface = false;
        cam.EyeTargetLocalPos = new Vec3(0f, 1.8f, 0f);
        cam.TargetPosition = Vec3.Zero;
        cam.FollowDistance = 4.743f;
        cam.Position = new Vec3(0f, 1.8f, -4.743f);

        float distance = WalkAndMeasure(cam, 4f, 600);

        Assert.InRange(distance, 4f, 7.5f);
    }
}
