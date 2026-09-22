using System;

/// <summary>
/// Stock <c>_GfxControlStars_t</c> starType 10: still sparkles on a sphere shell round the locator that
/// swell and shrink, the whole cloud fading in and out over the duration (43258, in nano 223386's hit).
/// One <see cref="Step"/> is one call of the stock Process, <c>FUN_100f8491</c>, case 10 at
/// <c>Gamecode 100f9615</c>..<c>100f97fb</c>.
///
/// Fields (loader <c>FUN_100f72a9</c>): 11 base size (+0x167c), 18-21 / 22-25 colour ramp, 26 duration,
/// 28 spark life in seconds (copied over +0x16cc every call), 29 shell radius (+0x16d8), 30 non-zero picks
/// the eight-colour palette at <c>0x102c5f48</c> (int, +0x16dc), 31 size swing in hundredths (int, +0x16e0).
/// The lazy init (<c>100f81e0</c>) seeds every timer to -100.
///
/// Per call, with p = age / duration:
/// <list type="bullet">
/// <item>Envelope e = 1 - (2p - 1)²; colour = the ramp at p with its alpha replaced by <c>_ftol(e * 255)</c>
/// (the ramp's alpha is forced to 0xff, then ANDed with <c>(alpha &lt;&lt; 24) | 0xffffff</c>).</item>
/// <item>Alive (age &lt; timer): q = (timer - age) / life, u = 2q - 1; the record stays at its point with
/// size (1 - u²) * field 31 / 100 + field 11, frame 0, and the shared colour, or with field 30 set the
/// palette entry for (slot &amp; 7) under the same alpha mask.</item>
/// <item>Due: up to 5 per call, not while terminating: the point is the locator plus a random unit vector
/// (<c>100d3005</c>) times field 29, timer = age + life; the record is left as it was. Otherwise hidden.</item>
/// </list>
/// Expiry readies it at once (the <c>0x100fc374</c> entry is 1).
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class StarsCase10 : IStarsStockCase
{
    public const int SlotCount = StarsCase3.SlotCount;

    /// <summary><c>100f9632 MOV [EBP-0x20], 5</c>.</summary>
    public const int SpawnsPerStep = 5;

    /// <summary>The palette at <c>0x102c5f48</c>, D3DCOLOR.</summary>
    public static readonly uint[] Palette =
    {
        0xffffc0c0u, 0xffffff00u, 0xffff00ffu, 0xff00ffffu, 0xff0000ffu, 0xff00ff00u, 0xffff0000u, 0xffc0ffc0u,
    };

    const float UnspawnedTimer = -100f;

    // 100f9645: 0.01 as a float, widened.
    const double Hundredth = 0.009999999776482582;

    readonly float _life;
    readonly float _baseSize;
    readonly float _radius;
    readonly bool _palette;
    readonly float _swing;
    readonly float[] _start;
    readonly float[] _end;
    readonly Func<int> _rand;

    // +0xc48 positions, +0x1448 per-slot expiry timers.
    readonly float[] _px = new float[SlotCount], _py = new float[SlotCount], _pz = new float[SlotCount];
    readonly float[] _timer = new float[SlotCount];
    readonly StarsCase3.Sprite[] _sprites = new StarsCase3.Sprite[SlotCount];

    // Port-only, for drawing between steps (see StarsCase3.Blend).
    readonly StarsCase3.Sprite[] _previous = new StarsCase3.Sprite[SlotCount];
    readonly int[] _slotSerial = new int[SlotCount];
    int _serial;

    public bool Terminating { get; set; }
    public int LastCount { get; private set; }
    public bool Drained => Terminating && LastCount == 0;
    public StarsCase3.Sprite[] Sprites => _sprites;

    /// <param name="lifeSeconds">Field 28.</param>
    /// <param name="baseSize">Field 11.</param>
    /// <param name="radius">Field 29.</param>
    /// <param name="paletteFlag">Field 30 as an int.</param>
    /// <param name="swing">Field 31 as an int.</param>
    public StarsCase10(
        float lifeSeconds,
        float baseSize,
        float radius,
        int paletteFlag,
        int swing,
        float[] startArgb,
        float[] endArgb,
        Func<int> rand)
    {
        _life = lifeSeconds;
        _baseSize = baseSize;
        _radius = radius;
        _palette = paletteFlag != 0;
        _swing = (float)(swing * Hundredth);
        _start = startArgb;
        _end = endArgb;
        _rand = rand ?? throw new ArgumentNullException(nameof(rand));

        for (int i = 0; i < SlotCount; i++)
        {
            _timer[i] = UnspawnedTimer;
            _sprites[i].Argb = 0xffffffffu;
        }
    }

    /// <summary>100f964e..100f96a8: the shared colour and the alpha mask at <paramref name="progress"/>.</summary>
    public uint SharedColour(float progress, out uint mask)
    {
        float s = (float)(((double)progress - 0.5) * 2.0);
        float envelope = (float)(1.0 - (double)s * s);
        int alpha = (int)(envelope * 255.0);
        mask = unchecked((uint)(alpha << 24) | 0xffffffu);
        return (StockColorRamp.Eval(_start, _end, progress) | 0xff000000u) & mask;
    }

    /// <summary>One stock Process call; <paramref name="duration"/> is the control's (+0x10).</summary>
    public void Step(float age, float duration, float ox, float oy, float oz)
    {
        Array.Copy(_sprites, _previous, SlotCount);

        float progress = age / duration;
        uint colour = SharedColour(progress, out uint mask);
        int budget = SpawnsPerStep;
        int count = 0;

        for (int i = 0; i < SlotCount; i++)
        {
            if (age < _timer[i])
            {
                float q = (_timer[i] - age) / _life;
                if (_palette)
                    colour = Palette[i & 7] & mask;
                float u = (float)(((double)q - 0.5) * 2.0);

                ref StarsCase3.Sprite s = ref _sprites[i];
                s.X = _px[i];
                s.Y = _py[i];
                s.Z = _pz[i];
                s.Size = (float)((1.0 - (double)u * u) * _swing + _baseSize);
                s.Argb = colour;
                s.Frame = 0;
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

            ElectraSim.RandomUnitVector(_rand, out float x, out float y, out float z);
            _px[i] = ox + x * _radius;
            _py[i] = oy + y * _radius;
            _pz[i] = oz + z * _radius;
            _timer[i] = age + _life;
            _slotSerial[i] = ++_serial;
            count++;
            budget--;
        }

        LastCount = count;
    }

    /// <summary>Port-only; see <see cref="StarsCase3.Blend"/>.</summary>
    public StarsCase3.Sprite Blend(int i, float t) => StarsCase3.Blend(_previous[i], _sprites[i], t);
}
