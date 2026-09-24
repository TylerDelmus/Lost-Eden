using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 3024 (0xbd0), stock <c>GfxControlBParticle_t</c>. The maths is <see cref="BParticleSim"/>
/// (particle mode 8 only; other modes draw nothing); this places the visual and draws it.
///
/// The visual sits at the locator's local-mode position (<c>10106306</c>; flag 0x400 drops it to the
/// ground) with the locator's turn (<c>101062d5</c>), so particle points are in the emitter's frame. The
/// control's colour and size keys go to the visual every call (<c>1010ab70</c>..<c>1010ab98</c>).
///
/// Each particle is a quad (<c>1000a70f</c>) whose axes are the camera's right and up turned by the
/// particle's angle about the view axis (stock hands the stored degrees to the rotation as they are,
/// and turns them for every quad type), sized from the keys, in the material cell of
/// <c>_ftol(frame)</c> (flag 0x100: the whole texture), alpha = the colour's alpha times the fade.
///
/// Field 14 picks the shape. Quad type 0 (<c>1000accb</c>, what mode 8 uses) is the symmetric
/// camera-facing one. Quad type 2 (<c>1000aea0</c>, what mode 1 uses) is a tapered plume: a far edge
/// at twice the width lifted twice the height along the turned up axis, narrowing to a base at the
/// particle. Mode 1 then offsets the UVs per particle and clears the alpha of the two far vertices
/// (<c>1000ac5f</c>), so the plume fades out along its length.
/// </summary>
public sealed class GfxControlBParticle : GfxControl
{
    readonly BParticleSim _sim;
    readonly Texture2D _atlas;
    readonly EffectAtlasFrames _frames;
    readonly int _cols;
    readonly int _rows;
    readonly bool _localMode;
    readonly List<EffectBillboardBatch.Strip> _strips = new List<EffectBillboardBatch.Strip>(1);
    int _stripsUsed;
    Matrix4x4 _visual = Matrix4x4.identity;

    public BParticleSim Sim => _sim;

    public GfxControlBParticle(
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
        _sim = new BParticleSim(record?.Fields, firstFrame, lastFrame, () => Random.value);
        _localMode = (_sim.Flags & 2) != 0;
        base.SetDuration(InfiniteDuration);
    }

    /// <summary>Stock slot 6 is the base one (<c>100a76f0</c>): ready at once.</summary>
    protected override void OnTerminateGracefully() => ReadyFlag = true;

    protected override void OnArmed() => Body(0f);

    protected override void OnProcess(float dt) => Body(dt);

    void Body(float dt)
    {
        if (Locator == null || !Locator.TryResolve(out Matrix4x4 m))
        {
            ReadyFlag = true;
            return;
        }

        if (_localMode)
        {
            Vector3 pos = m.GetColumn(3);
            if ((_sim.Flags & BParticleSim.FlagGround) != 0)
            {
                float g = EffectGround.HeightAt(pos.x, pos.y, pos.z);
                if (!float.IsNaN(g))
                    pos.y = g;
            }
            var o = Matrix4x4.identity;
            o.SetColumn(0, ((Vector3)m.GetColumn(0)).normalized);
            o.SetColumn(1, ((Vector3)m.GetColumn(1)).normalized);
            o.SetColumn(2, ((Vector3)m.GetColumn(2)).normalized);
            o.SetColumn(3, new Vector4(pos.x, pos.y, pos.z, 1f));
            _visual = o;
        }
        else
        {
            _visual = Matrix4x4.identity;
        }

        if (!_sim.Advance(dt))
            ReadyFlag = true;
    }

    public override void CollectBillboards(List<EffectBillboardBatch.Quad> dest, Camera camera)
    {
        if (!IsAlive || dest == null || camera == null || _atlas == null || !_sim.Supported)
            return;
        if (_sim.QuadType != 0)
            return;

        Vector3 right = camera.transform.right;
        Vector3 up = camera.transform.up;
        Vector3 eye = camera.transform.position;
        uint argb = _sim.Argb;
        uint alphaByte = (argb >> 24) & 0xff;

        BParticleSim.Particle[] particles = _sim.Particles;
        for (int i = 0; i < particles.Length; i++)
        {
            BParticleSim.Particle p = particles[i];
            Vector3 world = _visual.MultiplyPoint3x4(new Vector3(p.X, p.Y, p.Z));
            if (!_sim.DistanceFade(Vector3.Distance(eye, world), out float distanceFade))
                continue;
            if (!_sim.Drawn(p, distanceFade, out float fade, out float w, out float h))
                continue;

            Texture2D tex = _sim.SwapUv || _frames == null
                ? _atlas
                : _frames.GetFrame(_atlas, _cols, _rows, (int)p.Frame);
            if (tex == null)
                continue;

            int alpha = (int)(alphaByte * fade);
            float c = Mathf.Cos(p.Angle), s = Mathf.Sin(p.Angle);
            Vector3 r = right * c + up * s;
            Vector3 u = up * c - right * s;
            dest.Add(new EffectBillboardBatch.Quad
            {
                Matrix = Matrix4x4.TRS(world, Quaternion.identity, Vector3.one),
                Color = new Color(
                    ((argb >> 16) & 0xff) / 255f,
                    ((argb >> 8) & 0xff) / 255f,
                    (argb & 0xff) / 255f,
                    (alpha & 0xff) / 255f),
                Texture = tex,
                Additive = _sim.Additive,
                UseAxes = true,
                AxisX = r * (2f * w),
                AxisY = u * (2f * h),
            });
        }
    }

    /// <summary>
    /// Quad type 2 (<c>1000aea0</c>), the tapered plume. A strip carries one texture, so a run of
    /// particles ends whenever the material cell changes; the shipped mode-1 records share one frame
    /// across every particle, so in practice this is a single strip.
    /// </summary>
    public override void CollectStrips(List<EffectBillboardBatch.Strip> dest, Camera camera)
    {
        if (!IsAlive || dest == null || camera == null || _atlas == null || !_sim.Supported)
            return;
        if (_sim.QuadType != 2)
            return;

        Vector3 right = camera.transform.right;
        Vector3 up = camera.transform.up;
        Vector3 eye = camera.transform.position;
        uint argb = _sim.Argb;
        uint alphaByte = (argb >> 24) & 0xff;

        BParticleSim.Particle[] particles = _sim.Particles;
        EffectBillboardBatch.Strip strip = null;
        Texture current = null;
        int n = 0;
        _stripsUsed = 0;

        for (int i = 0; i < particles.Length; i++)
        {
            BParticleSim.Particle p = particles[i];
            Vector3 world = _visual.MultiplyPoint3x4(new Vector3(p.X, p.Y, p.Z));
            if (!_sim.DistanceFade(Vector3.Distance(eye, world), out float distanceFade))
                continue;
            if (!_sim.Drawn(p, distanceFade, out float fade, out float w, out float h))
                continue;

            Texture2D tex = _sim.SwapUv || _frames == null
                ? _atlas
                : _frames.GetFrame(_atlas, _cols, _rows, (int)p.Frame);
            if (tex == null)
                continue;

            if (strip == null || tex != current)
            {
                Flush(dest, strip, n);
                strip = NextStrip(particles.Length * 4);
                strip.Texture = tex;
                strip.Additive = _sim.Additive;
                current = tex;
                n = 0;
            }

            float c = Mathf.Cos(p.Angle), s = Mathf.Sin(p.Angle);
            Vector3 r = right * c + up * s;
            Vector3 u = up * c - right * s;
            Vector3 far = world + u * (2f * h);
            Vector3 wide = r * (2f * w);
            Vector3 near = r * w;

            int alpha = (int)(alphaByte * fade) & 0xff;
            var opaque = new Color32((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)alpha);
            // 1000ac88: the far pair keeps the colour and loses the alpha.
            var clear = new Color32(opaque.r, opaque.g, opaque.b, 0);

            Set(strip, n + 0, far + wide, 0f, 1f, clear, p);
            Set(strip, n + 1, far - wide, 1f, 1f, clear, p);
            Set(strip, n + 2, world + near, 0f, 0f, opaque, p);
            Set(strip, n + 3, world - near, 1f, 0f, opaque, p);
            n += 4;
        }

        Flush(dest, strip, n);
    }

    static void Flush(List<EffectBillboardBatch.Strip> dest, EffectBillboardBatch.Strip strip, int count)
    {
        if (strip == null || count <= 0)
            return;
        strip.Count = count;
        dest.Add(strip);
    }

    /// <summary>1000ac68: mode 1 slides the UVs by the particle's own offset and mirrors u.</summary>
    void Set(
        EffectBillboardBatch.Strip strip,
        int index,
        Vector3 position,
        float u,
        float v,
        Color32 colour,
        in BParticleSim.Particle p)
    {
        strip.Positions[index] = position;
        strip.Uvs[index] = _sim.Mode == 1
            ? new Vector2(1f - (u + p.UvU), v + p.UvV)
            : new Vector2(u, v);
        strip.Colors[index] = colour;
    }

    EffectBillboardBatch.Strip NextStrip(int vertices)
    {
        while (_strips.Count <= _stripsUsed)
            _strips.Add(new EffectBillboardBatch.Strip { Color = Color.white, Quads = true });

        EffectBillboardBatch.Strip strip = _strips[_stripsUsed++];
        if (strip.Positions == null || strip.Positions.Length < vertices)
        {
            strip.Positions = new Vector3[vertices];
            strip.Uvs = new Vector2[vertices];
            strip.Colors = new Color32[vertices];
        }

        return strip;
    }
}
