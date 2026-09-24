using System;

/// <summary>
/// Stock <c>_GfxControlSuns_t</c> (typeCode 2005 / 0x7d5, vftable <c>Gamecode 1016de8c</c>), sunType 4: a
/// nano tracer of still sparks along its hit location, drawn by DisplaySystem <c>GfxVisualSol</c>
/// (17000, one half of nano 56213's tracer 17800). One <see cref="Step"/> is one call of the stock
/// Process, <c>100fc425</c>, whose type 4 body starts at <c>100fc48b</c>. It takes no delta: the spawn
/// allowance is per call.
///
/// Fields (loader around <c>100fd16a</c>): 0 flags, 9 material, 10 sunType, 11-17 (not read by type 4),
/// 18-25 colour ramp, 26 duration (field 8 is read, then overwritten), 27 (unused here), 28 spark
/// size, 29 frame scale, 30 spark life in ms (int bits), 31 (unused here). sunTypes above 4 get
/// duration 0.
///
/// Type 4, per call: at most 3 of 32 slots open. A slot looks the hit location up (the id at +0x30,
/// <c>100cdfd9</c>) and sits still at start * (1 - q) + end * q, q = (rand() &amp; 0x7fff) / 32768; it
/// lives field 30 / 1000 s. While alive, with t its life fraction: width = height = field 28, colour =
/// the fields 18-25 ramp at t, frame = 15 - _ftol(field 29 * t), screen angle = (slot % 7) * 0.3 and
/// no side offset. Terminating stops the spawns; a call that then finds nothing readies the control.
/// When the duration runs out it is simply ready: no drain.
///
/// Type 1 (<c>100fcbeb</c>, <see cref="StepRing"/>): a ring of 18 sprites round the locator's world-mode
/// position (<c>1010640a</c>), e.g. the stars circling a stunned head (43307). With q = age / duration,
/// x = q (1 - q with field 30 set; below 0.2 it is eased to 1 - (1 - x)(5x)^27), w = 1 - x before the
/// easing: alpha = _ftol((1 - x²) 255), radius s = 1 - w², sizes 0.9 and 1.1 × field 28 × s. With field 31
/// set both sizes gain field 28 (1 - x)^8 and the radius stays at 0.3 or more. Pair k (k = 0..8) sits at
/// angle θ = 2k · 6.28 / 18 + age · field 29, at locator + s (sin θ, 0.1 sin(4 age + 3θ), cos θ): the
/// first sprite turned by 2 age, the second by -1.5 age and shifted 0.05 s along the screen's x, each in
/// its colour of the <see cref="RingPalette"/> (index 2k and 2k + 1) under that alpha, frame 0.
/// Terminating readies it after that call (the tail at <c>100fcfae</c>).
///
/// Type 0 (<c>100fcea7</c>, <see cref="StepHalo"/>): 8 sprites stacked 2 above the locator's world-mode
/// position, e.g. the glow over a blessed character (43768). With q = age / duration and envelope
/// e = 1 - (2q - 1)⁴, sprite i is (field 28 + i · field 29) · e wide and tall, turned by (i - 4) · age / 2,
/// in <see cref="HaloPalette"/>[i] with its alpha cut to 0xc0, frame 0. Terminating readies it after that
/// call.
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class SunsSim
{
    public const int SparkLineType = 4;
    public const int RingType = 1;
    public const int HaloType = 0;

    /// <summary>Sprites the type 0 stack fills each call: <c>100fcfa3 CMP EAX, 0x140</c>.</summary>
    public const int HaloSprites = 8;

    /// <summary>The D3DCOLORs at <c>0x102c60a0</c>.</summary>
    public static readonly uint[] HaloPalette =
    {
        0xffffc0c0u, 0xffffff00u, 0xffff00ffu, 0xff00ffffu, 0xff0000ffu, 0xff00ff00u, 0xffff0000u, 0xffc0ffc0u,
    };
    public const int SlotCount = 32;

    /// <summary>Sprites the type 1 ring fills each call: <c>100fce97 CMP EAX, 0x2d0</c>, 0x28 bytes each.</summary>
    public const int RingSprites = 18;

    /// <summary>The D3DCOLORs at <c>0x102c60c0</c>.</summary>
    public static readonly uint[] RingPalette =
    {
        0xffffc000u, 0xff0040ffu, 0xffff0040u, 0xff00ffc0u, 0xffc000ffu, 0xff40ff00u, 0xffc00040u, 0xff40ffc0u,
    };

    /// <summary>Slots one call may open: <c>100fc49f MOV [EBP-0x1c], 3</c>.</summary>
    public const int SpawnsPerStep = 3;

    const float UnspawnedTimer = -100f;
    const double AlongScale = 3.0517578125e-05;
    const double AngleStep = 0.30000001192092896;

    /// <summary>One <c>Sol_n::Sprite_t</c> (0x28 bytes).</summary>
    public struct Sprite
    {
        public float X, Y, Z;
        public float Width, Height;
        /// <summary>+0x14: the quad's turn in the screen plane, radians.</summary>
        public float Angle;
        /// <summary>+0x18: a shift along the screen's x axis.</summary>
        public float Offset;
        public uint Argb;
        public int Frame;
        public bool Visible;
    }

    readonly float _life;
    readonly float _size;
    readonly float _frameScale;
    readonly bool _reverse;
    readonly bool _burst;
    readonly float[] _start = new float[4];
    readonly float[] _end = new float[4];
    readonly Func<int> _rand;

    // +0x33c positions, +0x53c timers.
    readonly float[] _px = new float[SlotCount], _py = new float[SlotCount], _pz = new float[SlotCount];
    readonly float[] _timer = new float[SlotCount];
    readonly Sprite[] _sprites = new Sprite[SlotCount];

    public int SunType { get; }

    /// <summary>Stock +0x10.</summary>
    public float Duration { get; set; }

    /// <summary>Stock +0x5bc.</summary>
    public bool Terminating { get; set; }

    public int LastCount { get; private set; }
    public bool Drained => Terminating && LastCount == 0;

    /// <summary>Types 0 and 1 depend on the age alone (their bodies keep no state between calls).</summary>
    public bool AgeDriven => SunType == RingType || SunType == HaloType;

    /// <summary>After a call, whether the control is ready: types 0 and 1 as soon as they terminate, type 4 once drained.</summary>
    public bool Done => AgeDriven ? Terminating : Drained;
    public float Life => _life;
    public Sprite[] Sprites => _sprites;

    /// <param name="rand">Stock <c>rand()</c>: 0..0x7fff.</param>
    public SunsSim(float[] fields, Func<int> rand)
    {
        _rand = rand ?? throw new ArgumentNullException(nameof(rand));
        SunType = Int(fields, 10);
        Duration = F(fields, 26);
        _size = F(fields, 28);
        _frameScale = F(fields, 29);
        _life = (float)(Int(fields, 30) / 1000.0);
        // Type 1 reads fields 30 and 31 (+0x634 / +0x638) as flags.
        _reverse = Int(fields, 30) != 0;
        _burst = Int(fields, 31) != 0;
        for (int c = 0; c < 4; c++)
        {
            _start[c] = F(fields, 18 + c);
            _end[c] = F(fields, 22 + c);
        }

        if (SunType > 4)
        {
            Duration = 0f;
            return;
        }

        for (int i = 0; i < SlotCount; i++)
            _timer[i] = UnspawnedTimer;
    }

    /// <summary>Stock slot 11 (<c>100fd036</c>).</summary>
    public void SetStartColor(float a, float r, float g, float b)
    {
        _start[0] = a; _start[1] = r; _start[2] = g; _start[3] = b;
    }

    /// <summary>Stock slot 12 (<c>100fd069</c>).</summary>
    public void SetStopColor(float a, float r, float g, float b)
    {
        _end[0] = a; _end[1] = r; _end[2] = g; _end[3] = b;
    }

    /// <summary>
    /// The type 4 body of one call at <paramref name="age"/>. <paramref name="hasHitLocation"/> false
    /// means the lookup failed; start and end are then ignored.
    /// </summary>
    public void Step(
        float age,
        bool hasHitLocation,
        float sx, float sy, float sz,
        float ex, float ey, float ez)
    {
        int budget = SpawnsPerStep;
        int count = 0;

        for (int i = 0; i < SlotCount; i++)
        {
            if (age < _timer[i])
            {
                ref Sprite s = ref _sprites[i];
                s.X = _px[i];
                s.Y = _py[i];
                s.Z = _pz[i];
                float t = (float)(((double)age + _life - _timer[i]) / _life);
                s.Width = _size;
                s.Height = _size;
                s.Argb = StockColorRamp.Eval(_start, _end, t);
                s.Frame = 15 - (int)((double)_frameScale * t);
                s.Offset = 0f;
                s.Angle = (float)((i % 7) * AngleStep);
                s.Visible = true;
                count++;
                continue;
            }

            if (budget == 0 || Terminating || !hasHitLocation)
            {
                _sprites[i].Visible = false;
                continue;
            }

            float q = (float)((_rand() & 0x7fff) * AlongScale);
            float back = (float)(1.0 - q);
            _px[i] = ex * q + sx * back;
            _py[i] = ey * q + sy * back;
            _pz[i] = ez * q + sz * back;
            _timer[i] = (float)((double)_life + age);
            count++;
            budget--;
        }

        LastCount = count;
    }

    /// <summary>
    /// The type 1 body of one call at <paramref name="age"/>, round the locator's world-mode position.
    /// Terminating doesn't change it: the caller readies the control after the call.
    /// </summary>
    public void StepRing(float age, float ox, float oy, float oz)
    {
        double q = (double)age / Duration;
        float x = (float)(_reverse ? 1.0 - q : q);
        double w = 1.0 - x;
        float wStored = (float)w;
        if (_reverse && x < 0.2f)
        {
            float y = (float)(x * 5.0);
            y = (float)((double)y * y * y);
            y = (float)((double)y * y * y);
            float y27 = (float)((double)y * y * y);
            x = (float)(1.0 - w * y27);
        }

        int alpha = (int)((1.0 - (double)x * x) * 255.0);
        uint mask = unchecked((uint)(alpha << 24)) | 0xffffffu;
        float s = (float)(1.0 - (double)wStored * wStored);

        double size = _size * (double)s;
        float sizeA = (float)(0.8999999761581421 * size);
        float sizeB = (float)(size * 1.100000023841858);
        if (_burst)
        {
            float z = (float)(1.0 - x);
            z = (float)((double)z * z);
            z = (float)((double)z * z);
            z = (float)((double)z * z);
            double extra = _size * (double)z;
            sizeA = (float)(sizeA + extra);
            sizeB = (float)(extra + sizeB);
            if (s < 0.30000001192092896f)
                s = 0.30000001192092896f;
        }

        float offset = (float)(s * 0.05000000074505806);
        float spinA = (float)(age + (double)age);
        float spinB = (float)(age * -1.5);
        for (int k = 0; k < RingSprites / 2; k++)
        {
            int j = 2 * k;
            float theta = (float)(j * 6.28000020980835 * 0.05555550009012222 + age * (double)_frameScale);
            float wobble = (float)((float)Math.Sin((float)(age * 4.0 + theta * 3.0)) * 0.10000000149011612);
            float px = (float)((float)Math.Sin(theta) * s) + ox;
            float py = (float)(wobble * s) + oy;
            float pz = (float)((float)Math.Cos(theta) * s) + oz;

            ref Sprite a = ref _sprites[j];
            a.X = px; a.Y = py; a.Z = pz;
            a.Width = sizeA; a.Height = sizeA;
            a.Argb = RingPalette[j & 7] & mask;
            a.Frame = 0;
            a.Offset = 0f;
            a.Angle = spinA;
            a.Visible = true;

            ref Sprite b = ref _sprites[j + 1];
            b.X = px; b.Y = py; b.Z = pz;
            b.Width = sizeB; b.Height = sizeB;
            b.Argb = RingPalette[(j + 1) & 7] & mask;
            b.Frame = 0;
            b.Offset = offset;
            b.Angle = spinB;
            b.Visible = true;
        }

        LastCount = RingSprites;
    }

    /// <summary>
    /// The type 0 body of one call at <paramref name="age"/>, over the locator's world-mode position.
    /// Terminating doesn't change it: the caller readies the control after the call.
    /// </summary>
    public void StepHalo(float age, float ox, float oy, float oz)
    {
        float q = age / Duration;
        float v = (float)(q * 2.0 - 1.0);
        v = (float)((double)v * v);
        float envelope = (float)(1.0 - (double)v * v);
        float half = (float)(age * 0.5);

        for (int i = 0; i < HaloSprites; i++)
        {
            float fi = i;
            float size = (float)((fi * (double)_frameScale + _size) * envelope);
            ref Sprite s = ref _sprites[i];
            s.X = ox;
            s.Y = 2f + oy;
            s.Z = oz;
            s.Width = size;
            s.Height = size;
            s.Argb = HaloPalette[i & 7] & 0xc0ffffffu;
            s.Frame = 0;
            s.Offset = 0f;
            s.Angle = (float)((fi - 4.0) * half);
            s.Visible = true;
        }

        LastCount = HaloSprites;
    }

    static float F(float[] f, int i) => f != null && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i < f.Length ? GfxBits.Of(f, i) : 0;
}
