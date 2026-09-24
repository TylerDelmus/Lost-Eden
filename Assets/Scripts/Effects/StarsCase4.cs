using System;

/// <summary>
/// Stock <c>_GfxControlStars_t</c> starType 4: sparks that leave the locator with an upward kick and
/// keep rising (269470's hand flames, buff 72381). One <see cref="Step"/> is one call of the stock
/// Process, <c>FUN_100f8491</c>, whose case 4 runs <c>Gamecode 100f8c39</c>..<c>100f8e6c</c>. Like case 3 it
/// takes no delta: spawns, drag and lift are per call.
///
/// Fields (loader <c>FUN_100f72a9</c>): 18-21 / 22-25 colour ramp (+0x1698), 28 size (+0x16d4),
/// 29 launch speed (+0x16d8), 30 life in ms (int bits; +0x16cc = field 30 / 1000). The lazy init
/// (<c>100f81e0</c>, shared with case 3) seeds every timer to -100.
///
/// Per call, for each of the 128 slots:
/// <list type="bullet">
/// <item>Alive (age &lt; timer): <c>v = v * 0.95 - (0, -0.1, 0)</c>, <c>p += v * 0.1</c>; the record
/// takes p, frame <c>_ftol((timer - age) * 64 / life)</c> clamped to 0..63, t = (life + age - timer) / life,
/// width = height = (2 - t) * field 28, colour the ramp at t.</item>
/// <item>Due: while fewer than 2 have spawned this call and it is not terminating, p = the locator
/// (<c>1010640a</c>), v = field 29 * ((0, 3, 0) + a random point in the unit ball), timer = life + age;
/// the record is left as it was. Otherwise the record is hidden.</item>
/// </list>
/// On expiry it drains like case 3 (the <c>0x100fc374</c> entry is 0).
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class StarsCase4 : IStarsStockCase
{
    public const int SlotCount = StarsCase3.SlotCount;

    /// <summary>Slots a single Process may open: <c>100f8c62 MOV [EBP-0x20], 2</c>.</summary>
    public const int SpawnsPerStep = 2;

    /// <summary>100f8ca7: drag on the velocity, a float constant.</summary>
    public const float Drag = 0.949999988079071f;

    /// <summary>100f8c8c: the vector subtracted from the velocity each call is (0, -0.1, 0).</summary>
    public const float Lift = 0.10000000149011612f;

    /// <summary>100f8cc7: position step.</summary>
    public const float Step01 = 0.10000000149011612f;

    /// <summary>100f8de0: the upward part of the launch direction.</summary>
    public const float LaunchUp = 3f;

    /// <summary>100f8d1a: frame index range 0..63.</summary>
    public const int FrameCount = 64;

    const float UnspawnedTimer = -100f;

    readonly float _life;
    readonly float _size;
    readonly float _speed;
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

    /// <summary>Stock +0x1648.</summary>
    public bool Terminating { get; set; }

    /// <summary>Slots spawned or alive on the last step. Stock's <c>[EBP-0xc]</c>.</summary>
    public int LastCount { get; private set; }

    /// <summary>Terminating and the last step saw nothing (<c>100f884e</c>).</summary>
    public bool Drained => Terminating && LastCount == 0;

    public float Life => _life;
    public StarsCase3.Sprite[] Sprites => _sprites;

    /// <param name="size">Field 28.</param>
    /// <param name="speed">Field 29.</param>
    /// <param name="startArgb">Fields 18-21, A,R,G,B.</param>
    /// <param name="endArgb">Fields 22-25, A,R,G,B.</param>
    /// <param name="rand">Stock <c>rand()</c>: 0..0x7fff.</param>
    public StarsCase4(float lifeSeconds, float size, float speed, float[] startArgb, float[] endArgb, Func<int> rand)
    {
        _life = lifeSeconds;
        _size = size;
        _speed = speed;
        _start = startArgb;
        _end = endArgb;
        _rand = rand ?? throw new ArgumentNullException(nameof(rand));

        for (int i = 0; i < SlotCount; i++)
        {
            _timer[i] = UnspawnedTimer;
            _sprites[i].Argb = 0xffffffffu;
        }
    }

    /// <summary>One stock Process call at <paramref name="age"/>; o is the locator position.</summary>
    public void Step(float age, float ox, float oy, float oz)
    {
        Array.Copy(_sprites, _previous, SlotCount);

        int budget = SpawnsPerStep;
        int count = 0;

        for (int i = 0; i < SlotCount; i++)
        {
            if (age < _timer[i])
            {
                _vx[i] = _vx[i] * Drag;
                _vy[i] = _vy[i] * Drag + Lift;
                _vz[i] = _vz[i] * Drag;
                _px[i] += _vx[i] * Step01;
                _py[i] += _vy[i] * Step01;
                _pz[i] += _vz[i] * Step01;

                ref StarsCase3.Sprite s = ref _sprites[i];
                s.X = _px[i];
                s.Y = _py[i];
                s.Z = _pz[i];
                s.Frame = FrameIndex(_timer[i], age, _life);
                float t = StarsCase3.LifeFraction(_timer[i], age, _life);
                s.Size = Size(t, _size);
                s.Argb = StockColorRamp.Eval(_start, _end, t);
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

            StarsCase3.RandomPointInUnitBall(_rand, out float x, out float y, out float z);
            _px[i] = ox;
            _py[i] = oy;
            _pz[i] = oz;
            _vx[i] = (0f + x) * _speed;
            _vy[i] = (LaunchUp + y) * _speed;
            _vz[i] = (0f + z) * _speed;
            _timer[i] = _life + age;
            _slotSerial[i] = ++_serial;
            count++;
            budget--;
            // As in case 3, the spawn call leaves the sprite record as it was.
        }

        LastCount = count;
    }

    /// <summary>100f8d15..100f8d3b: _ftol((timer - age) * 64 / life), clamped to 0..63.</summary>
    public static int FrameIndex(float timer, float age, float life)
    {
        int frame = (int)(((double)timer - age) * 64.0 / life);
        if (frame > FrameCount - 1)
            return FrameCount - 1;
        return frame < 0 ? 0 : frame;
    }

    /// <summary>100f8d61..100f8d70: (2 - t) * field 28.</summary>
    public static float Size(float t, float size) => (float)((2.0 - t) * size);

    /// <summary>Port-only; see <see cref="StarsCase3.Blend"/>.</summary>
    public StarsCase3.Sprite Blend(int i, float t) => StarsCase3.Blend(_previous[i], _sprites[i], t);
}
