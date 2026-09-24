using System;

/// <summary>
/// Stock <c>_GfxControlStars_t</c> starType 13 (<c>Gamecode 100f9c3b</c>..<c>100f9d9e</c>): a trail of 128
/// sparks strung along a wandering heading, reaching out from the locator and back over the duration, e.g.
/// 43040-43046 (the hits of 204594 Weeping Flesh and its kin) and 43734-43737.
///
/// Fields (loader <c>FUN_100f72a9</c>): 11 spark size (+0x167c), 18-21 / 22-25 colour ramp, 26 duration,
/// 28 reach (+0x16d4), 29 turn per step in radians (+0x16d8).
///
/// The heading (<c>100f7ec9</c>): an axis A, starting (0, 0, 1), takes 0.15 of a random unit vector
/// (<c>100d3005</c>) and is set back to length 1; the heading D, starting (0, 1, 0), is turned about A by
/// field 29 and set to length 1. Stock's quaternion product (<c>1007c5e5</c>) is the Hamilton product with
/// its operands swapped, so its Q · D · Q⁻¹ is Hamilton's Q⁻¹ · D · Q: a turn of -field 29 about A.
///
/// The lazy init (<c>100f8316</c>) fills a ring of 128 headings with 128 steps (head = 128). Per call:
/// <list type="bullet">
/// <item>p = age / duration, env = 1 - (2p - 1)², which rises 0 → 1 → 0.</item>
/// <item>Seven steps: head = (head + 1) % 128, ring[head] = the new heading.</item>
/// <item>Slot i: f = ((head - i + 128) % 128) / 128, its heading's age. Shown while f &lt; env, at
/// locator + ring[i] · field 28 · f, size field 11, the ramp at f; the frame stays 0.</item>
/// </list>
/// On expiry it is simply ready (its <c>0x100fc374</c> entry is 1); terminating readies it after its next
/// call (the tail at <c>100f8622</c>).
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class StarsCase13 : IStarsStockCase
{
    public const int SlotCount = StarsCase3.SlotCount;

    public const int StepsPerCall = 7;

    /// <summary>10168e80: how much of a random unit vector the axis takes each step.</summary>
    public const float Wander = 0.15000000596046448f;

    // 1016de00.
    const double SlotFraction = 0.0078125;

    readonly float _size;
    readonly float _reach;
    readonly float _turn;
    readonly float[] _start;
    readonly float[] _end;
    readonly Func<int> _rand;

    // +0x1654 heading, +0x1660 axis, +0x48 the ring, +0x166c its head.
    float _dx, _dy = 1f, _dz;
    float _ax, _ay, _az = 1f;
    readonly float[] _rx = new float[SlotCount], _ry = new float[SlotCount], _rz = new float[SlotCount];
    int _head;
    // Port-only: bumped when a ring entry is rewritten, so Blend doesn't draw the jump.
    readonly int[] _slotSerial = new int[SlotCount];

    readonly StarsCase3.Sprite[] _sprites = new StarsCase3.Sprite[SlotCount];
    readonly StarsCase3.Sprite[] _previous = new StarsCase3.Sprite[SlotCount];

    public bool Initialised { get; private set; }
    public bool Terminating { get; set; }

    /// <summary>The tail (<c>100f8622</c>) readies a terminating case 13 after its next call.</summary>
    public bool Drained => Terminating;

    public StarsCase3.Sprite[] Sprites => _sprites;
    public int Head => _head;

    /// <param name="size">Field 11.</param>
    /// <param name="reach">Field 28.</param>
    /// <param name="turn">Field 29, radians per step.</param>
    public StarsCase13(float size, float reach, float turn, float[] startArgb, float[] endArgb, Func<int> rand)
    {
        _size = size;
        _reach = reach;
        _turn = turn;
        _start = startArgb;
        _end = endArgb;
        _rand = rand ?? throw new ArgumentNullException(nameof(rand));
    }

    /// <summary>The heading after the last step.</summary>
    public void Heading(out float x, out float y, out float z)
    {
        x = _dx;
        y = _dy;
        z = _dz;
    }

    /// <summary>One stock Process call; <paramref name="duration"/> is the control's (+0x10).</summary>
    public void Step(float age, float duration, float ox, float oy, float oz)
    {
        Array.Copy(_sprites, _previous, SlotCount);

        if (!Initialised)
        {
            // 100f8316: 100f73cf resets the heading and axis, then 128 steps fill the ring in order.
            Initialised = true;
            _dx = 0f; _dy = 1f; _dz = 0f;
            _ax = 0f; _ay = 0f; _az = 1f;
            for (int i = 0; i < SlotCount; i++)
            {
                Turn();
                _rx[i] = _dx; _ry[i] = _dy; _rz[i] = _dz;
            }
            _head = SlotCount;
        }

        float p = (float)(age / (double)duration);
        float q = (float)((double)p + p - 1.0);
        float envelope = (float)(1.0 - (double)q * q);

        for (int k = 0; k < StepsPerCall; k++)
        {
            Turn();
            _head = (_head + 1) % SlotCount;
            _rx[_head] = _dx; _ry[_head] = _dy; _rz[_head] = _dz;
            _slotSerial[_head]++;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            ref StarsCase3.Sprite s = ref _sprites[i];
            float f = (float)(((_head - i + SlotCount) % SlotCount) * SlotFraction);
            if (!(f < envelope))
            {
                s.Visible = false;
                continue;
            }

            float scale = (float)(_reach * (double)f);
            s.X = (float)(ox + (double)(float)(_rx[i] * (double)scale));
            s.Y = (float)(oy + (double)(float)(_ry[i] * (double)scale));
            s.Z = (float)(oz + (double)(float)(_rz[i] * (double)scale));
            s.Size = _size;
            s.Argb = StockColorRamp.Eval(_start, _end, f);
            s.Visible = true;
            s.Serial = _slotSerial[i] + 1;
        }
    }

    /// <summary>100f7ec9: wander the axis, then turn the heading about it by field 29.</summary>
    void Turn()
    {
        ElectraSim.RandomUnitVector(_rand, out float ux, out float uy, out float uz);
        _ax = (float)((float)(ux * (double)Wander) + (double)_ax);
        _ay = (float)((float)(uy * (double)Wander) + (double)_ay);
        _az = (float)((float)(uz * (double)Wander) + (double)_az);
        Normalise(ref _ax, ref _ay, ref _az);

        // 100520d9: Q = (A sin h, cos h), h = turn / 2 for a turn in [0, 2pi), else frac(turn / 2pi) · pi.
        float h;
        if (_turn >= 0f && _turn < 6.283185307179586)
            h = (float)(_turn * 0.5);
        else
        {
            float a = (float)(_turn / 6.2831854820251465);
            h = (float)((a - Math.Floor(a)) * 3.1415927410125732);
        }
        float sin = (float)Math.Sin(h), cos = (float)Math.Cos(h);
        float qx = (float)(_ax * (double)sin), qy = (float)(_ay * (double)sin), qz = (float)(_az * (double)sin), qw = cos;

        // 100daa20: Q⁻¹ = conj(Q) / |Q|².
        float n = (float)((double)qy * qy + (double)qx * qx + (double)qz * qz + (double)qw * qw);
        float r = (float)(1.0 / n);
        float ix = (float)(-qx * (double)r), iy = (float)(-qy * (double)r), iz = (float)(-qz * (double)r), iw = (float)(qw * (double)r);

        // Stock Q * V then * Q⁻¹, each stock product a * b being Hamilton b · a.
        StockProduct(qx, qy, qz, qw, _dx, _dy, _dz, 0f, out float tx, out float ty, out float tz, out float tw);
        StockProduct(tx, ty, tz, tw, ix, iy, iz, iw, out _dx, out _dy, out _dz, out _);
        Normalise(ref _dx, ref _dy, ref _dz);
    }

    /// <summary>1007c5e5, this = a, argument = b.</summary>
    static void StockProduct(float ax, float ay, float az, float aw, float bx, float by, float bz, float bw,
        out float x, out float y, out float z, out float w)
    {
        x = (float)((double)aw * bx + (double)by * az - (double)bz * ay + (double)ax * bw);
        y = (float)((double)aw * by - (double)bx * az + (double)bz * ax + (double)ay * bw);
        z = (float)((double)ay * bx - (double)ax * by + (double)bz * aw + (double)bw * az);
        w = (float)((double)aw * bw - ((double)ay * by + (double)bx * ax + (double)bz * az));
    }

    /// <summary>100439aa(1): times 1 / |v| (<c>1003e0fd</c>).</summary>
    static void Normalise(ref float x, ref float y, ref float z)
    {
        float length = (float)Math.Sqrt((float)((double)y * y + (double)x * x + (double)z * z));
        float s = (float)(1.0 / length);
        x = (float)(x * (double)s);
        y = (float)(y * (double)s);
        z = (float)(z * (double)s);
    }

    /// <summary>Port-only; see <see cref="StarsCase3.Blend"/>.</summary>
    public StarsCase3.Sprite Blend(int i, float t) => StarsCase3.Blend(_previous[i], _sprites[i], t);
}
