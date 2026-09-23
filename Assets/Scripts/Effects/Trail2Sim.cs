using System;

/// <summary>
/// Stock <c>GfxControlTrail2_t</c> (typeCode 3039, 0xbdf; vftable <c>Gamecode 1016faf4</c>, loader
/// <c>1011518a</c>, init <c>10115260</c>, Process <c>10115605</c>) and its visual, DisplaySystem's
/// <c>GfxVisualTrail2</c> (ctor <c>1002d040</c>, Sample <c>1002c7bd</c>, build <c>1002c918</c>, frame update
/// <c>1002d169</c>): a ribbon trail of the locator's frame, sampled once a call, e.g. in the Phasefront and Yalmaha
/// vehicle buffs (72301 / 72302 on attractors 2006 / 2007).
///
/// Fields: 0 flags, 8 duration, 10 material, 11 samples N, 12 sample interval (0: one a call), 13 rate of
/// the curves' clock, 14 thrust, 15 fluctuation, then from 16 a colour curve and two float curves (x and y
/// size), each a key count and (time, value) pairs (<c>101166f2</c>).
/// Flags: bits 0-2 the locator's; 0x400 the texture runs along v (else along u); 0x800 a sample taken
/// while the dynel moves backwards is black; 0x1000 the samples drift along the origin's z.
///
/// Per call, with t = fmod(field 13 · age, 1): the frame's x axis × the x curve at t and its y axis × the y
/// curve; the colour is the colour curve at t. With 0x800, d = the frame's place − the place of the sample at
/// the write index (the oldest once the ring is full); unless d is (0, 0, 0), d is set to length 1 and a
/// d · (the dynel's z) below -0.75 makes the colour 0. Then SetOrigin, and one Sample (or, with field 12
/// above 0, one per field 12 seconds of dt carried over).
///
/// The ring (<c>+0x1b8</c>, N samples of a matrix and a colour): Reset fills it and zeroes the write index and
/// the count. Sample writes at the index, moves it on (wrapping at N), counts up to N, and while the ring
/// isn't full rewrites samples 0 .. N - count - 1 with the same sample (not the unused ones, so the first
/// full ring holds 9 8 7 6 5 6 7 8 9 10 for N = 10: the trail folds until N more samples pass). The build
/// draws count + 1 points: point i &lt; N - 1 is sample (index + i) % N, the rest the origin; each point is a
/// pair of vertices on four
/// strips (±y, ±x, -x+y / +x-y, +x+y / -x-y about the place), u = i / count (v with 0x400).
///
/// With 0x1000 the visual's frame update moves samples 0 .. count - 1 by the origin's z axis ×
/// (field 14 + field 15 · r) · the frame's dt, r in [0, 1).
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class Trail2Sim
{
    public const int FlagAlongV = 0x400;
    public const int FlagDarkReversing = 0x800;
    public const int FlagThrust = 0x1000;

    /// <summary>1016fb48.</summary>
    public const float ReversingDot = -0.75f;

    public const int StripCount = 4;

    public struct V3
    {
        public float X, Y, Z;
        public V3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public bool IsZero => X == 0f && Y == 0f && Z == 0f;
    }

    /// <summary>A sample: the frame's (scaled) x and y axes, its z axis, its place, and a D3DCOLOR.</summary>
    public struct Sample
    {
        public V3 AxisX, AxisY, AxisZ, Place;
        public uint Colour;
    }

    public sealed class Strip
    {
        public float[] Positions;
        public float[] Uvs;
        public uint[] Colours;
        public int Count;
    }

    readonly Sample[] _ring;
    readonly StockColorCurve _colour;
    readonly StockFloatCurve _sizeX, _sizeY;
    Sample _origin;
    int _index;
    int _count;
    float _carried;

    public int Flags { get; }
    public int Material { get; }
    public int Length => _ring.Length;
    public float Interval { get; }
    public float Rate { get; }
    public float Thrust { get; }
    public float Fluctuate { get; }
    public float Duration { get; }

    public int Index => _index;
    public int Count => _count;
    public Sample Origin => _origin;
    public Sample this[int i] => _ring[i];

    public Trail2Sim(float[] fields)
    {
        Flags = Int(fields, 0);
        Duration = F(fields, 8);
        Material = Int(fields, 10);
        Interval = F(fields, 12);
        Rate = F(fields, 13);
        Thrust = F(fields, 14);
        Fluctuate = F(fields, 15);
        _ring = new Sample[Math.Max(0, Int(fields, 11))];

        int index = 16;
        _colour = StockColorCurve.Read(fields, ref index);
        _sizeX = StockFloatCurve.Read(fields, ref index);
        _sizeY = StockFloatCurve.Read(fields, ref index);
    }

    /// <summary><c>GfxVisualTrail2::Reset</c> (<c>1002c8c8</c>): every sample is this one; nothing counted.</summary>
    public void Reset(Sample sample)
    {
        _index = 0;
        _count = 0;
        for (int i = 0; i < _ring.Length; i++)
            _ring[i] = sample;
    }

    /// <summary><c>GfxVisualTrail2::SetOrigin</c> (<c>1002c8a4</c>).</summary>
    public void SetOrigin(Sample sample) => _origin = sample;

    /// <summary><c>GfxVisualTrail2::Sample</c> (<c>1002c7bd</c>).</summary>
    public void Push(Sample sample)
    {
        int n = _ring.Length;
        if (n == 0)
            return;
        _ring[_index] = sample;
        _index++;
        if (_count < _index)
            _count++;
        if (_index >= n)
            _index = 0;
        if (_count < n)
        {
            for (int i = 0; i < n - _count; i++)
                _ring[i] = sample;
        }
    }

    /// <summary><c>GfxVisualTrail2::GetCurrentSample</c> (<c>1002c86e</c>): the sample at the write index.</summary>
    public Sample Current => _ring.Length > 0 ? _ring[_index] : default;

    /// <summary>
    /// The control's head for this call (<c>101156a2</c>..<c>10115834</c>): <paramref name="frame"/> is the
    /// locator's frame (its place in <see cref="Sample.Place"/>); <paramref name="forward"/> the dynel's z axis.
    /// False when stock skips the sample (a zero forward, which a turn never gives).
    /// </summary>
    public bool Head(float age, Sample frame, V3 forward, out Sample head)
    {
        // 101156a2: fmod(field 13 · age, 1).
        float clock = (float)(Rate * (double)age);
        float t = (float)((double)clock % 1.0);
        float sx = _sizeX.Evaluate(t);
        float sy = _sizeY.Evaluate(t);

        head = frame;
        head.AxisX = new V3((float)(frame.AxisX.X * (double)sx), (float)(frame.AxisX.Y * (double)sx), (float)(frame.AxisX.Z * (double)sx));
        head.AxisY = new V3((float)(frame.AxisY.X * (double)sy), (float)(frame.AxisY.Y * (double)sy), (float)(frame.AxisY.Z * (double)sy));

        if ((Flags & FlagDarkReversing) == 0)
        {
            head.Colour = _colour.Evaluate(t);
            return true;
        }

        V3 last = Current.Place;
        var d = new V3((float)((double)frame.Place.X - last.X), (float)((double)frame.Place.Y - last.Y), (float)((double)frame.Place.Z - last.Z));
        if (d.IsZero)
        {
            head.Colour = _colour.Evaluate(t);
            return true;
        }

        d = Normalise(d);
        if (forward.IsZero)
            return false;

        float dot = (float)((double)d.Y * forward.Y + (double)forward.X * d.X + (double)d.Z * forward.Z);
        head.Colour = dot < ReversingDot ? 0u : _colour.Evaluate(t);
        return true;
    }

    /// <summary>
    /// <c>101150ff</c>: SetOrigin, then one Sample, or with field 12 above 0 one per field 12 seconds of dt
    /// carried over.
    /// </summary>
    public void Take(Sample head, float dt)
    {
        SetOrigin(head);
        if (!(0f < Interval))
        {
            Push(head);
            return;
        }

        _carried = (float)((double)dt + _carried);
        while (Interval <= _carried)
        {
            Push(head);
            _carried = (float)((double)_carried - Interval);
        }
    }

    /// <summary>
    /// 1002d169 with flag 0x1000: samples 0 .. count - 1 move by the origin's z axis ×
    /// (thrust + fluctuation · <paramref name="r"/>) · <paramref name="frameDt"/>.
    /// </summary>
    public void Drift(float frameDt, float r)
    {
        if ((Flags & FlagThrust) == 0)
            return;
        float k = (float)(((double)Fluctuate * r + 0.0 + Thrust) * frameDt);
        V3 z = _origin.AxisZ;
        var move = new V3((float)(z.X * (double)k), (float)(z.Y * (double)k), (float)(z.Z * (double)k));
        for (int i = 0; i < _count && i < _ring.Length; i++)
        {
            ref V3 p = ref _ring[i].Place;
            p = new V3((float)((double)p.X + move.X), (float)((double)p.Y + move.Y), (float)((double)p.Z + move.Z));
        }
    }

    /// <summary>How many points the build draws: count + 1 (a strip needs two).</summary>
    public int PointCount => _count + 1;

    /// <summary>Point <paramref name="i"/> of the build: sample (index + i) % N below N - 1, else the origin.</summary>
    public Sample Point(int i)
    {
        int n = _ring.Length;
        return i < n - 1 ? _ring[(_index + i) % n] : _origin;
    }

    /// <summary>New strips sized for this trail: 2 (N + 1) vertices each.</summary>
    public Strip[] NewStrips()
    {
        var strips = new Strip[StripCount];
        int vertices = 2 * (_ring.Length + 1);
        for (int s = 0; s < StripCount; s++)
        {
            strips[s] = new Strip
            {
                Positions = new float[vertices * 3],
                Uvs = new float[vertices * 2],
                Colours = new uint[vertices],
            };
        }
        return strips;
    }

    /// <summary>
    /// The build (<c>1002c918</c>): per point a vertex pair on each strip, D3D uvs (v down). Strip 0 is
    /// (P + Y, P - Y), 1 (P - X, P + X), 2 (P - X + Y, P + X - Y), 3 (P + X + Y, P - X - Y).
    /// </summary>
    public void Build(Strip[] strips)
    {
        int points = PointCount;
        bool alongV = (Flags & FlagAlongV) != 0;
        for (int i = 0; i < points; i++)
        {
            Sample p = Point(i);
            float along = (float)((float)i / (double)_count);
            V3 t = p.Place, x = p.AxisX, y = p.AxisY;
            Pair(strips[0], i, Add(t, y), Sub(t, y), along, alongV, p.Colour);
            Pair(strips[1], i, Sub(t, x), Add(t, x), along, alongV, p.Colour);
            Pair(strips[2], i, Add(Sub(t, x), y), Sub(Add(t, x), y), along, alongV, p.Colour);
            Pair(strips[3], i, Add(Add(t, x), y), Sub(Sub(t, x), y), along, alongV, p.Colour);
        }
        for (int s = 0; s < StripCount; s++)
            strips[s].Count = 2 * points;
    }

    static void Pair(Strip strip, int i, V3 a, V3 b, float along, bool alongV, uint colour)
    {
        int v = 2 * i;
        Put(strip, v, a, alongV ? 0f : along, alongV ? along : 0f, colour);
        Put(strip, v + 1, b, alongV ? 1f : along, alongV ? along : 1f, colour);
    }

    static void Put(Strip strip, int v, V3 p, float u, float w, uint colour)
    {
        strip.Positions[v * 3] = p.X;
        strip.Positions[v * 3 + 1] = p.Y;
        strip.Positions[v * 3 + 2] = p.Z;
        strip.Uvs[v * 2] = u;
        strip.Uvs[v * 2 + 1] = w;
        strip.Colours[v] = colour;
    }

    static V3 Add(V3 a, V3 b) => new V3((float)((double)b.X + a.X), (float)((double)b.Y + a.Y), (float)((double)b.Z + a.Z));
    static V3 Sub(V3 a, V3 b) => new V3((float)((double)a.X - b.X), (float)((double)a.Y - b.Y), (float)((double)a.Z - b.Z));

    /// <summary>100439aa(1): times 1 / |v| (<c>1003e0fd</c>).</summary>
    static V3 Normalise(V3 v)
    {
        float length = (float)Math.Sqrt((float)((double)v.Y * v.Y + (double)v.X * v.X + (double)v.Z * v.Z));
        float s = (float)(1.0 / length);
        return new V3((float)(v.X * (double)s), (float)(v.Y * (double)s), (float)(v.Z * (double)s));
    }

    static float F(float[] f, int i) => f != null && i >= 0 && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i >= 0 && i < f.Length ? GfxBits.Of(f, i) : 0;
}
