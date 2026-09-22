using System;

/// <summary>
/// Stock <c>_GfxControlSmoke_t</c> (typeCode 1009, 0x3f1; vftable <c>Gamecode 1016db34</c>, ctor
/// <c>100f12bf</c>, loader <c>100f0b6b</c>, init <c>100f0cd4</c>, Process <c>100f04bd</c>) and the
/// DisplaySystem <c>GfxVisualSprite2Type0</c> it fills (<see cref="Sprite2Type0Visual"/>). One
/// <see cref="Step"/> is one stock Process call.
///
/// Fields: 0 flags (bits 0-2 the locator's; 0x400 keeps it running when the locator is lost), 1-6 the
/// locator offset and turn, 7 attach, 8 duration, 9 material, 10 sprites per second, 11/12 disc radius
/// min/max, 13 speed, 14/15 width start/end, 16/17 height start/end, 18-21 / 22-25 start / end colour
/// A,R,G,B, 26 life, 27/28 the wind band's bottom and top above each sprite's spawn height, 29 wind mode,
/// 30 wind scale, 31/32 height offset min/max.
///
/// Init: a pool of _ftol(field 10 * 1.5 * field 26). The visual is additive only for effect 80005
/// (0x13885); every other Smoke alpha-blends. Its InitSpriteDefault (with a height rate of f17 - f17)
/// is never used: every spawn goes through the full NewSprite.
///
/// Per call: when (duration &lt; 0 or age &lt; duration - field 26), sprites are spawned until the counter
/// reaches _ftol(field 10 * age); then every sprite moves under the handler's smoothed wind (GetSmoothWind,
/// +0x30). A spawn draws from R250 (<c>1013dec9</c>), in this order: life = field 26 (1 + 0.3 r), angle
/// a in 0..2π, angle b in 0..π/2, radius in field 11..12, size scale s = 1 + 0.3 r, height in field
/// 31..32, and wind scale (0.9 r + 0.1) * field 30. It sits at (cos a cos b r, height, sin a cos b r)
/// through the locator's world-mode matrix (<c>101063d9</c>, identity in local mode) and flies at
/// (0.3 x, 0.3 z, -1) * field 13 / life through the same rotation (<c>100dcd23</c>). Width and height
/// start at field 14 s / field 16 s and ramp by (end - start) s over the life; colour ramps field 18-21 to
/// 22-25 and the frame first to last over it.
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class SmokeSim
{
    public const int FlagLocal = 2;
    public const int FlagKeepWhenLost = 0x400;

    /// <summary><c>100f0ce7</c>: the one Smoke record whose visual is additive.</summary>
    public const int AdditiveEffectId = 0x13885;

    readonly int _flags;
    readonly float _rate;
    readonly float _r0, _r1;
    readonly float _speed;
    readonly float _w0, _w1, _h0, _h1;
    readonly float[] _start = new float[4];
    readonly float[] _end = new float[4];
    readonly float _life;
    readonly float _windLow, _windHigh;
    readonly int _windMode;
    readonly float _windScale;
    readonly float _y0, _y1;
    readonly float _firstFrame, _lastFrame;
    readonly Func<float> _rand01;
    readonly Sprite2Type0Visual _visual;
    int _spawned;

    /// <summary>Stock +0x10: field 8, or what SetDuration / TerminateGracefully put there.</summary>
    public float Duration { get; set; }

    /// <summary>Visual +0x228: sprites alive after the last <see cref="Step"/>.</summary>
    public int LiveCount => _visual.LiveCount;

    public int Flags => _flags;
    public bool Local => (_flags & FlagLocal) != 0;
    public bool KeepsRunningWhenLost => (_flags & FlagKeepWhenLost) != 0;
    public bool Additive { get; }
    public float Life => _life;
    public int WindMode => _windMode;
    public Sprite2Type0Visual.Sprite[] Sprites => _visual.Sprites;

    /// <param name="effectId">The template's id: 80005 is the one additive Smoke.</param>
    /// <param name="rand01">Stock's R250 source at 0x102ead20: uniform in [0, 1).</param>
    public SmokeSim(float[] fields, int effectId, int firstFrame, int lastFrame, Func<float> rand01)
    {
        _rand01 = rand01 ?? throw new ArgumentNullException(nameof(rand01));

        _flags = Int(fields, 0);
        Duration = F(fields, 8);
        _rate = F(fields, 10);
        _r0 = F(fields, 11);
        _r1 = F(fields, 12);
        _speed = F(fields, 13);
        _w0 = F(fields, 14);
        _w1 = F(fields, 15);
        _h0 = F(fields, 16);
        _h1 = F(fields, 17);
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
        _y0 = F(fields, 31);
        _y1 = F(fields, 32);
        _firstFrame = firstFrame;
        _lastFrame = lastFrame;
        Additive = effectId == AdditiveEffectId;

        // 100f0e3d: the pool.
        _visual = new Sprite2Type0Visual((int)(_rate * 1.5 * _life), _rand01);
    }

    /// <summary>Slot 11 (<c>100f08be</c>): the start colour, for later spawns.</summary>
    public void SetStartColor(float a, float r, float g, float b)
    {
        _start[0] = a; _start[1] = r; _start[2] = g; _start[3] = b;
    }

    /// <summary>Slot 12 (<c>100f0962</c>): the end colour, for later spawns.</summary>
    public void SetStopColor(float a, float r, float g, float b)
    {
        _end[0] = a; _end[1] = r; _end[2] = g; _end[3] = b;
    }

    /// <summary>Slot 6 (<c>100f08a7</c>): duration = age + field 26.</summary>
    public void TerminateGracefully(float age) => Duration = _life + age;

    /// <summary>
    /// One Process body after the base timer, at <paramref name="age"/> with delta <paramref name="dt"/>.
    /// <paramref name="ox"/>.. is the locator's world-mode position and <paramref name="axes"/> its
    /// world-mode X, Y, Z axes (9 floats); pass zero and null in local mode. The wind is the effect
    /// handler's GetSmoothWind.
    /// </summary>
    public void Step(float age, float dt, float ox, float oy, float oz, float[] axes, float windX, float windY, float windZ)
    {
        if (!(0f <= Duration) || age < Duration - _life)
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
        // 100f0547..100f0645, in stock's draw order.
        float life = (float)((_rand01() * 0.2999999523162842 + 1.0) * _life);
        float inv = (float)(1.0 / life);
        float a = (float)(_rand01() * 6.2831854820251465);
        float b = (float)(_rand01() * 1.5707963705062866);
        float radius = (float)(_rand01() * (_r1 - _r0) + _r0);
        double ring = (float)Math.Cos(b) * (double)radius;
        float lx = (float)((float)Math.Cos(a) * ring);
        float lz = (float)((float)Math.Sin(a) * ring);
        float scale = (float)(_rand01() * 0.2999999523162842 + 1.0);
        float ly = (float)(_rand01() * (_y1 - _y0) + _y0);

        float dx = (float)(lx * 0.30000001192092896);
        float dy = (float)(0.30000001192092896 * lz);
        float vx = dx * _speed * inv;
        float vy = dy * _speed * inv;
        float vz = -1f * _speed * inv;

        float px, py, pz, wx, wy, wz;
        if (axes == null)
        {
            px = lx; py = ly; pz = lz;
            wx = vx; wy = vy; wz = vz;
        }
        else
        {
            // 100d76dc (point) and 100dcd23 (direction).
            px = (float)(axes[0] * (double)lx + axes[3] * (double)ly + axes[6] * (double)lz + ox);
            py = (float)(axes[1] * (double)lx + axes[4] * (double)ly + axes[7] * (double)lz + oy);
            pz = (float)(axes[2] * (double)lx + axes[5] * (double)ly + axes[8] * (double)lz + oz);
            wx = (float)(axes[0] * (double)vx + axes[3] * (double)vy + axes[6] * (double)vz);
            wy = (float)(axes[1] * (double)vx + axes[4] * (double)vy + axes[7] * (double)vz);
            wz = (float)(axes[2] * (double)vx + axes[5] * (double)vy + axes[8] * (double)vz);
        }

        float wind = (float)((_rand01() * 0.8999999761581421 + 0.10000000149011612) * _windScale);

        // Full NewSprite (100261de).
        _visual.NewSprite(new Sprite2Type0Visual.Sprite
        {
            X = px, Y = py, Z = pz,
            VX = wx, VY = wy, VZ = wz,
            Width = (float)(scale * (double)_w0),
            Height = (float)(_h0 * (double)scale),
            DWidth = (float)(((double)_w1 - _w0) * scale * inv),
            DHeight = (float)(((double)_h1 - _h0) * scale * inv),
            Life = life,
            A = _start[0], R = _start[1], G = _start[2], B = _start[3],
            DA = (_end[0] - _start[0]) * inv,
            DR = (_end[1] - _start[1]) * inv,
            DG = (_end[2] - _start[2]) * inv,
            DB = (_end[3] - _start[3]) * inv,
            Frame = _firstFrame,
            FrameRate = (_lastFrame - _firstFrame) * inv,
            WindMode = _windMode,
            WindLow = py + _windLow,
            WindHigh = _windHigh + py,
            WindScale = wind,
        });
    }

    static float F(float[] f, int i) => f != null && i >= 0 && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i >= 0 && i < f.Length ? BitConverter.SingleToInt32Bits(f[i]) : 0;
}
