using System;

/// <summary>
/// Stock <c>_GfxControlTracer6_t</c> (typeCode 1026 / 0x402, vftable <c>Gamecode 1016e104</c>, object
/// 0xd0): Tracer5's bigger cousin, and the last tracer the port drew with a stand-in. It is reached only
/// through <c>CreateGfxControlTracer(id, from, to)</c>, whose dispatch at <c>100d18d9</c> reads
/// <c>sub ecx, 0x401; je Tracer5; dec; je Tracer6; dec; je Tracer7; sub ecx, 0x7cf; jne fail</c> — so
/// 0x401, 0x402, 0x403 and 0xbd2 are Tracer5, 6, 7 and 8.
///
/// It draws two things along the flight line at once: a trail of sprites that widens as it goes, and
/// three ribbons that race ahead of it and arrive with it.
///
/// Fields (loader <c>10100c93</c>, the same shape as Tracer5's plus 17-26): 0 flags, 1-7 locator,
/// 8 duration, 9 material, 10 speed, 11 the spacing between sprites, 12 the sprite's base size,
/// 13-16 colour A (the sprites), 17 how much the sprite grows with distance along the line, 18 how fast
/// it grows with time, 19 the link count, 20 the ribbon's speed, 21 its length, 22 its width,
/// 23-26 colour B (the ribbons). Both colours are FISTP-packed once, as Tracer5 packs its one.
///
/// Init (<c>101014ff</c>) matches Tracer5's call for call: direction and length (<c>10023bbf</c> /
/// <c>10023bfd</c>), a segment under 0.01 readies the control, the speed is capped at 5 × the length so
/// the flight lasts at least 0.2 s, and the locator frame is the same one (rows cross(dir, p), dir, p,
/// start). It adds one cap of its own: <b>if field 11 × 4 is past the length, field 11 becomes
/// 0.24 × the length</b>, so a short flight still gets about four sprites.
///
/// Process (<c>10101872</c> → <c>1010107d</c>) runs in two phases, and the record's duration of -1 is
/// what selects between them — nothing expires it until the control sets its own:
/// <list type="bullet">
/// <item><b>The flight, while age &gt; duration.</b> The sprite trail runs from
/// tail = speed · age (kept at 0 or more) to head = tail + field 11 (capped at the length), and a sprite
/// is spawned every field 11 along it from the cursor up to the head. The ribbons run from
/// a = field 20 · max(0, age − (length / speed − length / field 20)) to b = a + field 21 — which is
/// tuned so a reaches the length exactly when the sprite tail does. When the tail reaches the length,
/// <b>duration = age + 1</b>.</item>
/// <item><b>The last second, while age &lt; duration.</b> The ribbons collapse to nothing and their
/// links are given colour 0 (<c>10101463</c>), so only the sprites are left, fading and growing out.
/// The base timer ends the control when age passes the duration.</item>
/// </list>
/// Every sprite, each call: its width and height grow by field 18 · dt, its position moves by its
/// velocity (which is always zero — stock spawns it at rest and never writes it), its life falls by dt
/// and is kept at 0 or more, its alpha becomes <c>ftol(life · 255)</c> and its frame
/// <c>ftol((1 − life) · 30 + 33)</c>, so it walks cells 33 to 63 of an 8 × 8 atlas as it dies.
///
/// Nothing here depends on Unity.
/// </summary>
public sealed class Tracer6Sim
{
    /// <summary>The build (<c>10100de0</c>) makes three GfxVisualCord4 visuals, all drawn alike.</summary>
    public const int RibbonCount = 3;

    /// <summary>Stock walks first + next + next of each ribbon's link list.</summary>
    public const int LinksPerRibbon = 3;

    /// <summary>Link lives, newest first — the list prepends, so the last built (1) comes out first.</summary>
    public static readonly float[] LinkLives = { 1f, 0.001f, 0.001f };

    public const float MinDistance = 0.01f;

    /// <summary>The second the control buys itself when the flight lands (<c>101010e9</c>).</summary>
    public const float StopSeconds = 1f;

    /// <summary><c>+0x184</c> / <c>+0x188</c>, both set to 8 by the build (<c>10100fae</c>).</summary>
    public const int AtlasCols = 8;
    public const int AtlasRows = 8;

    /// <summary>The frame a sprite is spawned on (<c>1010142d</c>), before its first update moves it.</summary>
    public const int SpawnFrame = 0x20;

    /// <summary>frame = ftol((1 - life) * 30 + 33) (<c>1010118e</c>): cells 33 to 63.</summary>
    public const float FrameSpan = 30f;
    public const float FrameBase = 33f;

    /// <summary>The ribbons' material is the literal 15 in the build (<c>10100e01</c>), not field 9.</summary>
    public const int RibbonMaterialIndex = 15;

    /// <summary>
    /// Stock's sprite pool is whatever <c>GfxVisualSprite3Type0</c>'s ctor makes; the build never calls
    /// an Init(count) on it, so the size is not recovered. This is large enough that no real record
    /// reaches it: the trail holds about length / field 11 sprites at once, and field 11 is at least
    /// 0.24 × the length once the init's cap has run.
    /// </summary>
    public const int MaxSprites = 512;

    /// <summary>One entry of the sprite visual's list, in the visual's frame.</summary>
    public struct Sprite
    {
        public float X, Y, Z;
        /// <summary>+0x30 and +0x34, both grown by field 18 · dt.</summary>
        public float Width, Height;
        /// <summary>+0x3c, 1 at birth and down by dt a second.</summary>
        public float Life;
        /// <summary>+0x40, the atlas cell.</summary>
        public int Frame;
        public bool Alive;
    }

    readonly float[] _basis = new float[9];
    readonly bool _local;
    readonly float[] _links = new float[LinksPerRibbon * 3];
    readonly Sprite[] _sprites = new Sprite[MaxSprites];

    // Loaded fields.
    readonly float _spacing;     // 11
    readonly float _spriteSize;  // 12
    readonly float _sizeGain;    // 17
    readonly float _growth;      // 18
    readonly float _ribbonSpeed; // 20
    readonly float _ribbonLength;// 21

    public bool Degenerate { get; }
    public float Distance { get; }
    public float Speed { get; }
    public float Spacing => _spacing;
    public float RibbonWidth { get; }
    public int LinkCount { get; }
    public uint SpriteArgb { get; }
    public uint RibbonArgb { get; }
    public float OriginX { get; }
    public float OriginY { get; }
    public float OriginZ { get; }
    public bool Local => _local;

    /// <summary>Stock <c>+0x10</c>: -1 on every record, until the flight lands and sets it.</summary>
    public float Duration { get; private set; }

    /// <summary>Stock <c>+0xcc</c>: how far along the line the next sprite goes.</summary>
    public float Cursor { get; private set; }

    public float SpriteHead { get; private set; }
    public float SpriteTail { get; private set; }

    /// <summary>The ribbons' trailing and leading ends, stock's <c>[ebp-4]</c> and <c>[ebp-8]</c>.</summary>
    public float RibbonTail { get; private set; }
    public float RibbonHead { get; private set; }

    /// <summary>False in the last second, when stock zeroes the ribbon links' colour.</summary>
    public bool RibbonsVisible { get; private set; }

    public float[] Basis => _basis;
    public float[] LinkPositions => _links;
    public Sprite[] Sprites => _sprites;
    public int LiveSprites { get; private set; }

    public Tracer6Sim(float[] fields, float sx, float sy, float sz, float ex, float ey, float ez)
    {
        int flags = Int(fields, 0);
        _local = (flags & 2) != 0;
        Duration = F(fields, 8);
        float speed = F(fields, 10);
        _spacing = F(fields, 11);
        _spriteSize = F(fields, 12);
        SpriteArgb = PackRounded(F(fields, 13), F(fields, 14), F(fields, 15), F(fields, 16));
        _sizeGain = F(fields, 17);
        _growth = F(fields, 18);
        LinkCount = Int(fields, 19);
        _ribbonSpeed = F(fields, 20);
        _ribbonLength = F(fields, 21);
        RibbonWidth = F(fields, 22);
        RibbonArgb = PackRounded(F(fields, 23), F(fields, 24), F(fields, 25), F(fields, 26));

        float dx = ex - sx, dy = ey - sy, dz = ez - sz;
        float dist = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        Distance = dist;
        if (dist < MinDistance)
        {
            // 10101604: a segment this short is done before it starts.
            Degenerate = true;
            Speed = speed;
            return;
        }

        // 10101618: at most five lengths a second, as Tracer5.
        float cap = (float)(dist * 5.0);
        Speed = speed > cap ? cap : speed;

        // 1010162d: four spacings past the end pulls the spacing in to a quarter of the line.
        if ((float)(_spacing * 4.0) > dist)
            _spacing = (float)(dist * 0.23999999463558197);

        float inv = 1f / dist;
        dx *= inv;
        dy *= inv;
        dz *= inv;
        Tracer1Sim.FindPerpendicular(dx, dy, dz, out float px, out float py, out float pz);
        float tx = dy * pz - dz * py;
        float ty = dz * px - dx * pz;
        float tz = dx * py - dy * px;
        float kt = 1f / (float)Math.Sqrt(tx * tx + ty * ty + tz * tz);
        tx *= kt;
        ty *= kt;
        tz *= kt;

        _basis[0] = tx; _basis[1] = ty; _basis[2] = tz;
        _basis[3] = dx; _basis[4] = dy; _basis[5] = dz;
        _basis[6] = px; _basis[7] = py; _basis[8] = pz;
        Tracer1Sim.ApplyLocatorRotation(fields, flags, _basis);

        float ox = sx, oy = sy, oz = sz;
        Tracer1Sim.ApplyLocatorOffset(fields, _basis, ref ox, ref oy, ref oz);
        OriginX = ox;
        OriginY = oy;
        OriginZ = oz;
    }

    /// <summary>One stock Process body (<c>1010107d</c>) at <paramref name="age"/> over <paramref name="dt"/>.</summary>
    public void Step(float age, float dt)
    {
        if (Degenerate)
            return;

        // 10101086: the sprite trail's ends.
        float tail = (float)(Speed * (double)age);
        float head = (float)(_spacing + (double)tail);
        if (Distance < head)
            head = Distance;
        if (!(0f <= tail))
            tail = 0f;

        // 101010c8: once the trail has run the whole line, the control gives itself a last second.
        if (age > Duration && Distance <= tail)
            Duration = (float)(age + (double)StopSeconds);

        SpriteHead = head;
        SpriteTail = tail;

        StepSprites(dt);

        // 101011b5: the spawn only runs while the control has no duration to live up to.
        if (age > Duration && head > Cursor)
            Spawn(head);

        StepRibbons(age);
    }

    /// <summary>The per-sprite body (<c>10101103</c>).</summary>
    void StepSprites(float dt)
    {
        int live = 0;
        for (int i = 0; i < _sprites.Length; i++)
        {
            if (!_sprites[i].Alive)
                continue;

            ref Sprite s = ref _sprites[i];
            s.Width = (float)(_growth * (double)dt + s.Width);
            s.Height = (float)(_growth * (double)dt + s.Height);
            // The velocity is always zero, so the position never moves; stock still does the add.

            float life = (float)(s.Life - (double)dt);
            s.Life = life;
            if (!(0f <= life))
                s.Life = 0f;

            s.Frame = (int)((1.0 - s.Life) * FrameSpan + FrameBase);
            // A sprite is only retired when its list slot is reused; stock keeps drawing it at alpha 0.
            if (s.Life > 0f)
                live++;
        }
        LiveSprites = live;
    }

    /// <summary>The spawn loop (<c>1010137e</c>): one sprite every field 11 up to the head.</summary>
    void Spawn(float head)
    {
        // A zero or negative spacing would never advance the cursor; stock would hang, the port stops.
        if (!(_spacing > 0f))
            return;

        while (Cursor <= head)
        {
            int slot = FreeSlot();
            if (slot >= 0)
            {
                float size = (float)(Cursor * (double)_sizeGain + _spriteSize);
                _sprites[slot] = new Sprite
                {
                    X = 0f,
                    Y = Cursor,
                    Z = 0f,
                    Width = size,
                    Height = size,
                    Life = 1f,
                    Frame = SpawnFrame,
                    Alive = true,
                };
            }
            Cursor = (float)(_spacing + (double)Cursor);
        }
    }

    int FreeSlot()
    {
        for (int i = 0; i < _sprites.Length; i++)
            if (!_sprites[i].Alive || _sprites[i].Life <= 0f)
                return i;
        return -1;
    }

    /// <summary>The ribbon ends and their three links (<c>101011da</c>).</summary>
    void StepRibbons(float age)
    {
        // 101011df: the ribbon is timed so its tail reaches the end exactly when the trail's does.
        float t = (float)(age - (Distance / (double)Speed - Distance / (double)_ribbonSpeed));
        if (!(0f <= t))
            t = 0f;
        float a = (float)(_ribbonSpeed * (double)t);
        float b = (float)(_ribbonLength + (double)a);

        // 10101225: inside the last second both ends collapse to nothing.
        if (age < Duration)
        {
            a = 0f;
            b = 0f;
        }
        if (!(0f < t))
        {
            a = 0f;
            b = 0f;
        }
        if (Distance < b)
            b = Distance;
        if (!(0f <= a))
            a = 0f;

        // 10101272: when the ribbon's tail lands, it too buys the last second.
        if (age > Duration && Distance <= a)
        {
            Duration = (float)(age + (double)StopSeconds);
            a = Distance;
        }

        RibbonTail = a;
        RibbonHead = b;
        // 10101463: the links lose their colour for the last second.
        RibbonsVisible = !(age < Duration);

        // FUN_1010640a: zero in local mode, the locator position otherwise.
        float gx = _local ? 0f : OriginX;
        float gy = _local ? 0f : OriginY;
        float gz = _local ? 0f : OriginZ;
        Set(0, gx, gy + b, gz);
        Set(1, gx, gy + a, gz);
        Set(2, gx, gy, gz);
    }

    void Set(int i, float x, float y, float z)
    {
        _links[i * 3] = x;
        _links[i * 3 + 1] = y;
        _links[i * 3 + 2] = z;
    }

    /// <summary>The atlas cell of <paramref name="frame"/> in an 8 × 8 sheet (<c>10028c68</c>).</summary>
    public static void AtlasCell(int frame, out int col, out int row)
    {
        col = frame % AtlasCols;
        row = frame / AtlasCols;
    }

    /// <summary>Per channel FISTP(c * 255 - 0.49999), packed A,R,G,B (<c>10101516</c>).</summary>
    static uint PackRounded(float a, float r, float g, float b)
    {
        int ia = Fistp((float)(a * 255.0) - 0.49999);
        int ir = Fistp((float)(r * 255.0) - 0.49999);
        int ig = Fistp((float)(g * 255.0) - 0.49999);
        int ib = Fistp((float)(b * 255.0) - 0.49999);
        return unchecked((uint)(((ia << 8 | ir) << 8 | ig) << 8 | ib));
    }

    static int Fistp(double v) => (int)Math.Round(v, MidpointRounding.ToEven);

    static float F(float[] f, int i) => f != null && i >= 0 && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i >= 0 && i < f.Length ? GfxBits.Of(f, i) : 0;
}
