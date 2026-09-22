using System;

/// <summary>
/// Stock <c>_GfxControlStars_t</c> starType 3 — the orbiting sparkle swarm (43168, the child of cast
/// effect 47299). One <see cref="Step"/> is one call of the stock Process, <c>FUN_100f8491</c>, whose
/// case 3 starts at <c>Gamecode 100f89dd</c>. Stock takes no delta anywhere in this case: the spring,
/// the spawn allowance and the slot recycling are all per call, so the swarm's density and speed are
/// set by how often Process runs. The caller decides that rate (see <see cref="GfxControlStars"/>).
///
/// Fields read by stock (ctor loader <c>FUN_100f72a9</c>, lazy init <c>FUN_100f7fb8</c>):
///   field 18-21 start A,R,G,B and 22-25 end A,R,G,B  → colour ramp at +0x1698 (<c>FUN_101085de</c>)
///   field 28 size curve  (+0x16d4)
///   field 29 X/Z spawn radius  (+0x16d8)
///   field 30 particle life in ms, as int bits  (+0x16dc; +0x16cc = field30 / 1000)
/// Duration (field 26), termination and the locator live in the control, not here.
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class StarsCase3
{
    /// <summary>Slot count. The ctor (<c>FUN_100f74ad</c>) sizes every per-particle array for 0x80.</summary>
    public const int SlotCount = 128;

    /// <summary>Slots a single Process may open: <c>100f8a06 MOV [EBP-0x20], 2</c>.</summary>
    public const int SpawnsPerStep = 2;

    /// <summary>Spring constant, both uses: <c>100f8a3b</c> / <c>100f8a79 FLD [0x10161850]</c> = 0.1.</summary>
    public const float Pull = 0.1f;

    /// <summary>Frame index range: <c>100f8abe FMUL 16.0</c> then clamped to 0..15.</summary>
    public const int FrameCount = 16;

    /// <summary>Timer seed from the lazy init (<c>0xc2c80000</c>), so every slot is due at once.</summary>
    const float UnspawnedTimer = -100f;

    /// <summary>One stock DiaBill sprite record (<c>Sprite_t</c>, 0x20 bytes at +0x44).</summary>
    public struct Sprite
    {
        public float X, Y, Z;
        /// <summary>Stock writes the same value to width (+0xc) and height (+0x10).</summary>
        public float Size;
        /// <summary>D3DCOLOR, 0xAARRGGBB, as packed by <c>FUN_10108663</c>.</summary>
        public uint Argb;
        public int Frame;
        public bool Visible;
        /// <summary>
        /// Port-only: which spawn last wrote this record (0 = none yet), so <see cref="Blend"/> can tell a
        /// particle that moved from a slot that was recycled. Stock has no such field.
        /// </summary>
        public int Serial;
    }

    readonly float _life;
    readonly float _sizeCurve;
    readonly float _radiusXZ;
    readonly float[] _start = new float[4];
    readonly float[] _delta = new float[4];
    readonly Func<int> _rand;

    // +0xc48 positions, +0x648 velocities, +0x1448 per-slot expiry timers.
    readonly float[] _px = new float[SlotCount], _py = new float[SlotCount], _pz = new float[SlotCount];
    readonly float[] _vx = new float[SlotCount], _vy = new float[SlotCount], _vz = new float[SlotCount];
    readonly float[] _timer = new float[SlotCount];
    readonly Sprite[] _sprites = new Sprite[SlotCount];

    // Port-only, for drawing between steps: the records as the previous step left them, and the serial
    // each slot's current particle writes into its record.
    readonly Sprite[] _previous = new Sprite[SlotCount];
    readonly int[] _slotSerial = new int[SlotCount];
    int _serial;

    /// <summary>Stock +0x1648. Set by TerminateGracefully or by the duration running out.</summary>
    public bool Terminating { get; set; }

    /// <summary>Slots spawned or alive on the last step. Stock's <c>[EBP-8]</c>.</summary>
    public int LastCount { get; private set; }

    /// <summary>Stock readies the control once terminating and a step saw nothing alive (<c>100f8c23</c>).</summary>
    public bool Drained => Terminating && LastCount == 0;

    public float Life => _life;
    public Sprite[] Sprites => _sprites;

    /// <param name="startArgb">Fields 18-21, in A,R,G,B order.</param>
    /// <param name="endArgb">Fields 22-25, in A,R,G,B order.</param>
    /// <param name="rand">Stock <c>rand()</c>: 0..0x7fff. <see cref="MsvcRand"/> reproduces the CRT.</param>
    public StarsCase3(
        float lifeSeconds,
        float sizeCurve,
        float radiusXZ,
        float[] startArgb,
        float[] endArgb,
        Func<int> rand)
    {
        _life = lifeSeconds;
        _sizeCurve = sizeCurve;
        _radiusXZ = radiusXZ;
        _rand = rand ?? throw new ArgumentNullException(nameof(rand));

        // FUN_10108663 computes the deltas once (end - start) and lerps start + t * delta.
        for (int c = 0; c < 4; c++)
        {
            _start[c] = startArgb[c];
            _delta[c] = endArgb[c] - startArgb[c];
        }

        // Lazy init FUN_100f7fb8: sprite records w=h=0, colour 0xffffffff, frame 0, hidden;
        // case 3 seeds every timer to -100.
        for (int i = 0; i < SlotCount; i++)
        {
            _timer[i] = UnspawnedTimer;
            _sprites[i].Argb = 0xffffffffu;
        }
    }

    /// <summary>
    /// One stock Process call. <paramref name="age"/> is the control's age after this call's delta was
    /// added (+0xc); the origin is the locator position (<c>FUN_1010640a</c>).
    /// </summary>
    public void Step(float age, float ox, float oy, float oz)
    {
        Array.Copy(_sprites, _previous, SlotCount);

        int budget = SpawnsPerStep;
        int count = 0;

        for (int i = 0; i < SlotCount; i++)
        {
            if (_timer[i] <= age)
            {
                if (budget == 0 || Terminating)
                {
                    _sprites[i].Visible = false;
                    continue;
                }

                // GetRandomPointInSphere, then X and Z (not Y) scaled by field 29 (100f8b8f / 100f8baa).
                RandomPointInUnitBall(_rand, out float x, out float y, out float z);
                x *= _radiusXZ;
                z *= _radiusXZ;

                _px[i] = ox + x;
                _py[i] = oy + y;
                _pz[i] = oz + z;

                // Horizontal perpendicular of the scaled offset: (z, 0, -x) (100f8bc4..100f8bed).
                _vx[i] = z;
                _vy[i] = 0f;
                _vz[i] = -x;

                _timer[i] = _life + age;
                _slotSerial[i] = ++_serial;
                count++;
                budget--;
                // The sprite record is not touched on the spawn call: it keeps its previous state.
                continue;
            }

            // vel += (origin - pos) * 0.1; pos += vel * 0.1 (100f8a36..100f8aae).
            _vx[i] += (ox - _px[i]) * Pull;
            _vy[i] += (oy - _py[i]) * Pull;
            _vz[i] += (oz - _pz[i]) * Pull;
            _px[i] += _vx[i] * Pull;
            _py[i] += _vy[i] * Pull;
            _pz[i] += _vz[i] * Pull;

            ref Sprite s = ref _sprites[i];
            s.X = _px[i];
            s.Y = _py[i];
            s.Z = _pz[i];
            s.Frame = FrameIndex(_timer[i], age, _life);

            float lifeFrac = LifeFraction(_timer[i], age, _life);
            s.Size = Size(lifeFrac, _sizeCurve);
            s.Argb = PackArgb(lifeFrac);
            s.Visible = true;
            s.Serial = _slotSerial[i];
            count++;
        }

        LastCount = count;
    }

    /// <summary>
    /// Port-only. Sprite <paramref name="i"/> drawn a fraction <paramref name="t"/> (0..1) of the way from
    /// the previous step to the last one, so a swarm stepped at the stock rate still moves smoothly at a
    /// higher frame rate. Position and size are interpolated; colour, frame and visibility are the last
    /// step's. A record written by a different particle on the previous step is not interpolated.
    /// </summary>
    public Sprite Blend(int i, float t) => Blend(_previous[i], _sprites[i], t);

    /// <summary><see cref="Blend(int, float)"/> for any stock case that keeps its records the same way.</summary>
    public static Sprite Blend(in Sprite previous, in Sprite current, float t)
    {
        Sprite s = current;
        Sprite p = previous;
        if (!p.Visible || p.Serial != s.Serial || s.Serial == 0)
            return s;

        s.X = p.X + (s.X - p.X) * t;
        s.Y = p.Y + (s.Y - p.Y) * t;
        s.Z = p.Z + (s.Z - p.Z) * t;
        s.Size = p.Size + (s.Size - p.Size) * t;
        return s;
    }

    /// <summary>
    /// <c>100f8ab9..100f8ae2</c>: 15 - _ftol((timer - age) * 16 / life), clamped to 0..15. _ftol truncates.
    /// </summary>
    public static int FrameIndex(float timer, float age, float life)
    {
        int n = (int)(((double)timer - age) * 16.0 / life);
        int frame = 15 - n;
        if (frame > 15)
            return 15;
        return frame < 0 ? 0 : frame;
    }

    /// <summary><c>100f8ae4..100f8b05</c>: ((life + age) - timer) / life.</summary>
    public static float LifeFraction(float timer, float age, float life)
        => (float)(((double)life + age - timer) / life);

    /// <summary>
    /// <c>100f8b08..100f8b24</c>: (t + 0.2) * sizeCurve * (1 - t*t). No clamp — it reaches 0 at t = 1.
    /// </summary>
    public static float Size(float lifeFrac, float sizeCurve)
    {
        double t = lifeFrac;
        return (float)((t + 0.2) * sizeCurve * (1.0 - t * t));
    }

    /// <summary>
    /// <c>FUN_10108663</c>: per channel _ftol((start + t * delta) * 255), packed A,R,G,B from the high
    /// byte down. No clamp; <paramref name="t"/> stays in 0..1 for live particles.
    /// </summary>
    public uint PackArgb(float t)
    {
        uint packed = 0;
        for (int c = 0; c < 4; c++)
        {
            float v = _start[c] + t * _delta[c];
            packed = (packed << 8) | ((uint)(int)(v * 255.0) & 0xffu);
        }
        return packed;
    }

    /// <summary>
    /// Stock <c>_GfxControl_t::GetRandomPointInSphere</c> (<c>100d316a</c>): each axis is
    /// (rand() &amp; 0x7fff) / 16384 - 1, redrawn until x² + y² + z² &lt; 1.
    /// </summary>
    public static void RandomPointInUnitBall(Func<int> rand, out float x, out float y, out float z)
    {
        const double Scale = 1.0 / 16384.0; // 100d3187 FMUL [0x1016c378]
        do
        {
            x = (float)((rand() & 0x7fff) * Scale - 1.0);
            y = (float)((rand() & 0x7fff) * Scale - 1.0);
            z = (float)((rand() & 0x7fff) * Scale - 1.0);
        }
        while ((float)(x * x + y * y + z * z) >= 1f);
    }

    /// <summary>The MSVC CRT <c>rand()</c> LCG the stock client links against.</summary>
    public sealed class MsvcRand
    {
        uint _state;

        public MsvcRand(uint seed) => _state = seed;

        public int Next()
        {
            _state = _state * 214013u + 2531011u;
            return (int)((_state >> 16) & 0x7fffu);
        }
    }
}
