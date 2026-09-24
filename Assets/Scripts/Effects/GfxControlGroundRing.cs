using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 3012 (0xbc4), stock <c>_GfxControlGroundRing_t</c> with its <c>GfxVisualGroundRing</c>.
/// The mechanics are <see cref="GroundRingSim"/>; this binds them to a locator, samples the ground
/// and draws the annulus.
///
/// The ring is one triangle strip alternating inner and outer rim points, so each pair of steps makes
/// a quad of the band. The inner rim carries colour A and the outer colour B (the visual's
/// <c>Update</c> takes the two separately, <c>10154380</c>).
///
/// <b>Ground height.</b> Stock asks n3Playfield for the terrain height under each point
/// (<c>100d33f3</c>). The port raycasts straight down instead, which is the engine's equivalent; with
/// nothing to hit — GfxTest has no floor collider — it falls back to the ring's own height, which is
/// the right answer on flat ground and the only honest one without terrain colliders.
/// </summary>
public sealed class GfxControlGroundRing : GfxControl
{
    /// <summary>How far above and below the ring to look for ground.</summary>
    const float ProbeUp = 200f;
    const float ProbeDown = 400f;

    readonly GroundRingSim _sim;
    readonly Texture2D _texture;
    readonly EffectBillboardBatch.Strip _strip;

    bool _placed;
    bool _built;

    public GroundRingSim Sim => _sim;

    public GfxControlGroundRing(GfxTweakRecord record, EffectLocator locator, Texture2D texture)
        : base(record, locator)
    {
        _texture = texture;
        _sim = new GroundRingSim(record?.Fields) { GroundHeight = SampleGround };

        int n = _sim.PointCount;
        _strip = new EffectBillboardBatch.Strip
        {
            Positions = new Vector3[n],
            Uvs = new Vector2[n],
            Colors = new Color32[n],
            Color = Color.white,
            Texture = texture,
            Additive = _sim.Additive,
        };

        SetDurationFromTemplate();
    }

    protected override void OnArmed() => Place();

    protected override void OnProcess(float dt)
    {
        Place();

        Matrix4x4 world = Matrix4x4.identity;
        bool valid = Locator != null && Locator.TryResolve(out world);
        Vector3 origin = valid ? (Vector3)world.GetColumn(3) : Vector3.zero;

        _sim.Step(Age, valid, origin.x, origin.y, origin.z);
        if (_sim.Dead)
            ReadyFlag = true;

        _placed = valid || _sim.OutlivesLocator;
    }

    /// <summary>The build (<c>100e1f21</c>): the one chance a baked ring gets to read the ground.</summary>
    void Place()
    {
        if (_built)
            return;
        Matrix4x4 world = Matrix4x4.identity;
        if (Locator == null || !Locator.TryResolve(out world))
            return;
        Vector3 origin = world.GetColumn(3);
        _sim.Build(origin.x, origin.y, origin.z);
        _built = true;
        _placed = true;
    }

    /// <summary>Stock's <c>100d33f3</c>, as a downward ray.</summary>
    float SampleGround(float x, float z)
    {
        if (LostEden.Vehicles.WorldCollision.HasSurface)
        {
            return LostEden.Vehicles.WorldCollision.GroundAt(
                new Vector3(x, _sim.CentreY, z), ProbeUp, ProbeDown, out Vector3 surfaceHit, out _)
                ? surfaceHit.y
                : _sim.CentreY;
        }

        var from = new Vector3(x, _sim.CentreY + ProbeUp, z);
        return Physics.Raycast(from, Vector3.down, out RaycastHit hit, ProbeUp + ProbeDown)
            ? hit.point.y
            : _sim.CentreY;
    }

    public override void CollectStrips(List<EffectBillboardBatch.Strip> dest, Camera camera)
    {
        if (!IsAlive || !_placed || !_built || dest == null || _texture == null || _sim.Segments <= 0)
            return;
        if ((_sim.CurrentA >> 24) == 0 && (_sim.CurrentB >> 24) == 0)
            return;

        var centre = new Vector3(_sim.CentreX, _sim.CentreY, _sim.CentreZ);
        int n = _sim.PointCount;
        Color32 inner = EffectBillboardBatch.ToColor32(_sim.CurrentA);
        Color32 outer = EffectBillboardBatch.ToColor32(_sim.CurrentB);

        // 10016eac: u walks round the ring in steps of field 11 / segments and v crosses the band
        // from 0 on the inner rim to field 12 on the outer, so the texture tiles ALONG the ring.
        // The world-projected mode at 10016f73 only applies when flags bit 8 is clear, which no
        // shipped record does.
        float step = _sim.Segments > 0 ? (float)(_sim.USpan / (double)_sim.Segments) : 0f;

        for (int i = 0; i < n; i++)
        {
            int p = i * 3;
            Vector3 world = centre + new Vector3(_sim.Points[p], _sim.Points[p + 1], _sim.Points[p + 2]);
            _strip.Positions[i] = world;
            float u = _sim.RingWalkUv
                ? (float)((i / 2) * (double)step)
                : (float)(world.x * (double)_sim.USpan);
            float v = _sim.RingWalkUv
                ? ((i & 1) == 0 ? 0f : _sim.VSpan)
                : (float)(world.z * (double)_sim.VSpan);
            _strip.Uvs[i] = new Vector2(u, v);
            _strip.Colors[i] = (i & 1) == 0 ? inner : outer;
        }

        _strip.Count = n;
        dest.Add(_strip);
    }
}
