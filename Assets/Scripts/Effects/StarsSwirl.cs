using System;

/// <summary>
/// Stock <c>_GfxControlStars_t</c> starTypes 6 and 11: sparks launched round a ring a metre below the
/// locator that circle and climb while a spring draws them in (43372 and 43318, in nano 223386's hit).
/// One <see cref="Step"/> is one call of the stock Process, <c>FUN_100f8491</c>: case 6 at
/// <c>Gamecode 100f90d7</c>..<c>100f936c</c>, case 11 at <c>100f9800</c>..<c>100f9a79</c>.
///
/// Fields (loader <c>FUN_100f72a9</c>): 18-21 / 22-25 colour ramp, 26 duration, 28 size curve (+0x16d4),
/// 29 ring radius (+0x16d8), 30 life in ms (int; +0x16cc = field 30 / 1000), 31 spring in hundredths
/// (int, +0x16e0). The lazy init (<c>100f81e0</c>) seeds every timer to -100.
///
/// Per call, for each slot, with k = field 31 / 100:
/// <list type="bullet">
/// <item>Alive: <c>v += k * (locator - p)</c> (case 6 with the pull's y set to 0), then
/// <c>p += v * step</c>, step 0.15 for case 6 and 0.1 for case 11. With t the life fraction, size
/// (t + 0.2) * field 28 * (1 - t²), frame <c>15 - _ftol(16 * t)</c> clamped to 0..15 (case 11 computes it
/// as <c>15 - _ftol((timer - age) * 16 / life)</c>), colour the ramp at t.</item>
/// <item>Case 6 keeps slot i alive (i &amp; 7) / 9.5 s past its timer and takes
/// t = (life + age - timer) / (life + that); case 11 uses t = (age + life - timer) / life.</item>
/// <item>Due: up to 2 per call, not while terminating: r = a unit vector on the XZ circle (<c>100d3215</c>)
/// times field 29, <c>p = locator + r + (0, -1, 0)</c>, <c>v = (r.z, 0.5, -r.x)</c>, timer = life + age;
/// the record is left as it was. Otherwise hidden.</item>
/// </list>
/// Both drain on expiry like case 3 (their <c>0x100fc374</c> entries are 0).
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class StarsSwirl : IStarsStockCase
{
    public const int SlotCount = StarsCase3.SlotCount;

    public const int SpawnsPerStep = 2;

    /// <summary>100f919f: case 6's position step.</summary>
    public const float Step6 = 0.15000000596046448f;

    /// <summary>100f98a8: case 11's position step.</summary>
    public const float Step11 = 0.10000000149011612f;

    /// <summary>100f992b: case 6's extra life per (slot &amp; 7), in seconds.</summary>
    public const double LingerDivisor = 9.5;

    /// <summary>100f99f2: the ring sits this far below the locator.</summary>
    public const float Drop = -1f;

    /// <summary>100f9a13: upward launch speed.</summary>
    public const float Rise = 0.5f;

    const float UnspawnedTimer = -100f;

    readonly bool _flatPull;
    readonly float _step;
    readonly float _life;
    readonly float _sizeCurve;
    readonly float _radius;
    readonly float _spring;
    readonly float[] _start;
    readonly float[] _end;
    readonly Func<int> _rand;

    // +0xc48 positions, +0x648 velocities, +0x1448 per-slot expiry timers.
    readonly float[] _px = new float[SlotCount], _py = new float[SlotCount], _pz = new float[SlotCount];
    readonly float[] _vx = new float[SlotCount], _vy = new float[SlotCount], _vz = new float[SlotCount];
    readonly float[] _timer = new float[SlotCount];
    readonly StarsCase3.Sprite[] _sprites = new StarsCase3.Sprite[SlotCount];

    // Port-only, for drawing between steps (see StarsCase3.Blend).
    readonly StarsCase3.Sprite[] _previous = new StarsCase3.Sprite[SlotCount];
    readonly int[] _slotSerial = new int[SlotCount];
    int _serial;

    public int StarType { get; }
    public bool Terminating { get; set; }
    public int LastCount { get; private set; }
    public bool Drained => Terminating && LastCount == 0;
    public StarsCase3.Sprite[] Sprites => _sprites;

    public static bool Handles(int starType) => starType == 6 || starType == 11;

    /// <param name="starType">6 or 11.</param>
    /// <param name="sizeCurve">Field 28.</param>
    /// <param name="radius">Field 29.</param>
    /// <param name="spring">Field 31 as an int.</param>
    public StarsSwirl(
        int starType,
        float lifeSeconds,
        float sizeCurve,
        float radius,
        int spring,
        float[] startArgb,
        float[] endArgb,
        Func<int> rand)
    {
        if (!Handles(starType))
            throw new ArgumentOutOfRangeException(nameof(starType), starType, "starType 6 or 11");

        StarType = starType;
        _flatPull = starType == 6;
        _step = starType == 6 ? Step6 : Step11;
        _life = lifeSeconds;
        _sizeCurve = sizeCurve;
        _radius = radius;
        _spring = (float)(spring / 100.0);
        _start = startArgb;
        _end = endArgb;
        _rand = rand ?? throw new ArgumentNullException(nameof(rand));

        for (int i = 0; i < SlotCount; i++)
        {
            _timer[i] = UnspawnedTimer;
            _sprites[i].Argb = 0xffffffffu;
        }
    }

    /// <summary>Case 6's extra life for slot <paramref name="i"/>; 0 for case 11.</summary>
    public float Linger(int i) => _flatPull ? (float)((i & 7) / LingerDivisor) : 0f;

    /// <summary>One stock Process call at <paramref name="age"/>; o is the locator position.</summary>
    public void Step(float age, float ox, float oy, float oz)
    {
        Array.Copy(_sprites, _previous, SlotCount);

        int budget = SpawnsPerStep;
        int count = 0;

        for (int i = 0; i < SlotCount; i++)
        {
            float linger = Linger(i);
            if (age < _timer[i] + linger)
            {
                float dx = ox - _px[i];
                float dy = _flatPull ? 0f : oy - _py[i];
                float dz = oz - _pz[i];
                _vx[i] += dx * _spring;
                _vy[i] += dy * _spring;
                _vz[i] += dz * _spring;
                _px[i] += _vx[i] * _step;
                _py[i] += _vy[i] * _step;
                _pz[i] += _vz[i] * _step;

                float t;
                int frame;
                if (_flatPull)
                {
                    t = (float)(((double)_life + age - _timer[i]) / ((double)_life + linger));
                    frame = 15 - (int)(16.0 * t);
                }
                else
                {
                    frame = 15 - (int)(((double)_timer[i] - age) * 16.0 / _life);
                    t = (float)(((double)age + _life - _timer[i]) / _life);
                }
                if (frame > 15)
                    frame = 15;
                else if (frame < 0)
                    frame = 0;

                ref StarsCase3.Sprite s = ref _sprites[i];
                s.X = _px[i];
                s.Y = _py[i];
                s.Z = _pz[i];
                s.Size = StarsCase3.Size(t, _sizeCurve);
                s.Argb = StockColorRamp.Eval(_start, _end, t);
                s.Frame = frame;
                s.Visible = true;
                s.Serial = _slotSerial[i];
                count++;
                continue;
            }

            if (budget == 0 || Terminating)
            {
                _sprites[i].Visible = false;
                continue;
            }

            StarsLimbSparks.RandomUnitXZ(_rand, out float rx, out float ry, out float rz);
            rx *= _radius;
            rz *= _radius;
            _px[i] = (ox + rx) + 0f;
            _py[i] = (oy + ry) + Drop;
            _pz[i] = (oz + rz) + 0f;
            _vx[i] = rz;
            _vy[i] = Rise;
            _vz[i] = -rx;
            _timer[i] = _life + age;
            _slotSerial[i] = ++_serial;
            count++;
            budget--;
        }

        LastCount = count;
    }

    /// <summary>Port-only; see <see cref="StarsCase3.Blend"/>.</summary>
    public StarsCase3.Sprite Blend(int i, float t) => StarsCase3.Blend(_previous[i], _sprites[i], t);
}
