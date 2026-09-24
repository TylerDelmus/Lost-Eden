using System;

/// <summary>
/// Stock <c>_GfxControlGlobalSmoke_t</c> (type 3017, 0xbc9; vftable <c>Gamecode 1016cec4</c>, object
/// 0x820, ctor <c>100e09fe</c>, loader <c>100e060f</c>, build <c>100e0738</c>, Process
/// <c>100dfc38</c>) driving a DisplaySystem <c>GfxVisualSol</c> of **64** sprites — the same visual
/// Suns uses (§5.10), just with twice the sprites.
///
/// A steady emitter: every call it revives as many dead slots as its budget allows, then walks all 64,
/// moving each by its velocity and reading its width, height, colour and frame off its own age. A
/// particle lives <c>field 26</c> seconds.
///
/// Fields: 0 flags, 8 duration, 9 material, 10 the kind, 11, 12, 13 the launch speed, 14/15 the width
/// at birth and death, 16/17 the height, 18-25 the colour ramp (<see cref="StockColorRamp"/>, the same
/// <c>101085de</c> loader), 26 the particle's life, 27, 28, 29 gravity, 30/31 the spawn budget.
///
/// Only the two kinds a nano reaches are ported:
/// <list type="bullet">
/// <item><b>0</b> (<c>100e0249</c>) a fountain: a random unit direction forced upwards, times field 13,
/// from a point up to a metre under the emitter.</item>
/// <item><b>2</b> (<c>100e0391</c>) a drift: straight out along +x at field 13 with a little spread,
/// sagging at field 13 × 0.4, and then pulled by field 29.</item>
/// </list>
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class GlobalSmokeSim
{
    /// <summary>The visual is built with 64 sprites (<c>100e0814</c> pushes 0x40).</summary>
    public const int Capacity = 64;

    /// <summary>Stock's birth stamp for a slot that has never lived (<c>100e085a</c>).</summary>
    public const float NeverBorn = -100f;

    public struct Particle
    {
        public bool Alive;
        public float Birth;
        public float X, Y, Z;
        public float VelX, VelY, VelZ;
        public float Width, Height, Angle, Frame;
        public uint Argb;
    }

    public readonly int Flags;
    public readonly float Duration;
    public readonly int Material;
    public readonly int Kind;
    public readonly float Field11, Field12;
    public readonly float Speed;         // field 13
    public readonly float WidthFrom, WidthTo, HeightFrom, HeightTo;
    public readonly float ParticleLife;  // field 26
    public readonly float Field27, Field28;
    public readonly float Gravity;       // field 29
    public readonly int BudgetBase;      // field 30
    public readonly int BudgetMask;      // field 31

    readonly float[] _rampStart = new float[4];
    readonly float[] _rampEnd = new float[4];
    readonly Func<double> _random;
    readonly Particle[] _particles = new Particle[Capacity];

    /// <summary>The material's frame count, which the build reads once (<c>100e0841</c>).</summary>
    public float FrameCount { get; set; }

    public Particle[] Particles => _particles;

    /// <summary>The kinds this port reproduces; the rest draw nothing.</summary>
    public static bool IsPortedKind(int kind) => kind == 0 || kind == 2;

    public bool Supported => IsPortedKind(Kind);

    public GlobalSmokeSim(float[] fields, Func<double> random)
    {
        _random = random ?? (() => 0.5);
        Flags = GfxBits.Of(fields, 0);
        Duration = F(fields, 8);
        Material = GfxBits.Of(fields, 9);
        Kind = GfxBits.Of(fields, 10);
        Field11 = F(fields, 11);
        Field12 = F(fields, 12);
        Speed = F(fields, 13);
        WidthFrom = F(fields, 14);
        WidthTo = F(fields, 15);
        HeightFrom = F(fields, 16);
        HeightTo = F(fields, 17);
        // 100e06d6 -> 101085de: fields 18-21 are the start A,R,G,B and 22-25 the end.
        for (int c = 0; c < 4; c++)
        {
            _rampStart[c] = F(fields, 18 + c);
            _rampEnd[c] = F(fields, 22 + c);
        }
        ParticleLife = F(fields, 26);
        Field27 = F(fields, 27);
        Field28 = F(fields, 28);
        Gravity = F(fields, 29);
        BudgetBase = GfxBits.Of(fields, 30);
        BudgetMask = GfxBits.Of(fields, 31);

        // 100e085a: every slot starts long dead so the first call can fill them all.
        for (int i = 0; i < Capacity; i++)
            _particles[i].Birth = NeverBorn;
    }

    /// <summary>
    /// 100dfd15: the emitter stops once the last particle it could start would outlive the effect, so
    /// the smoke ends exactly on the duration rather than being cut off. A negative duration never
    /// stops.
    /// </summary>
    public bool Emitting(float age) => !(Duration - ParticleLife < age) || !(0f < Duration);

    /// <summary>How many slots may be revived this call (<c>100dfcd7</c>).</summary>
    public int Budget(float age)
    {
        if (!Emitting(age))
            return 0;
        int mask = BudgetMask;
        int noise = mask == 0 ? 0 : (int)(_random() * 32768.0) & mask;
        return BudgetBase + noise;
    }

    /// <summary>One stock Process (<c>100dfc38</c>) at the emitter, with the step it advances by.</summary>
    public void Process(float age, float dt, float emitterX, float emitterY, float emitterZ)
    {
        if (!Supported)
            return;

        int budget = Budget(age);
        float dw = WidthTo - WidthFrom;
        float dh = HeightTo - HeightFrom;

        for (int i = 0; i < Capacity; i++)
        {
            ref Particle p = ref _particles[i];
            if (p.Alive)
            {
                float t = (age - p.Birth) / ParticleLife;
                // 100dffbd: past its life the slot simply goes dead and is free again next call.
                if (1f < t)
                {
                    p.Alive = false;
                    continue;
                }

                // 1007030f is a plain scale, so this is the ordinary velocity step.
                p.X += p.VelX * dt;
                p.Y += p.VelY * dt;
                p.Z += p.VelZ * dt;

                p.Width = t * dw + WidthFrom;
                p.Height = t * dh + HeightFrom;
                p.Argb = StockColorRamp.Eval(_rampStart, _rampEnd, t);
                // 100e005b: _ftol truncates, so the last frame is only reached at t = 1.
                p.Frame = (int)(FrameCount * t);
                p.Angle = Angle(i, age);

                // 100e00ba: only kinds 2 and 4 are pulled.
                if (Kind == 2 || Kind == 4)
                    p.VelY += Gravity * dt;
                continue;
            }

            if (budget == 0)
                continue;

            p.Birth = age;
            p.Alive = true;
            p.Width = WidthFrom;
            p.Height = HeightFrom;
            p.Argb = StockColorRamp.Eval(_rampStart, _rampEnd, 0f);
            p.Frame = 0f;
            p.Angle = Angle(i, age);
            Spawn(ref p, emitterX, emitterY, emitterZ);
            budget--;
        }
    }

    /// <summary>
    /// 100e012f: the screen angle is a function of the slot and the effect's own age, so neighbouring
    /// sprites lean opposite ways and the whole sheet keeps turning.
    /// </summary>
    public static float Angle(int slot, float age) => ((slot & 1) - 0.5f) * age * (slot - 4);

    void Spawn(ref Particle p, float ex, float ey, float ez)
    {
        if (Kind == 2)
        {
            // 100e0391: out along +x with a tenth of spread, sagging, and no z at all.
            p.VelX = (float)(_random() * (65535.0 / 655350.0) + 1.0) * Speed;
            p.VelY = Speed * -0.4f;
            p.VelZ = 0f;
            p.X = ex;
            p.Y = ey;
            p.Z = ez;
            return;
        }

        // 100e0249: a random unit direction, flipped up if it points down, then scaled by field 13.
        RandomUnit(out float dx, out float dy, out float dz);
        if (0f > dy)
            dy = -dy;
        p.VelX = dx * Speed;
        p.VelY = dy * Speed;
        p.VelZ = dz * Speed;
        p.X = ex;
        // 100e02a8: the spawn starts up to a metre below the emitter so the column has depth.
        p.Y = ey - (float)_random();
        p.Z = ez;
    }

    /// <summary>
    /// 100d3005 keeps a pool of 2048 unit vectors, each drawn by rejecting a cube sample outside the
    /// unit sphere and then normalising. The pool is only there to save work, so drawing one the same
    /// way is the same distribution.
    /// </summary>
    void RandomUnit(out float x, out float y, out float z)
    {
        for (int attempt = 0; attempt < 32; attempt++)
        {
            double ax = _random() * 2.0 - 1.0;
            double ay = _random() * 2.0 - 1.0;
            double az = _random() * 2.0 - 1.0;
            double len2 = ax * ax + ay * ay + az * az;
            if (len2 > 1.0 || len2 <= 0.0)
                continue;
            double len = Math.Sqrt(len2);
            x = (float)(ax / len);
            y = (float)(ay / len);
            z = (float)(az / len);
            return;
        }
        x = 0f;
        y = 1f;
        z = 0f;
    }

    static float F(float[] f, int i) => f != null && i >= 0 && i < f.Length ? f[i] : 0f;
}
