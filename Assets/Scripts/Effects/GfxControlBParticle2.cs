using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 3028 (0xbd4), stock <c>GfxControlBParticle2_t</c> (vftable <c>Gamecode 1016effc</c>). The
/// particle maths is <see cref="BParticle2Sim"/>; this owns the locator, the clock and the drawing.
///
/// The emitter and the turn come from the locator's local-mode accessors (<c>10106306</c> /
/// <c>101062d5</c>): with field 0 bit 1 the locator's position and turn, without it the world origin and
/// no turn. Slot 6 (<c>1010af68</c>) raises the terminate flag; unless flag 0x200000 keeps them, the
/// particles go with it at the next Process. At the duration they all go at once.
///
/// Stock runs Process once per frame with the frame's delta, and the respawn cap (field 15), the bounce
/// and the drag work per call, so the look changed with the frame rate. The port replays the calls on the
/// fixed clock (<see cref="EffectFrameRate.StockProcessHz"/>, Docs §3.7) and draws each particle between
/// its last two steps.
///
/// Each particle is a <c>GfxVisualBParticle2</c> (DisplaySystem ctor <c>1000b432</c>, geometry
/// <c>1000b4fc</c>): a quad on the camera's right and up axes turned by the particle's angle about the
/// view axis, half-width = size, half-height = size / field 33, the material cell <c>_ftol(frame)</c>,
/// coloured by the curve. Its corners carry u from the right edge to the left and v from top to bottom;
/// flag 0x100 swaps them. Flag 0x200 is the visual's additive switch.
/// </summary>
public sealed class GfxControlBParticle2 : GfxControl
{
    const int FlagLocalMode = 2;
    const int FlagSwapUv = 0x100;

    /// <summary>Replayed calls allowed in one frame; a hitch loses time rather than fast-forwarding.</summary>
    const int MaxStepsPerFrame = 8;

    readonly BParticle2Sim _sim;
    readonly Texture2D _atlas;
    readonly EffectAtlasFrames _frames;
    readonly int _cols;
    readonly int _rows;
    readonly bool _localMode;
    readonly bool _swapUv;
    readonly float _offsetY;
    readonly float[] _turn = new float[9];
    readonly System.Func<float, float, float, float> _ground = EffectGround.HeightAt;
    readonly BParticle2Sim.Particle[] _previous;
    float _carry;
    float _stepAge;

    public BParticle2Sim Sim => _sim;

    public GfxControlBParticle2(
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
        _sim = new BParticle2Sim(record?.Fields, firstFrame, lastFrame, () => Random.value);
        _previous = new BParticle2Sim.Particle[_sim.Particles.Length];
        int flags = _sim.Flags;
        _localMode = (flags & FlagLocalMode) != 0;
        _swapUv = (flags & FlagSwapUv) != 0;
        // 1010be61: the respawn height offset is the locator's template offset y (field 2).
        _offsetY = record != null ? record.Field(2, 0f) : 0f;
        base.SetDuration(_sim.Duration);

        Vector3 emitter = Emitter(out bool resolved);
        if (!resolved)
        {
            ReadyFlag = true;
            return;
        }
        if ((_sim.Flags & BParticle2Sim.FlagGroundEmitter) != 0)
        {
            float g = EffectGround.HeightAt(emitter.x, emitter.y, emitter.z);
            if (!float.IsNaN(g))
                emitter.y = g;
        }
        _sim.Init(emitter.x, emitter.y, emitter.z, _turn);
    }

    /// <summary>
    /// The emitter position and turn this frame, from the locator; fills <see cref="_turn"/>. Stock's
    /// init also drops the emitter to the ground with flag 0x400 (<c>1010c03f</c>).
    /// </summary>
    Vector3 Emitter(out bool resolved)
    {
        Matrix4x4 world = Matrix4x4.identity;
        resolved = Locator != null && Locator.TryResolve(out world);
        if (!_localMode || !resolved)
        {
            SetIdentity(_turn);
            return Vector3.zero;
        }

        Vector3 x = ((Vector3)world.GetColumn(0)).normalized;
        Vector3 y = ((Vector3)world.GetColumn(1)).normalized;
        Vector3 z = ((Vector3)world.GetColumn(2)).normalized;
        // turn * v = x * v.x + y * v.y + z * v.z.
        _turn[0] = x.x; _turn[1] = y.x; _turn[2] = z.x;
        _turn[3] = x.y; _turn[4] = y.y; _turn[5] = z.y;
        _turn[6] = x.z; _turn[7] = y.z; _turn[8] = z.z;
        return world.GetColumn(3);
    }

    static void SetIdentity(float[] m)
    {
        for (int i = 0; i < 9; i++)
            m[i] = i % 4 == 0 ? 1f : 0f;
    }

    /// <summary>Stock slot 6 (<c>1010af68</c>): only the flag.</summary>
    protected override void OnTerminateGracefully() => _sim.Terminating = true;

    protected override void OnArmed() => Body(0f, 0f);

    protected override void OnProcess(float dt)
    {
        float step = EffectFrameRate.StockProcessSeconds;
        int steps = EffectFrameRate.TakeFixedSteps(ref _carry, dt, step, MaxStepsPerFrame);
        for (int s = 0; s < steps && !ReadyFlag; s++)
        {
            System.Array.Copy(_sim.Particles, _previous, _previous.Length);
            _stepAge += step;
            Body(step, _stepAge);
        }
    }

    void Body(float dt, float age)
    {
        // 1010b4aa: terminating without 0x200000 is the end.
        if ((_sim.Flags & BParticle2Sim.FlagLiveUntilTerminated) == 0 && _sim.Terminating)
        {
            ReadyFlag = true;
            return;
        }

        Vector3 emitter = Emitter(out bool resolved);
        if (!resolved)
        {
            ReadyFlag = true;
            return;
        }

        if (!_sim.Step(dt, age, Duration, emitter.x, emitter.y, emitter.z, _turn, _ground, _offsetY))
            ReadyFlag = true;
    }

    public override void CollectBillboards(List<EffectBillboardBatch.Quad> dest, Camera camera)
    {
        if (!IsAlive || dest == null || camera == null || _atlas == null)
            return;

        Vector3 right = camera.transform.right;
        Vector3 up = camera.transform.up;
        float aspect = _sim.Aspect;
        // Between the last two steps: the time carried towards the next one says how far.
        float blend = Mathf.Clamp01(_carry / EffectFrameRate.StockProcessSeconds);
        BParticle2Sim.Particle[] particles = _sim.Particles;
        for (int i = 0; i < particles.Length; i++)
        {
            BParticle2Sim.Particle p = particles[i];
            if (!p.Visible)
                continue;

            // A particle that was dead last step was respawned; draw it where it is.
            BParticle2Sim.Particle q = _previous[i];
            if (q.Visible)
            {
                p.X = q.X + (p.X - q.X) * blend;
                p.Y = q.Y + (p.Y - q.Y) * blend;
                p.Z = q.Z + (p.Z - q.Z) * blend;
                p.Size = q.Size + (p.Size - q.Size) * blend;
                p.Angle = q.Angle + (p.Angle - q.Angle) * blend;
            }

            Texture2D frameTex = _frames != null
                ? _frames.GetFrame(_atlas, _cols, _rows, (int)p.Frame)
                : _atlas;
            if (frameTex == null)
                continue;

            // 1000b60b: right and up turned by the angle about the view axis.
            float c = Mathf.Cos(p.Angle), s = Mathf.Sin(p.Angle);
            Vector3 r = right * c + up * s;
            Vector3 u = up * c - right * s;
            float w = p.Size;
            float h = p.Size / aspect;

            uint argb = p.Argb;
            var quad = new EffectBillboardBatch.Quad
            {
                Matrix = Matrix4x4.TRS(new Vector3(p.X, p.Y, p.Z), Quaternion.identity, Vector3.one),
                Color = new Color(
                    ((argb >> 16) & 0xff) / 255f,
                    ((argb >> 8) & 0xff) / 255f,
                    (argb & 0xff) / 255f,
                    ((argb >> 24) & 0xff) / 255f),
                Texture = frameTex,
                Additive = _sim.Additive,
                UseAxes = true,
            };
            if (_swapUv)
            {
                quad.AxisX = -u * (2f * h);
                quad.AxisY = r * (2f * w);
            }
            else
            {
                quad.AxisX = -r * (2f * w);
                quad.AxisY = u * (2f * h);
            }
            dest.Add(quad);
        }
    }
}
