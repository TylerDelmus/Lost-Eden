using System;

/// <summary>
/// Stock <c>_GfxControlStars_t</c> starType 14 (<c>Gamecode 100f9da3</c>..<c>100f9f5e</c>): a ring of
/// sparks that each fly straight out from the locator while the ring as a whole rides up through it,
/// e.g. 43118-43120 (the hits of 95796 Captivated Gaze and its kin) and 14300.
///
/// Fields (loader <c>FUN_100f72a9</c>): 11 spark size (+0x167c), 18-21 / 22-25 colour ramp,
/// 26 duration, 28 reach (+0x16d4), 29 the turn between one spawn and the next in radians (+0x16d8),
/// 30 the spark's life in ms (+0x16dc, /1000 into +0x16cc by the lazy init <c>100f7fe7</c>),
/// 31 how many sparks may be spawned per call (+0x16e0).
///
/// The lazy init for this type (<c>100f81e0</c>) loads -100 and jumps to the shared fill
/// (<c>100f80a6</c>), which <c>rep stosd</c>s it over all 128 entries of the death-time array at
/// +0x1448, so every slot starts expired and the first calls spend their whole budget spawning.
///
/// Per call, with p = age / duration (or 1 once terminating, <c>100f9dbf</c>) and a budget of field 31:
/// <list type="bullet">
/// <item>A slot whose death time is still ahead of the age is <b>alive</b>: t = (age + life - death) /
/// life runs 0 to 1 over its life, and it sits at the locator plus
/// (dir.x · reach · t, dir.y + 2p - 1, dir.z · reach · t) — so it flies straight out in the xz plane
/// while the whole ring slides from one below the locator to one above it over the duration. Its size
/// is field 11 on both axes, its colour the ramp at t, its frame 0.</item>
/// <item>Otherwise, if there is budget left and it is not terminating, it <b>spawns</b>: the running
/// angle (+0x1650) advances by field 29 and the direction becomes (sin angle, 0, cos angle), the death
/// time becomes age + life, and the budget drops by one. <b>Stock does not touch the sprite record on
/// the spawning call</b>, so the slot draws one more frame wherever it died before the new spark
/// appears; that is reproduced here.</item>
/// <item>Otherwise the slot is hidden.</item>
/// </list>
/// Because the direction's y is always 0, every spark shares the ring's height: the shape is a circle
/// of outward streaks, not a sphere.
///
/// On expiry stock gives this type the grace path (its <c>0x100fc374</c> entry is 0, <c>100f84ed</c>):
/// the duration gains 5 seconds, spawning stops and the control keeps running until nothing is left
/// alive (<c>100f9f49</c>).
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class StarsCase14 : IStarsStockCase
{
    public const int SlotCount = StarsCase3.SlotCount;

    /// <summary><c>100f81e0</c>: the death time every slot starts on, so all of them begin expired.</summary>
    public const float DeadTime = -100f;

    readonly float _size;
    readonly float _reach;
    readonly float _turn;
    readonly float _life;
    readonly int _spawnsPerCall;
    readonly float[] _start;
    readonly float[] _end;

    // +0x1448 death times, +0xc48 directions, +0x1650 the running angle.
    readonly float[] _death = new float[SlotCount];
    readonly float[] _dx = new float[SlotCount];
    readonly float[] _dy = new float[SlotCount];
    readonly float[] _dz = new float[SlotCount];
    float _angle;

    readonly int[] _slotSerial = new int[SlotCount];
    readonly StarsCase3.Sprite[] _sprites = new StarsCase3.Sprite[SlotCount];
    readonly StarsCase3.Sprite[] _previous = new StarsCase3.Sprite[SlotCount];

    public bool Initialised { get; private set; }
    public bool Terminating { get; set; }

    /// <summary>The last step found nothing alive and nothing spawned (<c>100f9f56</c>).</summary>
    public bool Drained { get; private set; }

    public StarsCase3.Sprite[] Sprites => _sprites;

    /// <summary>Stock +0x1650, the angle the next spawn will be turned to.</summary>
    public float Angle => _angle;

    /// <summary>Stock +0x16cc: field 30 in seconds.</summary>
    public float Life => _life;

    /// <summary>How many of the 128 slots were alive or spawned on the last step.</summary>
    public int LiveCount { get; private set; }

    /// <param name="size">Field 11.</param>
    /// <param name="reach">Field 28.</param>
    /// <param name="turn">Field 29, radians between one spawn and the next.</param>
    /// <param name="lifeMs">Field 30, milliseconds.</param>
    /// <param name="spawnsPerCall">Field 31.</param>
    public StarsCase14(
        float size, float reach, float turn, int lifeMs, int spawnsPerCall,
        float[] startArgb, float[] endArgb)
    {
        _size = size;
        _reach = reach;
        _turn = turn;
        _life = (float)(lifeMs / 1000.0);
        _spawnsPerCall = spawnsPerCall;
        _start = startArgb;
        _end = endArgb;
    }

    /// <summary>One stock Process call; <paramref name="duration"/> is the control's (+0x10).</summary>
    public void Step(float age, float duration, float ox, float oy, float oz)
    {
        Array.Copy(_sprites, _previous, SlotCount);

        if (!Initialised)
        {
            Initialised = true;
            _angle = 0f;
            for (int i = 0; i < SlotCount; i++)
                _death[i] = DeadTime;
        }

        // 100f9dbf: once terminating the ring is pinned at the top of its travel.
        float p = Terminating ? 1f : (float)(age / (double)duration);
        float rise = (float)((double)p + p - 1.0);

        int budget = _spawnsPerCall;
        int live = 0;

        for (int i = 0; i < SlotCount; i++)
        {
            ref StarsCase3.Sprite s = ref _sprites[i];

            if (age < _death[i])
            {
                // 100f9e04: t runs 0 to 1 across the spark's own life.
                float t = (float)((age + (double)_life - _death[i]) / _life);
                float r = (float)(_reach * (double)t);

                s.X = (float)(ox + (double)(float)(_dx[i] * (double)r));
                s.Y = (float)(oy + (double)(float)(_dy[i] + (double)rise));
                s.Z = (float)(oz + (double)(float)(_dz[i] * (double)r));
                s.Size = _size;
                s.Argb = StockColorRamp.Eval(_start, _end, t);
                s.Frame = 0;
                s.Visible = true;
                s.Serial = _slotSerial[i];
                live++;
                continue;
            }

            if (budget > 0 && !Terminating)
            {
                // 100f9ec7: the angle walks on by field 29 for every spark, so consecutive sparks are
                // that far apart round the ring.
                _angle = (float)(_turn + (double)_angle);
                _dx[i] = (float)Math.Sin(_angle);
                _dy[i] = 0f;
                _dz[i] = (float)Math.Cos(_angle);
                _death[i] = (float)(_life + (double)age);
                _slotSerial[i]++;
                budget--;
                live++;
                // Stock leaves the sprite record alone here; the slot draws its old frame once more.
                continue;
            }

            s.Visible = false;
        }

        LiveCount = live;
        // 100f9f49: only a terminating case ends, and only once nothing is left.
        Drained = Terminating && live == 0;
    }

    /// <summary>Port-only; see <see cref="StarsCase3.Blend"/>.</summary>
    public StarsCase3.Sprite Blend(int i, float t) => StarsCase3.Blend(_previous[i], _sprites[i], t);
}
