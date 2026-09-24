using UnityEngine;

/// <summary>
/// Where <see cref="GfxControlGroundShake"/> posts its camera offset. Stock writes straight into
/// <c>n3Camera_t</c> +0x1d4 and calls <c>UpdateTargetEye</c>, which moves the eye and leaves the
/// target alone; the port keeps the offset here so whatever drives the camera can add it after it has
/// placed itself.
///
/// <see cref="Add"/> accumulates within a frame — two shakes at once sum — and <see cref="Consume"/>
/// hands the total over and clears it, so an offset never survives the frame that produced it. A
/// camera rig that never calls <see cref="Consume"/> simply doesn't shake, which is what the port did
/// before GroundShake was ported.
/// </summary>
public static class EffectCameraShake
{
    static Vector3 _offset;

    /// <summary>This frame's total so far, without clearing it.</summary>
    public static Vector3 Current => _offset;

    public static void Add(Vector3 offset) => _offset += offset;

    /// <summary>The total for this frame; clears it so the next frame starts from nothing.</summary>
    public static Vector3 Consume()
    {
        Vector3 total = _offset;
        _offset = Vector3.zero;
        return total;
    }

    public static void Clear() => _offset = Vector3.zero;
}
