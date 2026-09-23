using UnityEngine;

/// <summary>
/// typeCode 3032 (0xbd8), stock <c>GfxControlGroundShake_t</c>. The maths is
/// <see cref="GroundShakeSim"/>; this measures the camera and posts the offset.
///
/// Stock writes the offset into <c>n3Camera_t</c> +0x1d4 and calls <c>UpdateTargetEye</c>, so the eye
/// moves and the target does not. The port posts the same offset to
/// <see cref="EffectCameraShake"/>, which the camera rig adds to its eye; several shakes at once sum,
/// as they would in stock where each Process overwrites the same slot in turn (§9).
///
/// The locator is read every call (<c>1010f17c</c>) — stock keeps the position in +0x38..+0x40, which
/// is why fields 9, 10 and 11 are scratch rather than settings.
/// </summary>
public sealed class GfxControlGroundShake : GfxControl
{
    readonly GroundShakeSim _sim;

    public GroundShakeSim Sim => _sim;

    public GfxControlGroundShake(GfxTweakRecord record, EffectLocator locator)
        : base(record, locator)
    {
        _sim = new GroundShakeSim(record?.Fields, () => Random.value);
        base.SetDuration(_sim.Duration > 0f ? _sim.Duration : InfiniteDuration);
    }

    /// <summary>Stock slot 6 is the base <c>100a76f0</c>: ready at once.</summary>
    protected override void OnTerminateGracefully() => ReadyFlag = true;

    protected override void OnProcess(float dt)
    {
        // 1010f15d: past the duration it is simply done.
        if (_sim.Expired(Age))
        {
            ReadyFlag = true;
            return;
        }

        Camera camera = Camera.main;
        if (camera == null || Locator == null || !Locator.TryResolve(out Matrix4x4 world))
        {
            ReadyFlag = true;
            return;
        }

        Vector3 origin = world.GetColumn(3);
        float distance = Vector3.Distance(origin, camera.transform.position);
        float amount = _sim.Amount(Age, distance);
        if (amount <= 0f)
            return;

        _sim.Offset(amount, out float x, out float y, out float z);
        EffectCameraShake.Add(new Vector3(x, y, z));
    }
}
