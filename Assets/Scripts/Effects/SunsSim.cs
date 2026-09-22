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
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class SunsSim
{
    public const int SparkLineType = 4;
    public const int SlotCount = 32;

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

    static float F(float[] f, int i) => f != null && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i < f.Length ? BitConverter.SingleToInt32Bits(f[i]) : 0;
}
