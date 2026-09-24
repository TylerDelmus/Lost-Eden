using System;

/// <summary>
/// Stock <c>_GfxControlStars_t</c> starType 2: 128 sparkles in a ball round the locator that spread out,
/// shrink and fade over 1.5 s, each in one of eight hues (43220, nano 246033's hit). One <see cref="Step"/>
/// is one call of the stock Process, <c>FUN_100f8491</c>, case 2 at <c>Gamecode 100f8864</c>..<c>100f89d8</c>.
///
/// Fields (loader <c>FUN_100f72a9</c>): 18-21 / 22-25 colour ramp (+0x1698), 28 size (+0x16d4). Field 26
/// is loaded as the duration, but the lazy init (<c>100f80ba</c>) sets it to 1.5 s on the first call.
///
/// Lazy init: every slot gets a random point in the unit ball (<c>100d316a</c>, kept at +0x48), sits at the
/// locator with width and height 1, and is visible.
///
/// Per call, with p = age / duration:
/// <list type="bullet">
/// <item>k = √p · 0.7 + 0.6; slot i sits at locator + dir[i] · k.</item>
/// <item>n = _ftol((1 - p) · 15) clamped to 1..15; size = field 28 · (1 - p) · 20 / <see cref="FrameSpan"/>[n];
/// slot i shows frame n - (i &amp; 1).</item>
/// <item>Colour j (0..7) is the ramp at p with its start R, G, B replaced by <see cref="Palette"/>[j]; slot i
/// takes colour i &amp; 7.</item>
/// </list>
/// Expiry readies it at once (the <c>0x100fc374</c> entry is 1); terminating readies it after the next
/// call (the tail at <c>100f8622</c>).
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class StarsCase2 : IStarsStockCase
{
    public const int SlotCount = StarsCase3.SlotCount;

    /// <summary><c>100f8105 FLD [0x1015e7ac]</c>: the duration the lazy init writes over field 26.</summary>
    public const float LazyDuration = 1.5f;

    /// <summary>The ints at <c>0x102c5fa8</c>: the size is divided by entry n (and multiplied by 20).</summary>
    public static readonly int[] FrameSpan = { 10, 6, 7, 7, 9, 11, 16, 19, 22, 23, 24, 25, 26, 27, 28, 29 };

    /// <summary>The R, G, B triples at <c>0x102c5ee8</c>.</summary>
    public static readonly float[][] Palette =
    {
        new[] { 1f, 0.7f, 0.7f }, new[] { 1f, 1f, 0f }, new[] { 1f, 0f, 1f }, new[] { 0f, 1f, 1f },
        new[] { 0f, 0f, 1f }, new[] { 0f, 1f, 0f }, new[] { 1f, 0f, 0f }, new[] { 0.7f, 1f, 0.7f },
    };

    readonly float _size;
    readonly float[] _start;
    readonly float[] _end;
    readonly Func<int> _rand;

    // +0x48 per-slot directions.
    readonly float[] _dx = new float[SlotCount], _dy = new float[SlotCount], _dz = new float[SlotCount];
    readonly uint[] _colours = new uint[8];
    readonly float[] _rampStart = new float[4];
    readonly StarsCase3.Sprite[] _sprites = new StarsCase3.Sprite[SlotCount];

    // Port-only, for drawing between steps (see StarsCase3.Blend).
    readonly StarsCase3.Sprite[] _previous = new StarsCase3.Sprite[SlotCount];

    /// <summary>Stock +0x16f0: the lazy init has run.</summary>
    public bool Initialised { get; private set; }

    public bool Terminating { get; set; }

    /// <summary>The tail (<c>100f8622</c>) readies a terminating case 2 after its next call, alive or not.</summary>
    public bool Drained => Terminating;

    public StarsCase3.Sprite[] Sprites => _sprites;

    /// <param name="size">Field 28.</param>
    public StarsCase2(float size, float[] startArgb, float[] endArgb, Func<int> rand)
    {
        _size = size;
        _start = startArgb;
        _end = endArgb;
        _rand = rand ?? throw new ArgumentNullException(nameof(rand));
    }

    /// <summary>One stock Process call; <paramref name="duration"/> is the control's (+0x10).</summary>
    public void Step(float age, float duration, float ox, float oy, float oz)
    {
        Array.Copy(_sprites, _previous, SlotCount);

        if (!Initialised)
        {
            // 100f80ba.
            Initialised = true;
            for (int i = 0; i < SlotCount; i++)
            {
                StarsCase3.RandomPointInUnitBall(_rand, out _dx[i], out _dy[i], out _dz[i]);
                ref StarsCase3.Sprite s = ref _sprites[i];
                s.X = ox; s.Y = oy; s.Z = oz;
                s.Size = 1f;
                s.Argb = 0xffffffffu;
                s.Visible = true;
                s.Serial = 1;
            }
        }

        float p = age / duration;
        float root = (float)Math.Sqrt(p);
        float k = (float)(root * 0.699999988079071 + 0.6000000238418579);
        double rest = 1.0 - p;
        float shrink = (float)(_size * rest);
        int n = (int)(rest * 15.0);
        if (n > 15)
            n = 15;
        else if (n < 1)
            n = 1;
        float size = (float)(20.0 / FrameSpan[n] * shrink);

        _rampStart[0] = _start[0];
        for (int j = 0; j < 8; j++)
        {
            _rampStart[1] = Palette[j][0];
            _rampStart[2] = Palette[j][1];
            _rampStart[3] = Palette[j][2];
            _colours[j] = StockColorRamp.Eval(_rampStart, _end, p);
        }

        for (int i = 0; i < SlotCount; i++)
        {
            ref StarsCase3.Sprite s = ref _sprites[i];
            s.X = _dx[i] * k + ox;
            s.Y = _dy[i] * k + oy;
            s.Z = _dz[i] * k + oz;
            s.Size = size;
            s.Argb = _colours[i & 7];
            s.Frame = n - (i & 1);
        }
    }

    /// <summary>Port-only; see <see cref="StarsCase3.Blend"/>.</summary>
    public StarsCase3.Sprite Blend(int i, float t) => StarsCase3.Blend(_previous[i], _sprites[i], t);
}
