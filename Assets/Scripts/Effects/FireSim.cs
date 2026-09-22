using System;

/// <summary>
/// Stock <c>_GfxControlFire_t</c> (typeCode 1004, 0x3ec; vftable <c>Gamecode 1016ccdc</c>, ctor
/// <c>100dcb72</c>, loader <c>100dc433</c>, init <c>100dc59c</c>, Process <c>100dbed5</c>) and the
/// DisplaySystem <c>GfxVisualSprite2Type0</c> it fills (<see cref="Sprite2Type0Visual"/>). One
/// <see cref="Step"/> is one stock Process call.
///
/// Fields: 0 flags (bits 0-2 the locator's; 0x100 sends the sprites up the world y instead of down the
/// locator's z), 1-6 the locator offset and turn, 2 also a lift added to each sprite's y, 7 attach,
/// 8 duration, 9 material, 10 sprites per second, 11/12 disc radius min/max, 13 speed, 14/15 width
/// start/end, 16/17 height start/end, 18-21 / 22-25 start / end colour A,R,G,B, 26 life, 27/28 the wind
/// band's bottom and top above each sprite's spawn height, 29 wind mode, 30 wind scale, 31 <c>rand()</c>
/// mask gating a spawn call.
///
/// Init: a pool of _ftol(field 10 * 1.5 * field 26); InitSpriteDefault gives every sprite the same size,
/// colour and frame ramps over the life of field 26. The visual is always additive (ctor bool 1).
///
/// Per call: when (rand() &amp; field 31) == 0 and (duration &lt; 0 or age &lt; duration - field 26),
/// sprites are spawned until the counter reaches _ftol(field 10 * age); then every sprite moves. A spawn
/// takes a radius r from R250 (<c>1013dec9</c>) in field 11..12 and a random unit d (<c>100d3005</c>),
/// sits at (d.x r, d.z r, 0) through the locator's world-mode matrix (<c>101063d9</c>, identity in
/// local mode) with field 2 added to y, and flies at |d.y| * field 13 / field 26 down the locator's
/// world-mode z (<c>1010649b</c>), or up the world y with flag 0x100. NewSprite (<c>10025be0</c>) gets
/// the wind mode, band and scale.
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class FireSim
{
    public const int FlagLocal = 2;
    public const int FlagWorldUp = 0x100;

    readonly int _flags;
    readonly float _lift;
    readonly float _rate;
    readonly float _r0, _r1;
    readonly float _speed;
    readonly float[] _start = new float[4];
    readonly float[] _end = new float[4];
    readonly float _life;
    readonly float _windLow, _windHigh;
    readonly int _windMode;
    readonly float _windScale;
    readonly int _randMask;
    readonly Func<float> _rand01;
    readonly Func<int> _crtRand;
    readonly Sprite2Type0Visual _visual;
    Sprite2Type0Visual.Sprite _default;
    int _spawned;

    /// <summary>Stock +0x10: field 8, or what SetDuration / TerminateGracefully put there.</summary>
    public float Duration { get; set; }

    /// <summary>Visual +0x228: sprites alive after the last <see cref="Step"/>.</summary>
    public int LiveCount => _visual.LiveCount;

    public int Flags => _flags;
    public bool Local => (_flags & FlagLocal) != 0;
    public float Life => _life;
    public Sprite2Type0Visual.Sprite[] Sprites => _visual.Sprites;

    /// <param name="rand01">Stock's R250 source at 0x102ead20: uniform in [0, 1).</param>
    /// <param name="crtRand">Stock <c>rand()</c>, 0..0x7fff.</param>
    public FireSim(float[] fields, int firstFrame, int lastFrame, Func<float> rand01, Func<int> crtRand)
    {
        _rand01 = rand01 ?? throw new ArgumentNullException(nameof(rand01));
        _crtRand = crtRand ?? throw new ArgumentNullException(nameof(crtRand));

        _flags = Int(fields, 0);
        Duration = F(fields, 8);
        _rate = F(fields, 10);
        _r0 = F(fields, 11);
        _r1 = F(fields, 12);
        _speed = F(fields, 13);
        float w0 = F(fields, 14), w1 = F(fields, 15);
        float h0 = F(fields, 16), h1 = F(fields, 17);
        for (int i = 0; i < 4; i++)
        {
            _start[i] = F(fields, 18 + i);
            _end[i] = F(fields, 22 + i);
        }
        _life = F(fields, 26);
        _windLow = F(fields, 27);
        _windHigh = F(fields, 28);
        _windMode = Int(fields, 29);
        _windScale = F(fields, 30);
        _randMask = Int(fields, 31);
        _lift = F(fields, 2);

        // 100dc6f3: the pool.
        _visual = new Sprite2Type0Visual((int)(_rate * 1.5 * _life), _rand01);

        // 100dc7db: InitSpriteDefault.
        float inv = 1f / _life;
        _default = new Sprite2Type0Visual.Sprite
        {
            Width = w0,
            Height = h0,
            DWidth = (w1 - w0) * inv,
            DHeight = (h1 - h0) * inv,
            Life = _life,
            Frame = firstFrame,
            FrameRate = (lastFrame - firstFrame) * inv,
        };
        SetDefaultColor();
    }

    /// <summary>Slot 11 (<c>100dc186</c>): the start colour, for later spawns.</summary>
    public void SetStartColor(float a, float r, float g, float b)
    {
        _start[0] = a; _start[1] = r; _start[2] = g; _start[3] = b;
        SetDefaultColor();
    }

    /// <summary>Slot 12 (<c>100dc22a</c>): the end colour, for later spawns.</summary>
    public void SetStopColor(float a, float r, float g, float b)
    {
        _end[0] = a; _end[1] = r; _end[2] = g; _end[3] = b;
        SetDefaultColor();
    }

    /// <summary>Slot 6 (<c>100dc16f</c>): duration = age + field 26.</summary>
    public void TerminateGracefully(float age) => Duration = _life + age;

    /// <summary>
    /// One Process body after the base timer, at <paramref name="age"/> with delta <paramref name="dt"/>.
    /// <paramref name="ox"/>.. is the locator's world-mode position and <paramref name="axes"/> its
    /// world-mode X, Y, Z axes (9 floats); pass zero and null in local mode. The wind is the effect
    /// handler's GetWind.
    /// </summary>
    public void Step(float age, float dt, float ox, float oy, float oz, float[] axes, float windX, float windY, float windZ)
    {
        if ((_crtRand() & _randMask) == 0
            && (!(0f <= Duration) || age < Duration - _life))
        {
            int target = (int)(_rate * age);
            while (_spawned < target)
            {
                _spawned++;
                Spawn(ox, oy, oz, axes);
            }
        }

        _visual.ProcessSprites(dt, windX, windY, windZ);
    }

    void Spawn(float ox, float oy, float oz, float[] axes)
    {
        float radius = (float)(_rand01() * (_r1 - _r0) + _r0);
        ElectraSim.RandomUnitVector(_crtRand, out float dx, out float dy, out float dz);
        float lx = dx * radius;
        float ly = dz * radius;
        float k = Math.Abs(dy);

        // 100d76dc: (lx, ly, 0) through the world-mode matrix, then the lift on y.
        float px, py, pz;
        float zx, zy, zz;
        if (axes == null)
        {
            px = lx; py = ly; pz = 0f;
            zx = 0f; zy = 0f; zz = 1f;
        }
        else
        {
            px = (float)(axes[0] * (double)lx + axes[3] * (double)ly + ox);
            py = (float)(axes[1] * (double)lx + axes[4] * (double)ly + oy);
            pz = (float)(axes[2] * (double)lx + axes[5] * (double)ly + oz);
            zx = axes[6]; zy = axes[7]; zz = axes[8];
        }
        py = _lift + py;

        // 100dbfd6: down the locator's z, or up the world y; * |d.y| * field 13 / field 26.
        float ux, uy, uz;
        if ((_flags & FlagWorldUp) != 0)
        {
            ux = 0f; uy = 1f; uz = 0f;
        }
        else
        {
            ux = -zx; uy = -zy; uz = -zz;
        }

        Sprite2Type0Visual.Sprite sprite = _default;
        sprite.X = px; sprite.Y = py; sprite.Z = pz;
        sprite.VX = ux * k * _speed / _life;
        sprite.VY = uy * k * _speed / _life;
        sprite.VZ = uz * k * _speed / _life;
        sprite.WindMode = _windMode;
        sprite.WindLow = _windLow + py;
        sprite.WindHigh = _windHigh + py;
        sprite.WindScale = _windScale;
        _visual.NewSprite(sprite);
    }

    /// <summary><c>SetDefaultColor</c> (<c>1002597e</c>): the start colour and its rate over field 26.</summary>
    void SetDefaultColor()
    {
        float inv = 1f / _life;
        _default.A = _start[0];
        _default.R = _start[1];
        _default.G = _start[2];
        _default.B = _start[3];
        _default.DA = (_end[0] - _start[0]) * inv;
        _default.DR = (_end[1] - _start[1]) * inv;
        _default.DG = (_end[2] - _start[2]) * inv;
        _default.DB = (_end[3] - _start[3]) * inv;
    }

    static float F(float[] f, int i) => f != null && i >= 0 && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i >= 0 && i < f.Length ? BitConverter.SingleToInt32Bits(f[i]) : 0;
}
