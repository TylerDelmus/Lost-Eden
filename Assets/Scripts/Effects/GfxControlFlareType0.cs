using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 1005 — stock <c>_GfxControlFlare_t</c>, driving its <c>GfxVisualFlareType0</c>. The
/// mechanics are <see cref="FlareType0Sim"/>; this binds them to a locator and draws the result.
///
/// Field 0 bit 1 puts the sprites in the locator's frame: stock then spawns them at zero with no
/// rotation and places the visual on the locator (<c>FUN_1010640a</c> / <c>FUN_101063d9</c> /
/// <c>FUN_10106306</c> / <c>FUN_101062d5</c>), so they ride along with it. Otherwise they are spawned
/// in world space, directions rotated by the locator, and stay where they were emitted.
///
/// Stock Flare has no tint parameter and no light; colour comes from the template or from a parent
/// through <see cref="SetStartColor"/> / <see cref="SetStopColor"/> (Spell1 does this).
/// </summary>
public sealed class GfxControlFlareType0 : GfxControl
{
    readonly FlareType0Sim _sim;
    readonly Texture2D _atlas;
    readonly EffectAtlasFrames _frames;
    readonly int _cols;
    readonly int _rows;
    readonly bool _local;
    readonly float[] _rot = new float[9];
    readonly float[] _corners = new float[12];
    Matrix4x4 _visual = Matrix4x4.identity;

    public FlareType0Sim Sim => _sim;

    /// <summary>Where the sprites are drawn from (the locator in local mode). For debug tooling.</summary>
    public Matrix4x4 VisualMatrix => _visual;

    public GfxControlFlareType0(
        GfxTweakRecord record,
        EffectLocator locator,
        Texture2D atlas,
        EffectAtlasFrames frames,
        int cols,
        int rows,
        int firstFrame,
        int lastFrame)
        : base(record, locator)
    {
        _atlas = atlas;
        _frames = frames;
        _cols = Mathf.Max(1, cols);
        _rows = Mathf.Max(1, rows);
        _local = record != null && (record.FieldInt(0, 0) & 2) != 0;
        _sim = new FlareType0Sim(
            record?.Fields,
            firstFrame,
            lastFrame,
            () => Random.value,
            () => Random.Range(0, 0x8000));
        // Expiry is the stock rule on the sim's duration (see OnProcess), not the base timer.
        base.SetDuration(InfiniteDuration);
    }

    /// <summary>Stock slot 8 (<c>100dcda5</c>): +0x10 = seconds.</summary>
    public override void SetDuration(float seconds)
    {
        if (_sim != null)
            _sim.Duration = seconds;
    }

    public override void SetStartColor(float a, float r, float g, float b) => _sim.SetStartColor(a, r, g, b);

    public override void SetStopColor(float a, float r, float g, float b) => _sim.SetStopColor(a, r, g, b);

    protected override void OnTerminateGracefully() => _sim.TerminateGracefully(Age);

    protected override void OnProcess(float dt)
    {
        // _GfxControl_t::Process (100d2a86): ready once 0 < duration < age.
        if (_sim.Duration > 0f && _sim.Duration < Age)
        {
            ReadyFlag = true;
            return;
        }

        Matrix4x4 world = WorldMatrix;
        bool ready;
        if (_local)
        {
            _visual = world;
            ready = _sim.Step(Age, dt, 0f, 0f, 0f, null);
        }
        else
        {
            _visual = Matrix4x4.identity;
            // Unity's MultiplyVector(v) equals D3D's row-vector v * M for the same rotation, so the
            // stock rows are Unity's columns.
            _rot[0] = world.m00; _rot[1] = world.m10; _rot[2] = world.m20;
            _rot[3] = world.m01; _rot[4] = world.m11; _rot[5] = world.m21;
            _rot[6] = world.m02; _rot[7] = world.m12; _rot[8] = world.m22;
            Vector3 o = world.GetColumn(3);
            ready = _sim.Step(Age, dt, o.x, o.y, o.z, _rot);
        }

        if (ready)
            ReadyFlag = true;
    }

    public override void CollectBillboards(List<EffectBillboardBatch.Quad> dest, Camera camera)
    {
        if (!IsAlive || _sim.LiveCount == 0)
            return;
        CollectSprites(dest, camera, _visual, _sim.Sprites, _atlas, _frames, _cols, _rows, _corners);
    }

    /// <summary>
    /// Draws a <see cref="FlareType0Visual"/>'s live sprites, given in the frame of
    /// <paramref name="visual"/>, as stock does (<see cref="FlareType0Visual.SpriteQuad"/>). Shared
    /// with the controls that use the same visual (Tracer1).
    /// </summary>
    internal static void CollectSprites(
        List<EffectBillboardBatch.Quad> dest,
        Camera camera,
        Matrix4x4 visual,
        FlareType0Visual.Sprite[] sprites,
        Texture2D atlas,
        EffectAtlasFrames frames,
        int cols,
        int rows,
        float[] corners)
    {
        if (dest == null || camera == null || atlas == null || sprites == null)
            return;

        Transform cam = camera.transform;
        Vector3 right = cam.right;
        Vector3 up = cam.up;
        Matrix4x4 toCamera = camera.worldToCameraMatrix;

        for (int i = 0; i < sprites.Length; i++)
        {
            ref FlareType0Visual.Sprite s = ref sprites[i];
            if (!s.Active)
                continue;

            Vector3 p1 = visual.MultiplyPoint3x4(new Vector3(s.P1x, s.P1y, s.P1z));
            Vector3 p2 = visual.MultiplyPoint3x4(new Vector3(s.P2x, s.P2y, s.P2z));
            // Unity camera space looks down -z; stock's D3D view space looks down +z.
            Vector3 c1 = toCamera.MultiplyPoint3x4(p1);
            Vector3 c2 = toCamera.MultiplyPoint3x4(p2);

            FlareType0Visual.SpriteQuad(
                p1.x, p1.y, p1.z, p2.x, p2.y, p2.z,
                c1.x, c1.y, -c1.z, c2.x, c2.y, -c2.z,
                right.x, right.y, right.z, up.x, up.y, up.z,
                s.Size0, corners);

            var v0 = new Vector3(corners[0], corners[1], corners[2]);
            var v1 = new Vector3(corners[3], corners[4], corners[5]);
            var v2 = new Vector3(corners[6], corners[7], corners[8]);
            var v3 = new Vector3(corners[9], corners[10], corners[11]);

            // DisplaySystem truncates the frame (_ftol) before picking the cell.
            Texture2D frameTex = frames != null
                ? frames.GetFrame(atlas, cols, rows, (int)s.Frame)
                : atlas;
            if (frameTex == null)
                continue;

            uint argb = s.Argb;
            dest.Add(new EffectBillboardBatch.Quad
            {
                Matrix = Matrix4x4.Translate((v0 + v3) * 0.5f),
                UseAxes = true,
                AxisX = v1 - v0,
                AxisY = v2 - v0,
                Color = new Color(
                    ((argb >> 16) & 0xff) / 255f,
                    ((argb >> 8) & 0xff) / 255f,
                    (argb & 0xff) / 255f,
                    ((argb >> 24) & 0xff) / 255f),
                Texture = frameTex,
                // GfxVisualFlareType0(material, null, true): SRCALPHA / ONE.
                Additive = true,
            });
        }
    }
}
