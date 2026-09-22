using System;

/// <summary>
/// Stock <c>_GfxControlFlare_t</c> (typeCode 1005, ctor <c>Gamecode 100dd63c</c>, Process
/// <c>100ddc6d</c>) driving the sprite pool of its visual, <see cref="FlareType0Visual"/>. One
/// <see cref="Step"/> is one stock Process call with that call's delta.
///
/// Fields (loader <c>FUN_100dcedb</c>):
///   0 flags (0x100 swaps the two end scales, 0x200 readies once the pool is empty)
///   8 duration, 10 sprites per second, 12/13 size0 start/end, 14/15 size1 start/end,
///   16-19 start A,R,G,B, 20-23 end A,R,G,B, 24 rand() mask gating a spawn call,
///   25/26 angle a range, 27/28 angle b range, 29/30 length range, 31 burst / minimum pool,
///   32/33 end scales, 34/35 sprite life range.
///
/// Each sprite is a segment: both ends start at the emitter and slide apart along one random
/// direction, ends scaled by fields 32 and 33, over the sprite's life.
/// <see cref="FlareType0Visual.SpriteQuad"/> gives the stock quad for it. No Unity dependency so the
/// maths can be asserted from unit tests.
/// </summary>
public sealed class FlareType0Sim
{
    public const int FlagSwapEnds = 0x100;
    public const int FlagReadyWhenEmpty = 0x200;

    readonly int _flags;
    readonly float _rate;
    readonly float _size0, _size0End, _size1, _size1End;
    readonly float[] _start = new float[4];
    readonly float[] _end = new float[4];
    readonly int _randMask;
    readonly float _a0, _a1, _b0, _b1, _m0, _m1;
    readonly float _scale32, _scale33;
    readonly float _life0, _life1;
    readonly float _firstFrame, _lastFrame;
    readonly Func<float> _rand01;
    readonly Func<int> _crtRand;
    readonly FlareType0Visual _visual;
    int _spawned;

    /// <summary>Stock +0x10. Field 8; <see cref="TerminateGracefully"/> and SetDuration move it.</summary>
    public float Duration { get; set; }

    /// <summary>Live sprites after the last <see cref="Step"/> (visual +0x224).</summary>
    public int LiveCount => _visual.LiveCount;

    public int Flags => _flags;
    public float Life1 => _life1;
    public FlareType0Visual.Sprite[] Sprites => _visual.Sprites;

    /// <param name="fields">The template's field array (floats; ints read as raw bits).</param>
    /// <param name="rand01">Stock's float RNG at 0x102ead20: uniform in [0, 1).</param>
    /// <param name="crtRand">Stock <c>rand()</c>, 0..0x7fff.</param>
    public FlareType0Sim(float[] fields, int firstFrame, int lastFrame, Func<float> rand01, Func<int> crtRand)
    {
        _rand01 = rand01 ?? throw new ArgumentNullException(nameof(rand01));
        _crtRand = crtRand ?? throw new ArgumentNullException(nameof(crtRand));

        _flags = Int(fields, 0);
        Duration = F(fields, 8);
        _rate = F(fields, 10);
        _size0 = F(fields, 12);
        _size0End = F(fields, 13);
        _size1 = F(fields, 14);
        _size1End = F(fields, 15);
        for (int c = 0; c < 4; c++)
        {
            _start[c] = F(fields, 16 + c);
            _end[c] = F(fields, 20 + c);
        }
        _randMask = Int(fields, 24);
        _a0 = F(fields, 25);
        _a1 = F(fields, 26);
        _b0 = F(fields, 27);
        _b1 = F(fields, 28);
        _m0 = F(fields, 29);
        _m1 = F(fields, 30);
        int burst = Int(fields, 31);
        _scale32 = F(fields, 32);
        _scale33 = F(fields, 33);
        _life0 = F(fields, 34);
        _life1 = F(fields, 35);
        _firstFrame = firstFrame;
        _lastFrame = lastFrame;

        // Visual init FUN_100dd071: pool = max(_ftol(rate * 1.5 * life1), field31); the spawn counter
        // then starts at -field31 so the first call releases field31 sprites (100dd215..).
        int pool = (int)(_rate * 1.5 * _life1);
        if (pool < burst)
            pool = burst;
        // InitSpriteDefault's life is field 35.
        _visual = new FlareType0Visual(pool, _life1);
        _spawned = -burst;
    }

    /// <summary>Stock slot 11: start colour A,R,G,B (+0x54..+0x60). Spell1 pushes its own.</summary>
    public void SetStartColor(float a, float r, float g, float b)
    {
        _start[0] = a;
        _start[1] = r;
        _start[2] = g;
        _start[3] = b;
    }

    /// <summary>Stock slot 12: end colour A,R,G,B (+0x64..+0x70).</summary>
    public void SetStopColor(float a, float r, float g, float b)
    {
        _end[0] = a;
        _end[1] = r;
        _end[2] = g;
        _end[3] = b;
    }

    /// <summary>Stock slot 6 (<c>100dcd98</c>): duration = age + field 35, so spawning stops now.</summary>
    public void TerminateGracefully(float age) => Duration = age + _life1;

    /// <summary>
    /// One stock Process after the base timer (<c>100ddc6d</c>). <paramref name="ox"/>.. is the spawn
    /// origin and <paramref name="rot"/> the 3x3 applied to each spawn direction: the locator's
    /// position and rotation in world mode, zero and identity in local mode (field 0 bit 1), where the
    /// visual carries the locator instead. Returns true when stock would ready the control.
    /// </summary>
    public bool Step(float age, float dt, float ox, float oy, float oz, float[] rot)
    {
        if ((_randMask & _crtRand()) == 0 && (Duration < 0f || age < Duration - _life1))
        {
            int target = (int)(_rate * age);
            while (_spawned < target)
            {
                _spawned++;
                Spawn(dt, ox, oy, oz, rot);
            }
        }

        _visual.ProcessSprites(dt);
        return (_flags & FlagReadyWhenEmpty) != 0 && LiveCount == 0;
    }

    /// <summary>The spawn at <c>FUN_100dd2eb</c>.</summary>
    void Spawn(float dt, float ox, float oy, float oz, float[] rot)
    {
        float a = (_a1 - _a0) * _rand01() + _a0;
        float b = (_b1 - _b0) * _rand01() + _b0;
        float m = (_m1 - _m0) * _rand01() + _m0;

        float cb = (float)Math.Cos(b) * m;
        float dx = (float)Math.Cos(a) * cb;
        float dy = (float)Math.Sin(a) * cb;
        float dz = (float)Math.Sin(b) * -m;

        if (rot != null)
        {
            // FUN_100dcd23: row vector times the locator's 3x3 (rows at +0, +0x10, +0x20).
            float rx = dx * rot[0] + dy * rot[3] + dz * rot[6];
            float ry = dx * rot[1] + dy * rot[4] + dz * rot[7];
            float rz = dx * rot[2] + dy * rot[5] + dz * rot[8];
            dx = rx;
            dy = ry;
            dz = rz;
        }

        float s1 = (_flags & FlagSwapEnds) != 0 ? _scale33 : _scale32;
        float s2 = (_flags & FlagSwapEnds) != 0 ? _scale32 : _scale33;

        float life = (_life1 - _life0) * _rand01() + _life0;
        float inv = 1f / life;

        // A sprite that would not outlive two frames starts at its end points instead.
        float jump = life <= dt + dt ? 1f : 0f;
        float k = inv * (1f - jump);

        _visual.NewSprite(new FlareType0Visual.Sprite
        {
            P1x = ox + dx * s1 * jump,
            P1y = oy + dy * s1 * jump,
            P1z = oz + dz * s1 * jump,
            P2x = ox + dx * s2 * jump,
            P2y = oy + dy * s2 * jump,
            P2z = oz + dz * s2 * jump,
            V1x = dx * s1 * k,
            V1y = dy * s1 * k,
            V1z = dz * s1 * k,
            V2x = dx * s2 * k,
            V2y = dy * s2 * k,
            V2z = dz * s2 * k,
            Size0 = _size0,
            Size1 = _size1,
            Size0Rate = (_size0End - _size0) * inv,
            Size1Rate = inv * (_size1End - _size1),
            Life = life,
            Argb = 0xffffffffu,
            A = _start[0],
            R = _start[1],
            G = _start[2],
            B = _start[3],
            ARate = inv * (_end[0] - _start[0]),
            RRate = inv * (_end[1] - _start[1]),
            GRate = inv * (_end[2] - _start[2]),
            BRate = (_end[3] - _start[3]) * inv,
            Frame = _firstFrame,
            FrameRate = inv * (_lastFrame - _firstFrame),
            Active = true,
        });
    }

    static float F(float[] f, int i) => f != null && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i < f.Length ? BitConverter.SingleToInt32Bits(f[i]) : 0;
}
