using System;

/// <summary>
/// Stock <c>_GfxControlStars_t</c> starTypes 16 to 20: nano tracers made of short-lived sparks
/// along their hit location. One <see cref="Step"/> is one call of the stock Process, <c>FUN_100f8491</c>;
/// case 16 starts at <c>Gamecode 100fa20f</c> (17100, nano 43878's tracer), case 17 at <c>100fa44b</c>
/// (11100-11102, nano 75347's tracer 17200), case 18 at <c>100fa781</c> (17300, nano 26355's tracer),
/// case 19 at <c>100fa9e4</c> (45597, nano 150501's tracer), case 20 at <c>100fac3e</c> (45686, nano 28597's
/// tracer). None takes a delta: the spawn allowance is per call, so the density is set by how often
/// Process runs.
///
/// Built by <c>CreateGfxControl(id, hitLoc)</c> (ctor <c>100f7d4a</c>), which keeps the hit location's id
/// and a locator at the world origin. Every spawn looks the hit location up again (<c>100cdfd9</c>) and
/// reads its start and end (<c>101054fe</c> / <c>10105534</c>); without one nothing spawns. A spark sits
/// still at end * q + start * (1 - q) plus a random point in the unit ball times field 29:
/// <list type="bullet">
/// <item>case 16: q = (rand() &amp; 0x7fff) / 32768, anywhere along the line; it lives field 30 / 1000 s.</item>
/// <item>case 19: q = age / duration, the trail's head; it lives field 30 / 1000 s times
/// (rand() &amp; 0x7ff) * 0.0001 + 0.9.</item>
/// <item>case 20: as case 19 with no life jitter; while alive its size is field 28 throughout and its frame
/// counts up, _ftol(t * 63.99), over the 64 cells.</item>
/// <item>case 18: as case 19 with no life jitter and 10 spawns per call. Its life is written
/// field 30 / 1000 * (1 - (i % 17) / 34) for slot i, but the division is an integer one
/// (<c>100fa7b7</c>..<c>100fa7c3</c>), so the factor is always 1.</item>
/// <item>case 17: a helix instead of the ball: with D = end - start (x nudged by 0.001 under 1e-5),
/// q as case 16, a = FindPerpendicular(D), b = a x D set to length 1 and
/// theta = sqrt(|D|) * 2q + (age / duration) * 4 + field 29, the spark sits at
/// start + D q + (b cos theta + a sin theta) * (1 - (1 - 2q)²) / 2; it lives field 30 / 1000 s and
/// takes the ramp at age / duration rather than at its own life.</item>
/// </list>
/// While alive, with t its life fraction: size = field 28 * (1 - (1 - 2t)²), frame =
/// 15 - _ftol(t * 15.99), colour = the fields 18-25 ramp at t (case 17: at age / duration; case 20: see above).
///
/// Fields: 26 duration, 28 size, 29 scatter radius (case 17: helix phase), 30 life in ms (int bits),
/// 18-21 / 22-25 colour.
/// Every record of these types sets field 0 = 5 (world mode), so the sprite positions are world positions.
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class StarsLineSparks : IStarsStockCase
{
    public const int SlotCount = StarsCase3.SlotCount;

    /// <summary>Slots a single Process may open: <c>100fa22c</c> / <c>100faa07 MOV [EBP-0x20], 0xf</c>.</summary>
    public const int SpawnsPerStep = 15;

    /// <summary>Case 18's allowance: <c>100fa7a7 MOV [EBP-0x10], 0xa</c>.</summary>
    public const int Case18SpawnsPerStep = 10;

    /// <summary>Timer seed from the lazy init (<c>100f81e0</c>), so every slot is due at once.</summary>
    const float UnspawnedTimer = -100f;

    // 100fabed / 100fabf3: float constants the compiler widened to doubles.
    const double LifeJitterScale = 9.999999747378752e-05;
    const double LifeJitterBase = 0.8999999761581421;

    /// <summary>100fa36e: rand() &amp; 0x7fff over 32768.</summary>
    const double AlongScale = 3.0517578125e-05;

    // 100faaae: 15.99 as a float, widened.
    const double FrameScale = 15.989999771118164;

    // 100facd4: case 20's frame scale, 63.99 as a float, widened.
    const double Case20FrameScale = 63.9900016784668;

    readonly bool _alongProgress;
    readonly bool _lifeJitter;
    readonly int _spawnsPerStep;
    readonly bool _helix;
    readonly bool _flat;
    readonly float _life;
    readonly float _size;
    readonly float _radius;
    readonly float[] _startArgb;
    readonly float[] _endArgb;
    readonly Func<int> _rand;

    // +0xc48 positions, +0x1448 per-slot expiry timers.
    readonly float[] _px = new float[SlotCount], _py = new float[SlotCount], _pz = new float[SlotCount];
    readonly float[] _timer = new float[SlotCount];
    readonly StarsCase3.Sprite[] _sprites = new StarsCase3.Sprite[SlotCount];

    // Port-only, for drawing between steps (see StarsCase3.Blend).
    readonly StarsCase3.Sprite[] _previous = new StarsCase3.Sprite[SlotCount];
    readonly int[] _slotSerial = new int[SlotCount];
    int _serial;

    public int StarType { get; }

    /// <summary>Stock +0x1648. Set by TerminateGracefully.</summary>
    public bool Terminating { get; set; }

    /// <summary>Slots spawned or alive on the last step. Stock's <c>[EBP-0x24]</c>.</summary>
    public int LastCount { get; private set; }

    /// <summary>Stock readies the control once terminating and a step saw nothing alive (<c>100fc2f0</c>).</summary>
    public bool Drained => Terminating && LastCount == 0;

    public float Life => _life;
    public StarsCase3.Sprite[] Sprites => _sprites;

    public static bool Handles(int starType) => starType is 16 or 17 or 18 or 19 or 20;

    /// <param name="starType">16, 17, 18, 19 or 20.</param>
    /// <param name="radius">Field 29: the ball's radius, or case 17's helix phase.</param>
    /// <param name="startArgb">Fields 18-21, in A,R,G,B order.</param>
    /// <param name="endArgb">Fields 22-25, in A,R,G,B order.</param>
    /// <param name="rand">Stock <c>rand()</c>: 0..0x7fff.</param>
    public StarsLineSparks(
        int starType,
        float lifeSeconds,
        float size,
        float radius,
        float[] startArgb,
        float[] endArgb,
        Func<int> rand)
    {
        if (!Handles(starType))
            throw new ArgumentOutOfRangeException(nameof(starType), starType, "starType 16 to 20");

        StarType = starType;
        _alongProgress = starType == 18 || starType == 19 || starType == 20;
        _lifeJitter = starType == 19;
        _spawnsPerStep = starType == 18 ? Case18SpawnsPerStep : SpawnsPerStep;
        _helix = starType == 17;
        _flat = starType == 20;
        _life = lifeSeconds;
        _size = size;
        _radius = radius;
        _startArgb = startArgb;
        _endArgb = endArgb;
        _rand = rand ?? throw new ArgumentNullException(nameof(rand));

        for (int i = 0; i < SlotCount; i++)
        {
            _timer[i] = UnspawnedTimer;
            _sprites[i].Argb = 0xffffffffu;
        }
    }

    /// <summary>
    /// One stock Process call at <paramref name="age"/>. <paramref name="hasHitLocation"/> false means the
    /// lookup failed; start and end are then ignored.
    /// </summary>
    public void Step(
        float age,
        float duration,
        bool hasHitLocation,
        float sx, float sy, float sz,
        float ex, float ey, float ez)
    {
        Array.Copy(_sprites, _previous, SlotCount);

        float progress = (float)((double)age / duration);
        int budget = _spawnsPerStep;
        int count = 0;

        for (int i = 0; i < SlotCount; i++)
        {
            if (age < _timer[i])
            {
                ref StarsCase3.Sprite s = ref _sprites[i];
                s.X = _px[i];
                s.Y = _py[i];
                s.Z = _pz[i];

                float t = (float)(((double)_life + age - _timer[i]) / _life);
                if (_flat)
                {
                    // 100facae..100facec: field 28 as it is, frame _ftol(t * 63.99).
                    s.Size = _size;
                    s.Frame = (int)(t * Case20FrameScale);
                }
                else
                {
                    float u = (float)(1.0 - 2.0 * t);
                    float bulge = (float)(1.0 - (double)u * u);
                    s.Size = (float)((double)_size * bulge);
                    s.Frame = 15 - (int)(t * FrameScale);
                }
                s.Argb = StockColorRamp.Eval(_startArgb, _endArgb, _helix ? progress : t);
                s.Visible = true;
                s.Serial = _slotSerial[i];
                count++;
                continue;
            }

            if (budget == 0 || Terminating || !hasHitLocation)
            {
                _sprites[i].Visible = false;
                continue;
            }

            if (_helix)
            {
                SpawnHelix(i, age, progress, sx, sy, sz, ex, ey, ez);
                count++;
                budget--;
                continue;
            }

            StarsCase3.RandomPointInUnitBall(_rand, out float x, out float y, out float z);
            float q = _alongProgress ? progress : (float)((_rand() & 0x7fff) * AlongScale);
            float rx = x * _radius, ry = y * _radius, rz = z * _radius;
            float back = (float)(1.0 - q);
            _px[i] = (ex * q + sx * back) + rx;
            _py[i] = (ey * q + sy * back) + ry;
            _pz[i] = (ez * q + sz * back) + rz;

            if (_lifeJitter)
            {
                float jitter = (float)((_rand() & 0x7ff) * LifeJitterScale + LifeJitterBase);
                _timer[i] = (float)((double)_life * jitter + age);
            }
            else
            {
                _timer[i] = (float)((double)_life + age);
            }

            _slotSerial[i] = ++_serial;
            count++;
            budget--;
            // As in case 3, the spawn call leaves the sprite record as it was.
        }

        LastCount = count;
    }

    /// <summary>Case 17's spawn (<c>100fa573</c>..<c>100fa754</c>).</summary>
    void SpawnHelix(int i, float age, float progress, float sx, float sy, float sz, float ex, float ey, float ez)
    {
        float dx = ex - sx, dy = ey - sy, dz = ez - sz;
        if (Math.Sqrt((float)(dx * dx + dy * dy + dz * dz)) < 1e-05)
            dx = (float)(dx + 0.0010000000474974513);

        Tracer1Sim.FindPerpendicular(dx, dy, dz, out float ax, out float ay, out float az);
        float bx = ay * dz - az * dy, by = az * dx - ax * dz, bz = ax * dy - ay * dx;
        float bLength = (float)Math.Sqrt((float)(bx * bx + by * by + bz * bz));
        if (bLength != 0f)
        {
            float scale = (float)(1.0 / bLength);
            bx *= scale;
            by *= scale;
            bz *= scale;
        }

        float q = (float)((_rand() & 0x7fff) * AlongScale);
        double twoQ = (double)q + q;
        float s = (float)(1.0 - twoQ);
        float root = (float)Math.Sqrt((float)Math.Sqrt((float)(dx * dx + dy * dy + dz * dz)));
        float theta = (float)(root * twoQ + progress * 4.0 + _radius);
        float cos = (float)Math.Cos(theta), sin = (float)Math.Sin(theta);
        float rad = (float)((1.0 - (double)s * s) * 0.5);

        float rx = (bx * cos + ax * sin) * rad;
        float ry = (by * cos + ay * sin) * rad;
        float rz = (bz * cos + az * sin) * rad;
        _px[i] = (sx + dx * q) + rx;
        _py[i] = (sy + dy * q) + ry;
        _pz[i] = (sz + dz * q) + rz;
        _timer[i] = (float)((double)_life + age);
        _slotSerial[i] = ++_serial;
    }

    /// <summary>Port-only; see <see cref="StarsCase3.Blend"/>.</summary>
    public StarsCase3.Sprite Blend(int i, float t) => StarsCase3.Blend(_previous[i], _sprites[i], t);
}
