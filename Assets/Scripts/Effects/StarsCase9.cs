using System;

/// <summary>
/// Stock <c>_GfxControlStars_t</c> starType 9 (<c>Gamecode 100f946f</c>..<c>100f9610</c>): sparks that flash
/// big above a random point near the locator and fall, e.g. 43070-43074 (the hits of 28636 Slime Cascade
/// and its kin) and 13700.
///
/// Fields (loader <c>FUN_100f72a9</c>): 11 base size (+0x167c), 18-21 / 22-25 colour ramp, 26 duration,
/// 28 fall speed (+0x16d4), 29 spread radius (+0x16d8), 30 life in ms (+0x16cc = field 30 / 1000), 31 flash
/// in hundredths (int, +0x16e0). The lazy init (<c>100f81e0</c>) seeds every timer to -100.
///
/// Only every other sprite record is used (a stride of 0x40): 64 sparks, spark j in record 2j, its spawn
/// point at +0xc48 + 24j, its timer at +0x1448 + 8j. Per call, for each:
/// <list type="bullet">
/// <item>Alive (age &lt; timer): x = age + life - timer (seconds), t = x / life, g = 0.5 - x · field 28. The
/// size is field 11, plus g · field 31 / 100 while g &gt; 0; the position spawn + (0, 1.5 + g, 0); the colour
/// the ramp at t; the frame 0.</item>
/// <item>Due: up to 2 per call, not while terminating: spawn = locator + (x, 0, z) · field 29, (x, z) a
/// point in the unit disc (<c>100d32c2</c>: each (rand() &amp; 0x7fff) / 16384 - 1, redrawn until inside and not
/// at the centre), timer = life + age; the record is left as it was. Otherwise hidden.</item>
/// </list>
/// It drains on expiry like case 3 (its <c>0x100fc374</c> entry is 0) and is ready once terminating with
/// nothing alive (<c>100f884e</c>).
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class StarsCase9 : IStarsStockCase
{
    public const int SlotCount = StarsCase3.SlotCount;
    public const int SparkCount = SlotCount / 2;
    public const int SpawnsPerStep = 2;

    // 10156ec0 / 10161968.
    const double Hundredth = 0.009999999776482582;
    const double Lift = 1.5;
    const float UnspawnedTimer = -100f;

    readonly float _life;
    readonly float _size;
    readonly float _fall;
    readonly float _radius;
    readonly float _flash;
    readonly float[] _start;
    readonly float[] _end;
    readonly Func<int> _rand;

    readonly float[] _px = new float[SparkCount], _py = new float[SparkCount], _pz = new float[SparkCount];
    readonly float[] _timer = new float[SparkCount];
    readonly StarsCase3.Sprite[] _sprites = new StarsCase3.Sprite[SlotCount];
    readonly StarsCase3.Sprite[] _previous = new StarsCase3.Sprite[SlotCount];
    readonly int[] _serial = new int[SparkCount];

    public bool Terminating { get; set; }
    public int LastCount { get; private set; }
    public bool Drained => Terminating && LastCount == 0;
    public StarsCase3.Sprite[] Sprites => _sprites;

    /// <param name="size">Field 11.</param>
    /// <param name="fall">Field 28.</param>
    /// <param name="radius">Field 29.</param>
    /// <param name="flash">Field 31 as an int.</param>
    public StarsCase9(float lifeSeconds, float size, float fall, float radius, int flash,
        float[] startArgb, float[] endArgb, Func<int> rand)
    {
        _life = lifeSeconds;
        _size = size;
        _fall = fall;
        _radius = radius;
        _flash = (float)(flash * Hundredth);
        _start = startArgb;
        _end = endArgb;
        _rand = rand ?? throw new ArgumentNullException(nameof(rand));
        for (int j = 0; j < SparkCount; j++)
            _timer[j] = UnspawnedTimer;
    }

    /// <summary>One stock Process call at <paramref name="age"/>; o is the locator position.</summary>
    public void Step(float age, float ox, float oy, float oz)
    {
        Array.Copy(_sprites, _previous, SlotCount);

        int budget = SpawnsPerStep;
        int count = 0;
        for (int j = 0; j < SparkCount; j++)
        {
            ref StarsCase3.Sprite s = ref _sprites[2 * j];
            if (age < _timer[j])
            {
                double x = (double)age + _life - _timer[j];
                float t = (float)(x / _life);
                float g = (float)(0.5 - x * _fall);
                float size = _size;
                if (0f < g)
                    size = (float)(g * (double)_flash + size);

                s.X = (float)(_px[j] + 0.0);
                s.Y = (float)(_py[j] + (double)(float)(g + Lift));
                s.Z = (float)(_pz[j] + 0.0);
                s.Size = size;
                s.Argb = StockColorRamp.Eval(_start, _end, t);
                s.Frame = 0;
                s.Visible = true;
                s.Serial = _serial[j];
                count++;
                continue;
            }

            if (budget == 0 || Terminating)
            {
                s.Visible = false;
                continue;
            }

            RandomInUnitDisc(_rand, out float dx, out float dz);
            _px[j] = (float)(ox + (double)(float)(dx * (double)_radius));
            _py[j] = (float)(oy + 0.0);
            _pz[j] = (float)(oz + (double)(float)(dz * (double)_radius));
            _timer[j] = (float)((double)_life + age);
            _serial[j]++;
            count++;
            budget--;
        }

        LastCount = count;
    }

    /// <summary>
    /// <c>100d32c2</c>: x and z each (rand() &amp; 0x7fff) / 16384 - 1, redrawn while x² + z² &gt;= 1 or the
    /// point is the centre. Not set to length 1.
    /// </summary>
    public static void RandomInUnitDisc(Func<int> rand, out float x, out float z)
    {
        const double Scale = 6.103515625e-05;
        while (true)
        {
            x = (float)((rand() & 0x7fff) * Scale - 1.0);
            z = (float)((rand() & 0x7fff) * Scale - 1.0);
            float squared = (float)((double)z * z + (double)x * x);
            if (!(squared < 1f))
                continue;
            if ((float)Math.Sqrt(squared) == 0f)
                continue;
            return;
        }
    }

    /// <summary>Port-only; see <see cref="StarsCase3.Blend"/>.</summary>
    public StarsCase3.Sprite Blend(int i, float t) => StarsCase3.Blend(_previous[i], _sprites[i], t);
}
