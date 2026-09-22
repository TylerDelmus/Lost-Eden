using System;

/// <summary>
/// Stock <c>_GfxControlShockWave_t</c> (typeCode 3000, 0xbb8; vftable <c>Gamecode 1016da14</c>, dynel ctor
/// <c>100eeec0</c>, loader <c>100ee3cc</c>, init <c>100eeacb</c>, Process <c>100ee525</c>) with its
/// DisplaySystem visuals <c>GfxVisualGroundRing</c> (ctor <c>100172bd</c>, Update <c>10016e84</c>, draw
/// <c>10017223</c>) and <c>GfxVisualCone</c> (ctor <c>1000c196</c>, build <c>1000bd0a</c>, draw
/// <c>1000c0ec</c>): rings that spread over the ground one after another, e.g. 43103, the hit of 152838
/// Magnified Psychic Hammer, with thin beams flashing up at the start of each.
///
/// Fields: 0 flags, 8 ring life (the base duration, +0x10), 9 ring material, 10 segments N, 11 rings,
/// 12/16 inner radius start/end, 13/17 outer radius start/end, 14/18 inner colour start/end, 15/19 outer
/// colour start/end (D3DCOLORs), 20/21 the ring's U/V scale, 22 height above the ground, 23 period,
/// 24 cones, 25 cone material, 26 cone segments, 27/28 cone U/V scale, 29 cone height, 30/31 cone
/// radius bottom/top, 32 per-cone step, 33 per-cone taper, 34/35 cone colour bottom/top.
/// Flags: bit 0 the centre follows the dynel, 0x800 the rings are additive, 0x1000 the ring's UVs run
/// round it (else planar over the ground), 0x2000 radii clamp at 0, 0x4000 the cone's UVs swap.
///
/// Ring i runs over t = (age - period i) / life in [0, 1]: radii and colours go start to end (colours
/// through randy31's byte-wise Color_t: each channel _ftol(c s + 0.5), clamped, then added), and its
/// N + 1 inner/outer pairs sit on the circle round its centre at the ground's height (+ field 22). The
/// centre is taken the first time the ring runs (or, with cones, while the first 0.3 of its period).
/// Past t = 1 the ring is deleted. Any ring updated in a call clears the ready flag, so the control lives
/// as long as its rings, whatever the base expiry says; slot 6 only raises the flag.
///
/// Cone j: bottom radius f30 + j f32, top f31 f33^j + j f32, height f29. Each period p = age / period,
/// frac = p mod 1: the bottom colour's alpha times 10 frac, then 1, then 1 - (frac - 0.2) / 0.8 (from
/// 0.1 and 0.2 on); the top's times 10 frac, then 2 - 10 frac, then 0. The cones move to the centre while
/// frac &lt; 0.1 and go once p reaches the ring count.
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class ShockWaveSim
{
    public const int FlagFollow = 1;
    public const int FlagRingAdditive = 0x800;
    public const int FlagRingUvAround = 0x1000;
    public const int FlagClampRadii = 0x2000;
    public const int FlagConeUvSwap = 0x4000;

    // 0x1016da68 / 0x1016da0c: a ring centre not taken yet, and the test for it.
    const float UnsetCentre = 1.0000000200408773e+20f;
    const float UnsetTest = 9.999999980506448e+18f;
    const double TwoPi = 6.2831854820251465;

    public sealed class Ring
    {
        public bool Alive = true;
        /// <summary>Updated at least once (the visual draws nothing before its first Update).</summary>
        public bool Drawn;
        public float X = UnsetCentre, Y, Z;
        public uint Inner, Outer;
        /// <summary>N + 1 pairs, inner then outer, x y z relative to the centre.</summary>
        public float[] Vertices;
        /// <summary>u v per vertex, as D3D (v down the image).</summary>
        public float[] Uvs;
    }

    public sealed class Cone
    {
        public bool Alive = true;
        public float X, Y, Z;
        public uint Bottom, Top;
        public float Height, BottomRadius, TopRadius;
        /// <summary>N + 1 pairs, bottom then top, relative to the cone's position.</summary>
        public float[] Vertices;
        public float[] Uvs;
    }

    readonly Func<float, float, float, float> _ground;
    readonly float _r1a, _r2a, _r1b, _r2b;
    readonly uint _c1a, _c2a, _c1b, _c2b;
    readonly float _uScale, _vScale, _lift, _period;
    readonly uint _coneA, _coneB;
    bool _ready;

    public int Flags { get; }
    public int RingMaterial { get; }
    public int Segments { get; }
    public int ConeMaterial { get; }
    public int ConeSegments { get; }
    public Ring[] Rings { get; }
    public Cone[] Cones { get; }

    /// <summary>+0x10: field 8, or what SetDuration puts there (slot 8, <c>1010340b</c>).</summary>
    public float Life { get; set; }

    public bool RingsAdditive => (Flags & FlagRingAdditive) != 0;

    /// <param name="cx">The centre at creation (+0x78): the dynel's position.</param>
    /// <param name="ground">Stock's ground height under a point (<c>100d33f3</c>); NaN when there is none.</param>
    public ShockWaveSim(float[] fields, float cx, float cy, float cz, Func<float, float, float, float> ground)
    {
        _ground = ground;
        Flags = Int(fields, 0);
        Life = F(fields, 8);
        RingMaterial = Int(fields, 9);
        Segments = Math.Max(0, Int(fields, 10));
        int rings = Math.Max(0, Int(fields, 11));
        _r1a = F(fields, 12);
        _r2a = F(fields, 13);
        _c1a = (uint)Int(fields, 14);
        _c2a = (uint)Int(fields, 15);
        _r1b = F(fields, 16);
        _r2b = F(fields, 17);
        _c1b = (uint)Int(fields, 18);
        _c2b = (uint)Int(fields, 19);
        _uScale = F(fields, 20);
        _vScale = F(fields, 21);
        _lift = F(fields, 22);
        _period = F(fields, 23);
        int cones = Math.Max(0, Int(fields, 24));
        ConeMaterial = Int(fields, 25);
        ConeSegments = Math.Max(0, Int(fields, 26));
        _coneA = (uint)Int(fields, 34);
        _coneB = (uint)Int(fields, 35);

        Rings = new Ring[rings];
        for (int i = 0; i < rings; i++)
        {
            Rings[i] = new Ring
            {
                Vertices = new float[(Segments + 1) * 6],
                Uvs = new float[(Segments + 1) * 4],
            };
        }

        // 100eebd1: the cones, each at the centre with no colour yet.
        float coneU = F(fields, 27), coneV = F(fields, 28);
        float height = F(fields, 29), bottom = F(fields, 30), top = F(fields, 31);
        float step = F(fields, 32), taper = F(fields, 33);
        bool swap = (Flags & FlagConeUvSwap) != 0;
        Cones = new Cone[cones];
        for (int j = 0; j < cones; j++)
        {
            double along = (double)j * step;
            var cone = new Cone
            {
                X = cx, Y = cy, Z = cz,
                Height = height,
                BottomRadius = (float)(bottom + along),
                TopRadius = (float)(Math.Pow(taper, j) * top + along),
            };
            BuildCone(cone, ConeSegments, coneU, coneV, swap);
            Cones[j] = cone;
        }
    }

    /// <summary>Stock slot 6 (the base <c>100a76f0</c>): only the ready flag, which a running ring clears.</summary>
    public void Terminate() => _ready = true;

    /// <summary>
    /// One Process at <paramref name="age"/> with the centre (+0x78) at (cx, cy, cz). Returns the ready flag.
    /// </summary>
    public bool Step(float age, float cx, float cy, float cz)
    {
        // _GfxControl_t::Process (100d2a86): ready once 0 <= duration < age.
        if (0f <= Life && Life < age)
            _ready = true;

        StepCones(age, cx, cy, cz);

        for (int i = 0; i < Rings.Length; i++)
        {
            Ring ring = Rings[i];
            if (!ring.Alive)
                continue;

            float t = (float)(((double)age - (double)_period * i) / Life);
            if (1f < t)
            {
                ring.Alive = false;
                continue;
            }
            if (!(0f < t || 0f == t))
                continue;

            if (!(UnsetTest > ring.X))
            {
                ring.X = cx;
                ring.Y = cy;
                ring.Z = cz;
            }

            float back = (float)(1.0 - t);
            ring.Inner = Add(Scale(_c1a, back), Scale(_c1b, t));
            ring.Outer = Add(Scale(_c2a, back), Scale(_c2b, t));
            float r1 = (float)((double)_r1a * back + (double)_r1b * t);
            float r2 = (float)((double)_r2a * back + (double)_r2b * t);
            if ((Flags & FlagClampRadii) != 0)
            {
                if (!(0f < r1 || 0f == r1))
                    r1 = 0f;
                if (!(0f < r2 || 0f == r2))
                    r2 = 0f;
            }

            BuildRing(ring, r1, r2);
            ring.Drawn = true;
            _ready = false;
        }

        return _ready;
    }

    /// <summary>100ee569..100ee709.</summary>
    void StepCones(float age, float cx, float cy, float cz)
    {
        if (Cones.Length == 0 || !Cones[0].Alive)
            return;

        float p = (float)((double)age / _period);
        int n = (int)p;
        if (n >= Rings.Length)
        {
            for (int j = 0; j < Cones.Length; j++)
                Cones[j].Alive = false;
            return;
        }

        // 1013f2ec: fmod, which keeps the dividend's sign, as C#'s % on doubles does.
        float frac = (float)((double)p % 1.0);
        float bottom, top;
        if (frac < 0.10000000149011612)
        {
            bottom = (float)(frac * 10.0);
            top = bottom;
        }
        else if (frac < 0.20000000298023224)
        {
            bottom = 1f;
            top = (float)(2.0 - frac * 10.0);
        }
        else
        {
            bottom = (float)(1.0 - (frac - 0.20000000298023224) / 0.800000011920929);
            if (!(0f < bottom || 0f == bottom))
                bottom = 0f;
            top = 0f;
        }

        // 1013f276 (_ftol2) on alpha * factor, into the colour's top byte.
        uint a = WithAlpha(_coneA, (int)((double)(_coneA >> 24) * bottom));
        uint b = WithAlpha(_coneB, (int)((double)(_coneB >> 24) * top));
        for (int j = 0; j < Cones.Length; j++)
        {
            Cone cone = Cones[j];
            cone.Bottom = a;
            cone.Top = b;
            if (frac < 0.10000000149011612)
            {
                cone.X = cx;
                cone.Y = cy;
                cone.Z = cz;
            }
        }

        // 100ee6bc: the period's ring takes the centre while it's young.
        if (frac < 0.30000001192092896)
        {
            Ring ring = Rings[n];
            ring.X = cx;
            ring.Y = cy;
            ring.Z = cz;
        }
    }

    /// <summary>100ee852..100ee94e, then GfxVisualGroundRing::Update (10016e84) for the UVs.</summary>
    void BuildRing(Ring ring, float r1, float r2)
    {
        int n = Segments;
        float step = n > 0 ? (float)(TwoPi / n) : 0f;
        float a = 0f;
        float uStep = n > 0 ? (float)((double)_uScale / n) : 0f;
        float u = 0f;
        bool around = (Flags & FlagRingUvAround) != 0;
        for (int k = 0; k <= n; k++)
        {
            float dx = (float)Math.Cos(a), dz = (float)Math.Sin(a);
            int o = k * 6;
            Place(ring, dx, dz, r1, ring.Vertices, o);
            Place(ring, dx, dz, r2, ring.Vertices, o + 3);

            int uv = k * 4;
            if (around)
            {
                ring.Uvs[uv] = u;
                ring.Uvs[uv + 1] = 0f;
                ring.Uvs[uv + 2] = u;
                ring.Uvs[uv + 3] = _vScale;
                u = (float)((double)uStep + u);
            }
            else
            {
                ring.Uvs[uv] = (float)(((double)ring.Vertices[o] + ring.X) * _uScale);
                ring.Uvs[uv + 1] = (float)(((double)ring.Vertices[o + 2] + ring.Z) * _vScale);
                ring.Uvs[uv + 2] = (float)(((double)ring.Vertices[o + 3] + ring.X) * _uScale);
                ring.Uvs[uv + 3] = (float)(((double)ring.Vertices[o + 5] + ring.Z) * _vScale);
            }
            a = (float)((double)step + a);
        }
    }

    /// <summary>A point dir r from the ring's centre, raised to the ground there plus field 22.</summary>
    void Place(Ring ring, float dx, float dz, float r, float[] dest, int o)
    {
        float x = dx * r, z = dz * r;
        float wx = x + ring.X, wy = 0f + ring.Y, wz = z + ring.Z;
        float g = _ground != null ? _ground(wx, wy, wz) : float.NaN;
        // The port: with no ground under the point, the ground is at the centre's height.
        if (float.IsNaN(g))
            g = ring.Y;
        dest[o] = x;
        dest[o + 1] = (float)((double)g + _lift - ring.Y);
        dest[o + 2] = z;
    }

    /// <summary>GfxVisualCone's build (<c>1000bd0a</c>) and its cos / sin tables (ctor <c>1000c292</c>).</summary>
    static void BuildCone(Cone cone, int n, float uScale, float vScale, bool swap)
    {
        cone.Vertices = new float[(n + 1) * 6];
        cone.Uvs = new float[(n + 1) * 4];
        float uStep = n > 0 ? (float)((double)(swap ? vScale : uScale) / n) : 0f;
        float along = 0f;
        for (int k = 0; k <= n; k++)
        {
            float angle = n > 0 ? (float)(k * TwoPi / n) : 0f;
            float c = (float)Math.Cos(angle), s = (float)Math.Sin(angle);
            int o = k * 6;
            cone.Vertices[o] = c * cone.BottomRadius;
            cone.Vertices[o + 1] = 0f;
            cone.Vertices[o + 2] = s * cone.BottomRadius;
            cone.Vertices[o + 3] = c * cone.TopRadius;
            cone.Vertices[o + 4] = cone.Height;
            cone.Vertices[o + 5] = s * cone.TopRadius;

            int uv = k * 4;
            if (!swap)
            {
                cone.Uvs[uv] = along;
                cone.Uvs[uv + 1] = 0f;
                cone.Uvs[uv + 2] = along;
                cone.Uvs[uv + 3] = vScale;
            }
            else
            {
                cone.Uvs[uv] = 0f;
                cone.Uvs[uv + 1] = along;
                cone.Uvs[uv + 2] = uScale;
                cone.Uvs[uv + 3] = along;
            }
            along = (float)((double)uStep + along);
        }
    }

    /// <summary>randy31 <c>Color_t::operator*(float)</c> (<c>10019c93</c>): each byte _ftol(c s + 0.5), clamped.</summary>
    public static uint Scale(uint c, float s)
    {
        uint result = 0;
        for (int shift = 0; shift < 32; shift += 8)
        {
            int v = (int)(((c >> shift) & 0xff) * (double)s + 0.5);
            if (v < 0)
                v = 0;
            if (v > 255)
                v = 255;
            result |= (uint)v << shift;
        }
        return result;
    }

    /// <summary>randy31 <c>Color_t::operator+</c> (<c>10019bb1</c>): each byte summed, clamped at 255.</summary>
    public static uint Add(uint a, uint b)
    {
        uint result = 0;
        for (int shift = 0; shift < 32; shift += 8)
        {
            int v = (int)((a >> shift) & 0xff) + (int)((b >> shift) & 0xff);
            if (v > 255)
                v = 255;
            result |= (uint)v << shift;
        }
        return result;
    }

    static uint WithAlpha(uint c, int alpha) => (c & 0x00ffffffu) | ((uint)(alpha & 0xff) << 24);

    static float F(float[] f, int i) => f != null && i >= 0 && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i >= 0 && i < f.Length ? BitConverter.SingleToInt32Bits(f[i]) : 0;
}
