using System;

/// <summary>
/// Stock <c>_GfxControlStars_t</c> starType 28 (<c>Gamecode 100fc0d7</c>..<c>100fc2ef</c>): a point that
/// travels along the hit-location line over the duration, shedding sprites as it goes. Its only record
/// is 12600, the hit of 259902 Throw Snowball and its kin — a snowball.
///
/// Fields (loader <c>FUN_100f72a9</c>): 18-25 the colour ramp, 26 duration, 28 the size a sprite swells
/// to (+0x16d4), 30 the sprite's life in ms (+0x16dc, <c>/1000</c> into +0x16cc by the shared lazy init).
///
/// The travel: the hit location's two ends are read every call (<c>101054fe</c> start, <c>10105534</c>
/// end) and mixed by p = age / duration, so the point runs start → end. <b>Without a hit location
/// nothing spawns</b> (<c>100fc28b</c>), as with the line types.
///
/// Per call, up to <b>ten</b> sprites may be started (the literal 10 at <c>100fc0df</c>):
/// <list type="bullet">
/// <item><b>Spawn</b>: the slot's death time becomes age + life.</item>
/// <item><b>Alive</b>: t = (age + life - death) / life runs 0 to 1. The sprite's size on both axes is
/// <c>field 28 · (1 - (1 - 2t)²)</c>, an envelope that swells from nothing at birth to full at half
/// life and back, its colour is the ramp at t, and its frame is <c>15 - ftol(t · 15.99)</c>, counting
/// 15 down to 0.</item>
/// </list>
/// <b>Every live sprite is placed at the travelling point itself</b>, not where it was born: stock
/// writes the slot's stored position into the sprite (<c>100fc1cf</c>) and then overwrites it with the
/// current point (<c>100fc26a</c>), so the stored one never reaches the screen. The result is a single
/// bright head rather than a trail. The per-slot arrays at +0xc48 and +0x648 are still written on spawn
/// and are dead weight; they are not modelled.
///
/// Stock also computes <c>1 - (i % 17) / 34</c> per slot (<c>100fc18a</c>), which is <b>always 1</b>
/// because the remainder can never reach 34. It is used as the life multiplier at spawn, so it has no
/// effect; whatever it was meant to be, the shipped game does not do it.
///
/// starType 28 is past the end of the expiry table (which covers 1 to 25), so an expired control takes
/// the default <c>100fc366</c> and simply ends — no 5 s drain. The shared tail (<c>100fc2f0</c>) also
/// ends it once it is terminating with nothing alive.
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class StarsCase28 : IStarsStockCase
{
    public const int SlotCount = StarsCase3.SlotCount;

    /// <summary><c>100fc0df</c>: sprites started per call, a literal rather than a field.</summary>
    public const int SpawnsPerCall = 10;

    /// <summary><c>100fc244</c>: frame = 15 - ftol(t * 15.99).</summary>
    public const float FrameScale = 15.989999771118164f;
    public const int TopFrame = 15;

    /// <summary><c>100f81e0</c>: every death time starts here, so all slots begin expired.</summary>
    public const float DeadTime = -100f;

    readonly float _size;
    readonly float _life;
    readonly float[] _start;
    readonly float[] _end;

    readonly float[] _death = new float[SlotCount];
    readonly int[] _slotSerial = new int[SlotCount];
    readonly StarsCase3.Sprite[] _sprites = new StarsCase3.Sprite[SlotCount];
    readonly StarsCase3.Sprite[] _previous = new StarsCase3.Sprite[SlotCount];

    public bool Initialised { get; private set; }
    public bool Terminating { get; set; }

    /// <summary>Terminating and the last step found nothing (<c>100fc2f0</c>).</summary>
    public bool Drained { get; private set; }

    public StarsCase3.Sprite[] Sprites => _sprites;

    /// <summary>Stock +0x16cc: field 30 in seconds.</summary>
    public float Life => _life;

    /// <summary>Where the point had got to on the last step.</summary>
    public float HeadX { get; private set; }
    public float HeadY { get; private set; }
    public float HeadZ { get; private set; }

    public int LiveCount { get; private set; }

    /// <param name="size">Field 28.</param>
    /// <param name="lifeMs">Field 30, milliseconds.</param>
    public StarsCase28(float size, int lifeMs, float[] startArgb, float[] endArgb)
    {
        _size = size;
        _life = (float)(lifeMs / 1000.0);
        _start = startArgb;
        _end = endArgb;
    }

    /// <summary>
    /// One stock Process call. <paramref name="hasLine"/> is false when the control has no hit
    /// location, which stops the spawns exactly as <c>100fc28b</c> does.
    /// </summary>
    public void Step(
        float age, float duration, bool hasLine,
        float sx, float sy, float sz, float ex, float ey, float ez)
    {
        Array.Copy(_sprites, _previous, SlotCount);

        if (!Initialised)
        {
            Initialised = true;
            for (int i = 0; i < SlotCount; i++)
                _death[i] = DeadTime;
        }

        // 100fc122: the point mixes the two ends by the control's own progress.
        float p = (float)(age / (double)duration);
        float q = (float)(1.0 - p);
        HeadX = (float)((float)(ex * (double)p) + (double)(float)(sx * (double)q));
        HeadY = (float)((float)(ey * (double)p) + (double)(float)(sy * (double)q));
        HeadZ = (float)((float)(ez * (double)p) + (double)(float)(sz * (double)q));

        int budget = SpawnsPerCall;
        int live = 0;

        for (int i = 0; i < SlotCount; i++)
        {
            ref StarsCase3.Sprite s = ref _sprites[i];

            if (age < _death[i])
            {
                float t = (float)((age + (double)_life - _death[i]) / _life);

                // 100fc1eb: 1 - (1 - 2t)^2, so it swells to full at half life and back.
                float u = (float)(1.0 - (double)t - t);
                float envelope = (float)(1.0 - (double)u * u);

                // 100fc26a: the sprite sits at the travelling point, not where it was born.
                s.X = HeadX;
                s.Y = HeadY;
                s.Z = HeadZ;
                s.Size = (float)(_size * (double)envelope);
                s.Argb = StockColorRamp.Eval(_start, _end, t);
                s.Frame = TopFrame - (int)(t * (double)FrameScale);
                s.Visible = true;
                s.Serial = _slotSerial[i];
                live++;
                continue;
            }

            if (budget > 0 && !Terminating && hasLine)
            {
                // 100fc2b9: life * the always-1 multiplier described above.
                _death[i] = (float)(_life + (double)age);
                _slotSerial[i]++;
                budget--;
                live++;
                // Stock writes the point into the slot's own array here; nothing ever reads it back.
                continue;
            }

            s.Visible = false;
        }

        LiveCount = live;
        Drained = Terminating && live == 0;
    }

    /// <summary>Port-only; see <see cref="StarsCase3.Blend"/>.</summary>
    public StarsCase3.Sprite Blend(int i, float t) => StarsCase3.Blend(_previous[i], _sprites[i], t);
}
