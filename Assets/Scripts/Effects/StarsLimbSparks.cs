using System;

/// <summary>
/// Stock <c>_GfxControlStars_t</c> starType 22: sparks drawn onto the host's limbs (43426, nano
/// 43878's heal hit). One <see cref="Step"/> is one call of the stock Process, <c>FUN_100f8491</c>,
/// whose case 22 starts at <c>Gamecode 100fb299</c>. It takes no delta: the spawn allowance and the
/// spring are per call, so how fast the sparks settle is set by how often Process runs.
///
/// Every call first reads twelve attach points on the locator's dynel (<c>10106078</c>, ids at
/// <c>0x102c5fe8</c>) as six segments, base to tip: left and right calf to thigh, pelvis to head, left
/// and right forearm to upper arm, and left to right upper arm. A segment is its base point P, the
/// vector D = tip - base and its length L (<c>10023bfd</c>).
///
/// A spark picks a segment k = rand() % 6 and q = (rand() &amp; 0x7fff) / 32768, and starts at rest at
/// P + D q + r' * field 28, where r' is a random direction round the segment: a unit vector on the XZ
/// circle (<c>100d3215</c>) mapped onto a = FindPerpendicular(D) and b = |a x D| = 1 (its x along a,
/// its z along b; the raw vector when L is 0). Each later call pulls it toward its segment:
/// s = clamp(dot(D, p - P) / L, 0, 1), c = P + D s, then v += (c - p) * 0.1 and p += v * 0.1, as case 3
/// does. s is divided by L once, not L², so for a spark a fraction f along the segment s = f * L: on
/// limbs shorter than a metre c sits nearer the base than the spark and the sparks slide toward it. A spark that was within 0.04 of c is due again at once. It lives field 30 / 1000 s; with t
/// its life fraction, size = (t * 0.7 + 0.3) * field 29, frame = _ftol(t * 15.99), colour = the
/// fields 18-25 ramp at t. Field 31 is the spawn allowance per call.
///
/// Every record sets field 0 = 5 (world mode), so the sprite positions are world positions.
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class StarsLimbSparks : IStarsStockCase
{
    public const int SlotCount = StarsCase3.SlotCount;

    /// <summary>The attach points read each call, in pairs of (base, tip): <c>0x102c5fe8</c>.</summary>
    public static readonly int[] AttachIds = { 1013, 1011, 1014, 1012, 1000, 1006, 1009, 1007, 1010, 1008, 1007, 1008 };

    public const int SegmentCount = 6;

    /// <summary>Spring constant, both uses: <c>FLD [0x10161850]</c> = 0.1.</summary>
    const float Pull = 0.1f;

    /// <summary>A spark this close to its segment point is due again: <c>100fb505 FCOMP [0x1016c340]</c>.</summary>
    const float SettleDistance = 0.04f;

    const double AlongScale = 3.0517578125e-05;
    const double SizeSlope = 0.699999988079071;
    const double SizeBase = 0.30000001192092896;
    const double FrameScale = 15.99;

    readonly float _life;
    readonly float _radius;
    readonly float _size;
    readonly int _allowance;
    readonly float[] _startArgb;
    readonly float[] _endArgb;
    readonly Func<int> _rand;

    // Segments of this call: base P, vector D, length L.
    readonly float[] _segP = new float[SegmentCount * 3];
    readonly float[] _segD = new float[SegmentCount * 3];
    readonly float[] _segL = new float[SegmentCount];

    // +0xc48 positions, +0x648 velocities, +0x1248 segment per slot, +0x1448 timers (lazy init 0).
    readonly float[] _px = new float[SlotCount], _py = new float[SlotCount], _pz = new float[SlotCount];
    readonly float[] _vx = new float[SlotCount], _vy = new float[SlotCount], _vz = new float[SlotCount];
    readonly int[] _segment = new int[SlotCount];
    readonly float[] _timer = new float[SlotCount];
    readonly StarsCase3.Sprite[] _sprites = new StarsCase3.Sprite[SlotCount];

    // Port-only, for drawing between steps (see StarsCase3.Blend).
    readonly StarsCase3.Sprite[] _previous = new StarsCase3.Sprite[SlotCount];
    readonly int[] _slotSerial = new int[SlotCount];
    int _serial;

    /// <summary>Stock +0x1648.</summary>
    public bool Terminating { get; set; }

    /// <summary>Slots spawned or alive on the last step. Stock's <c>[EBP-0xc]</c>.</summary>
    public int LastCount { get; private set; }

    public bool Drained => Terminating && LastCount == 0;

    public float Life => _life;
    public StarsCase3.Sprite[] Sprites => _sprites;

    /// <param name="startArgb">Fields 18-21, in A,R,G,B order.</param>
    /// <param name="endArgb">Fields 22-25, in A,R,G,B order.</param>
    /// <param name="rand">Stock <c>rand()</c>: 0..0x7fff.</param>
    public StarsLimbSparks(
        float lifeSeconds,
        float radius,
        float size,
        int allowance,
        float[] startArgb,
        float[] endArgb,
        Func<int> rand)
    {
        _life = lifeSeconds;
        _radius = radius;
        _size = size;
        _allowance = allowance;
        _startArgb = startArgb;
        _endArgb = endArgb;
        _rand = rand ?? throw new ArgumentNullException(nameof(rand));

        for (int i = 0; i < SlotCount; i++)
            _sprites[i].Argb = 0xffffffffu;
    }

    /// <summary>
    /// One stock Process call at <paramref name="age"/>. <paramref name="attach"/> holds the world
    /// positions of <see cref="AttachIds"/>, x y z each.
    /// </summary>
    public void Step(float age, float[] attach)
    {
        Array.Copy(_sprites, _previous, SlotCount);
        BuildSegments(attach);

        int budget = _allowance;
        int count = 0;

        for (int i = 0; i < SlotCount; i++)
        {
            if (age < _timer[i])
            {
                PullTowardSegment(i);
                if (Settle(i, age))
                    _timer[i] = 0f;
                count++;
                continue;
            }

            if (budget == 0 || Terminating)
            {
                _sprites[i].Visible = false;
                continue;
            }

            Spawn(i, age);
            count++;
            budget--;
            // As in case 3, the spawn call leaves the sprite record as it was.
        }

        LastCount = count;
    }

    void BuildSegments(float[] attach)
    {
        for (int k = 0; k < SegmentCount; k++)
        {
            int b = k * 6, o = k * 3;
            float px = attach[b], py = attach[b + 1], pz = attach[b + 2];
            float dx = attach[b + 3] - px, dy = attach[b + 4] - py, dz = attach[b + 5] - pz;
            _segP[o] = px;
            _segP[o + 1] = py;
            _segP[o + 2] = pz;
            _segD[o] = dx;
            _segD[o + 1] = dy;
            _segD[o + 2] = dz;
            _segL[k] = Length(dx, dy, dz);
        }
    }

    // The spring toward the segment; ex/ey/ez keep c - p for the settle test.
    float _ex, _ey, _ez;

    void PullTowardSegment(int i)
    {
        int o = _segment[i] * 3;
        float ax = _segP[o], ay = _segP[o + 1], az = _segP[o + 2];
        float dx = _segD[o], dy = _segD[o + 1], dz = _segD[o + 2];

        float wx = _px[i] - ax, wy = _py[i] - ay, wz = _pz[i] - az;
        float dot = (float)((double)dy * wy + (double)wx * dx + (double)dz * wz);
        float length = _segL[_segment[i]];
        // A zero-length segment gives stock 0 / 0 here; the port keeps the base point instead.
        float s = length != 0f ? (float)((double)dot / length) : 0f;
        if (!(0f < s || 0f == s))
            s = 0f;
        else if (1f < s)
            s = 1f;

        float cx = ax + dx * s, cy = ay + dy * s, cz = az + dz * s;
        _ex = cx - _px[i];
        _ey = cy - _py[i];
        _ez = cz - _pz[i];

        _vx[i] += _ex * Pull;
        _vy[i] += _ey * Pull;
        _vz[i] += _ez * Pull;
        _px[i] += _vx[i] * Pull;
        _py[i] += _vy[i] * Pull;
        _pz[i] += _vz[i] * Pull;
    }

    /// <summary>Writes the sprite record; true when the spark was within <see cref="SettleDistance"/>.</summary>
    bool Settle(int i, float age)
    {
        ref StarsCase3.Sprite s = ref _sprites[i];
        s.X = _px[i];
        s.Y = _py[i];
        s.Z = _pz[i];

        float t = (float)(((double)_life + age - _timer[i]) / _life);
        s.Size = (float)((t * SizeSlope + SizeBase) * _size);
        s.Argb = StockColorRamp.Eval(_startArgb, _endArgb, t);
        s.Frame = (int)(t * FrameScale);
        s.Visible = true;
        s.Serial = _slotSerial[i];

        return Length(_ex, _ey, _ez) < SettleDistance;
    }

    void Spawn(int i, float age)
    {
        int k = _rand() % SegmentCount;
        float q = (float)((_rand() & 0x7fff) * AlongScale);
        RandomUnitXZ(_rand, out float rx, out float ry, out float rz);

        int o = k * 3;
        float dx = _segD[o], dy = _segD[o + 1], dz = _segD[o + 2];
        if (_segL[k] != 0f)
        {
            Tracer1Sim.FindPerpendicular(dx, dy, dz, out float ax, out float ay, out float az);
            // 1003e09e then 10070816: b = a x D set to length 1.
            float bx = ay * dz - az * dy, by = az * dx - ax * dz, bz = ax * dy - ay * dx;
            float scale = (float)(1.0 / Length(bx, by, bz));
            bx *= scale;
            by *= scale;
            bz *= scale;
            float x = rx, z = rz;
            rx = ax * x + bx * z;
            ry = ay * x + by * z;
            rz = az * x + bz * z;
        }

        _px[i] = (_segP[o] + dx * q) + rx * _radius;
        _py[i] = (_segP[o + 1] + dy * q) + ry * _radius;
        _pz[i] = (_segP[o + 2] + dz * q) + rz * _radius;
        _vx[i] = 0f;
        _vy[i] = 0f;
        _vz[i] = 0f;
        _segment[i] = k;
        _timer[i] = (float)((double)_life + age);
        _slotSerial[i] = ++_serial;
    }

    /// <summary>
    /// <c>100d3215</c>: x and z each (rand() &amp; 0x7fff) / 16384 - 1, redrawn until inside the unit circle
    /// and not zero, then scaled to length 1; y is 0.
    /// </summary>
    public static void RandomUnitXZ(Func<int> rand, out float x, out float y, out float z)
    {
        const double Scale = 6.103515625e-05;
        y = 0f;
        while (true)
        {
            x = (float)((rand() & 0x7fff) * Scale - 1.0);
            z = (float)((rand() & 0x7fff) * Scale - 1.0);
            float lengthSq = (float)(x * x + z * z);
            if (lengthSq >= 1f)
                continue;
            float length = (float)Math.Sqrt(lengthSq);
            if (length == 0f)
                continue;
            x /= length;
            z /= length;
            return;
        }
    }

    static float Length(float x, float y, float z) => (float)Math.Sqrt((float)(x * x + y * y + z * z));

    /// <summary>Port-only; see <see cref="StarsCase3.Blend"/>.</summary>
    public StarsCase3.Sprite Blend(int i, float t) => StarsCase3.Blend(_previous[i], _sprites[i], t);
}
