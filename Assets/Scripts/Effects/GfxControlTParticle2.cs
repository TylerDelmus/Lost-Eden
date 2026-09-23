using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 3031 (0xbd7), stock <c>GfxControlTParticle2_t</c> (vftable <c>Gamecode 1016fa34</c>). The
/// particle maths is <see cref="TParticle2Sim"/>; this owns the locator, the clock and the drawing.
///
/// The emitter and the turn come from the locator (<c>10106306</c> position, <c>101062d5</c> matrix with
/// its rows set to length 1 by <c>10113a45</c>), with no flag gate — unlike BParticle2, this control
/// always follows its locator. Particles then live in world space: each is its own
/// <c>GfxVisualTParticle2</c> with an identity transform, and the control writes the particle's world
/// position straight into the visual's vertices. Slot 6 is the base <c>100a76f0</c>, so a terminate is
/// ready at once, and the duration is the base timer's (slot 8 <c>1010340b</c>).
///
/// Stock runs Process once per frame with the frame's delta, and the respawn cap (field 15), the bounce
/// and the growth (field 30, a bare multiply) work per call, so the look changed with the frame rate.
/// The port replays the calls on the fixed clock (<see cref="EffectFrameRate.StockProcessHz"/>,
/// Docs §3.7) and draws each particle between its last two steps.
///
/// Geometry (<c>1002ac11</c>), per particle and the same construction as
/// <see cref="GfxControlTParticle"/>: a streak from the particle to <c>position + velocity * field 14</c>.
/// d = the velocity set to length 1 (a still particle is skipped); side1 = |d x (0, 1, 0)| (else
/// (1, 0, 0)), side2 = |side1 x d| (else (0, 1, 0)). Two quads, on side2 then side1, the near pair at
/// half-width = size and the far pair at size / field 33, the near pair in curve A's colour and the far
/// pair in curve B's. The texture is the material's cell for the particle's frame: u from the cell's
/// column and v from its row, v running from the far pair to the near; flag 0x100 transposes u and v and
/// 0x80000 flips v end for end. Flag 0x200 is the visual's additive switch.
/// </summary>
public sealed class GfxControlTParticle2 : GfxControl
{
    /// <summary>Replayed calls allowed in one frame; a hitch loses time rather than fast-forwarding.</summary>
    const int MaxStepsPerFrame = 8;

    readonly TParticle2Sim _sim;
    readonly Texture2D _atlas;
    readonly int _cols;
    readonly int _rows;
    readonly bool _swapUv;
    readonly bool _flipV;
    readonly bool _edgeFade;
    readonly float[] _turn = new float[9];
    readonly System.Func<float, float, float, float> _ground = EffectGround.HeightAt;
    readonly TParticle2Sim.Particle[] _previous;
    readonly EffectBillboardBatch.Strip _strip;
    float _carry;
    float _stepAge;

    public TParticle2Sim Sim => _sim;

    public GfxControlTParticle2(
        GfxTweakRecord record,
        EffectLocator locator,
        Texture2D atlas,
        int cols,
        int rows,
        int firstFrame,
        int lastFrame)
        : base(record, locator)
    {
        _atlas = atlas;
        _cols = Mathf.Max(1, cols);
        _rows = Mathf.Max(1, rows);
        EffectAtlasFrames.InferGrid(atlas, ref _cols, ref _rows);
        _sim = new TParticle2Sim(record?.Fields, firstFrame, lastFrame, () => Random.value);
        _previous = new TParticle2Sim.Particle[_sim.Particles.Length];
        int flags = _sim.Flags;
        _swapUv = (flags & TParticle2Sim.FlagSwapUv) != 0;
        _flipV = (flags & TParticle2Sim.FlagFlipV) != 0;
        _edgeFade = (flags & TParticle2Sim.FlagEdgeFade) != 0;
        base.SetDuration(_sim.Duration);

        int quads = _sim.Particles.Length * 2;
        _strip = new EffectBillboardBatch.Strip
        {
            Positions = new Vector3[quads * 4],
            Uvs = new Vector2[quads * 4],
            Colors = new Color32[quads * 4],
            Color = Color.white,
            Texture = atlas,
            Additive = _sim.Additive,
            Quads = true,
        };

        Vector3 emitter = Emitter(out bool resolved);
        if (!resolved)
        {
            ReadyFlag = true;
            return;
        }
        // 101143a8: the init drops the emitter to the ground with 0x400 before the first spawn.
        if ((_sim.Flags & TParticle2Sim.FlagGroundEmitter) != 0)
        {
            float g = EffectGround.HeightAt(emitter.x, emitter.y, emitter.z);
            if (!float.IsNaN(g))
                emitter.y = g;
        }
        _sim.Init(emitter.x, emitter.y, emitter.z, _turn);
        System.Array.Copy(_sim.Particles, _previous, _previous.Length);
    }

    /// <summary>The emitter position and turn this frame, from the locator; fills <see cref="_turn"/>.</summary>
    Vector3 Emitter(out bool resolved)
    {
        Matrix4x4 world = Matrix4x4.identity;
        resolved = Locator != null && Locator.TryResolve(out world);
        if (!resolved)
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

    /// <summary>Stock slot 6 is the base one (<c>100a76f0</c>): ready at once.</summary>
    protected override void OnTerminateGracefully() => ReadyFlag = true;

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
        Vector3 emitter = Emitter(out bool resolved);
        if (!resolved)
        {
            ReadyFlag = true;
            return;
        }

        if (!_sim.Step(dt, age, Duration, emitter.x, emitter.y, emitter.z, _turn, _ground))
            ReadyFlag = true;
    }

    public override void CollectStrips(List<EffectBillboardBatch.Strip> dest, Camera camera)
    {
        if (!IsAlive || dest == null || _atlas == null)
            return;

        // Between the last two steps: the time carried towards the next one says how far.
        float blend = Mathf.Clamp01(_carry / EffectFrameRate.StockProcessSeconds);
        Vector3 eye = camera != null ? camera.transform.position : Vector3.zero;
        TParticle2Sim.Particle[] particles = _sim.Particles;
        int count = 0;

        for (int i = 0; i < particles.Length; i++)
        {
            TParticle2Sim.Particle p = particles[i];
            if (!p.Visible)
                continue;

            // A particle that was dead last step was respawned; draw it where it is.
            TParticle2Sim.Particle q = _previous[i];
            if (q.Visible)
            {
                p.X = q.X + (p.X - q.X) * blend;
                p.Y = q.Y + (p.Y - q.Y) * blend;
                p.Z = q.Z + (p.Z - q.Z) * blend;
                p.VX = q.VX + (p.VX - q.VX) * blend;
                p.VY = q.VY + (p.VY - q.VY) * blend;
                p.VZ = q.VZ + (p.VZ - q.VZ) * blend;
                p.Size = q.Size + (p.Size - q.Size) * blend;
            }

            // 1002ac66: a particle with no velocity has no streak.
            var velocity = new Vector3(p.VX, p.VY, p.VZ);
            if (velocity == Vector3.zero)
                continue;

            var near = new Vector3(p.X, p.Y, p.Z);
            Vector3 far = near + velocity * _sim.Trail;
            Vector3 d = velocity.normalized;

            Vector3 side1 = Vector3.Cross(d, Vector3.up);
            side1 = side1 == Vector3.zero ? Vector3.right : side1.normalized;
            Vector3 side2 = Vector3.Cross(side1, d);
            side2 = side2 == Vector3.zero ? Vector3.up : side2.normalized;

            float nearWidth = p.Size;
            float farWidth = p.Size / _sim.TailDivisor;
            Color32 nearColour = EffectBillboardBatch.ToColor32(p.NearArgb);
            Color32 farColour = EffectBillboardBatch.ToColor32(p.FarArgb);
            Cell(p.Frame, out float u0, out float v0, out float du, out float dv);

            AddRibbon(near, far, side2, nearWidth, farWidth, nearColour, farColour, u0, v0, du, dv, eye, ref count);
            AddRibbon(near, far, side1, nearWidth, farWidth, nearColour, farColour, u0, v0, du, dv, eye, ref count);
        }

        if (count == 0)
            return;
        _strip.Count = count;
        dest.Add(_strip);
    }

    /// <summary><c>1002ad20</c>: the material's cell for this frame, column by column then row by row.</summary>
    void Cell(float frame, out float u0, out float v0, out float du, out float dv)
    {
        du = 1f / _cols;
        dv = 1f / _rows;
        int f = (int)frame;
        u0 = (f % _cols) * du;
        v0 = (f / _cols) * dv;
    }

    /// <summary>
    /// One quad: the near pair at the particle, the far pair down the velocity. Stock lays the corners
    /// out as -side, +side at the near end then -side, +side at the far end (<c>1002aefd</c>).
    /// </summary>
    void AddRibbon(
        Vector3 near, Vector3 far, Vector3 side, float nearWidth, float farWidth,
        Color32 nearColour, Color32 farColour,
        float u0, float v0, float du, float dv, Vector3 eye, ref int count)
    {
        EffectBillboardBatch.Strip strip = _strip;
        int v = count;
        strip.Positions[v] = near - side * nearWidth;
        strip.Positions[v + 1] = near + side * nearWidth;
        strip.Positions[v + 2] = far - side * farWidth;
        strip.Positions[v + 3] = far + side * farWidth;
        strip.Colors[v] = nearColour;
        strip.Colors[v + 1] = nearColour;
        strip.Colors[v + 2] = farColour;
        strip.Colors[v + 3] = farColour;

        // 1002adcb / 1002ae1e, in D3D's (tu, tv).
        float a0, b0, a1, b1, a2, b2, a3, b3;
        if (!_swapUv)
        {
            a0 = u0; b0 = v0 + dv;
            a1 = u0 + du; b1 = v0 + dv;
            a2 = u0; b2 = v0;
            a3 = u0 + du; b3 = v0;
        }
        else
        {
            a0 = u0; b0 = v0;
            a1 = u0; b1 = v0 + dv;
            a2 = u0 + du; b2 = v0;
            a3 = u0 + du; b3 = v0 + dv;
        }
        if (_flipV)
        {
            // 1002ae68: the near pair and the far pair trade their v.
            (b0, b2) = (b2, b0);
            (b1, b3) = (b3, b1);
        }
        // D3D (tu, tv) with tv flipped for Unity.
        strip.Uvs[v] = new Vector2(a0, 1f - b0);
        strip.Uvs[v + 1] = new Vector2(a1, 1f - b1);
        strip.Uvs[v + 2] = new Vector2(a2, 1f - b2);
        strip.Uvs[v + 3] = new Vector2(a3, 1f - b3);

        if (_edgeFade)
            EdgeFade(v, eye);

        count = v + 4;
    }

    /// <summary>
    /// Flag 0x40000 (<c>1002aaa3</c>): every corner's alpha scaled by |quad normal . view|, so the
    /// ribbon dims as it turns edge-on. The view direction is from the camera to the visual's own
    /// origin, which for these particles is the world origin — stock keeps their vertices in world
    /// space under an identity transform, so its object-space step is a no-op (Docs §9). No record
    /// sets this flag.
    /// </summary>
    void EdgeFade(int v, Vector3 eye)
    {
        Vector3 view = -eye;
        if (view == Vector3.zero)
            return;
        view.Normalize();

        Vector3 edgeA = (_strip.Positions[v + 1] - _strip.Positions[v]).normalized;
        Vector3 edgeB = (_strip.Positions[v + 2] - _strip.Positions[v]).normalized;
        Vector3 normal = Vector3.Cross(edgeA, edgeB);
        if (normal == Vector3.zero)
            return;
        normal.Normalize();

        float scale = Mathf.Abs(Vector3.Dot(normal, view));
        for (int k = 0; k < 4; k++)
        {
            Color32 c = _strip.Colors[v + k];
            c.a = (byte)Mathf.Clamp(Mathf.RoundToInt(c.a * scale), 0, 255);
            _strip.Colors[v + k] = c;
        }
    }
}
