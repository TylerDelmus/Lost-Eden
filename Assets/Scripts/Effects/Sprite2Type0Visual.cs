using System;

/// <summary>
/// The sprite pool of stock DisplaySystem <c>GfxVisualSprite2Type0</c> (ctor <c>1002646b</c>, <c>Init
/// 1002667b</c>, <c>InitSpriteDefault 10025844</c>, <c>NewSprite 100261de</c> / <c>10025be0</c>,
/// <c>ProcessSprites 100267c1</c>, quad <c>10025391</c>), shared by the controls that draw through it:
/// Sparks (1018, <see cref="SparksSim"/>), Fire (1004, <see cref="FireSim"/>) and Smoke (1009,
/// <see cref="SmokeSim"/>).
///
/// A sprite (0x78 bytes) carries its position, velocity, width and height with their rates, life, colour
/// with its rates, frame with its rate, and its wind mode, band and scale. ProcessSprites(dt, wind):
/// life -= dt, and a sprite below 0 dies. Otherwise, with a wind mode and h = (y - bottom) / (top -
/// bottom) clamped to 0..1, the sprite drifts by wind * f * its wind scale * dt: f = 1 (mode 1), h (2),
/// h² (3), (h² + h) / 2 (4), 1 with y += 1.5 dt (5), h with a random walk x, y += (r - 0.5) dt (6), 0 (8
/// and up). Mode 7 has no drift: the velocity is set to the sprite's direction times its band top (a
/// speed there) before the move, and the direction becomes the new velocity at length 1 after gravity, so
/// the sprite flies at a constant speed along a path that bends. Then p += v dt, v.y += gravity dt (visual
/// +0x22c: 0 from the ctor, and a mode-7 NewSprite sets it to that sprite's band bottom), colour, frame
/// and size += their rates * dt. The colour is packed per channel as FISTP(c * 255 - 0.49999).
///
/// NewSprite also draws the direction (+0x18) from the visual's random source: (2r - 1, 0.89r + 0.1,
/// 2r - 1), always, whatever the mode.
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class Sprite2Type0Visual
{
    public struct Sprite
    {
        public float X, Y, Z;
        public float VX, VY, VZ;
        public float Width, Height, DWidth, DHeight;
        public float Life;
        public float A, R, G, B, DA, DR, DG, DB;
        public float Frame, FrameRate;
        public int WindMode;
        public float WindLow, WindHigh;
        public float WindScale;
        /// <summary>+0x18: drawn by NewSprite; mode 7 steers by it.</summary>
        public float DirX, DirY, DirZ;
        public bool Alive;
        public uint Argb;
    }

    /// <summary>The wind mode that steers instead of drifting.</summary>
    public const int SteeredMode = 7;

    readonly Sprite[] _pool;
    readonly Func<float> _rand01;

    public Sprite[] Sprites => _pool;

    /// <summary>Visual +0x228: sprites alive after the last <see cref="ProcessSprites"/>.</summary>
    public int LiveCount { get; private set; }

    /// <summary>Visual +0x22c: y acceleration per second². 0 from the ctor; Sparks sets its field 40.</summary>
    public float Gravity { get; set; }

    /// <summary><c>Init(count)</c>: <paramref name="count"/> slots, all dead.</summary>
    /// <param name="rand01">The random walk of wind mode 6: uniform in [0, 1).</param>
    public Sprite2Type0Visual(int count, Func<float> rand01)
    {
        _rand01 = rand01 ?? throw new ArgumentNullException(nameof(rand01));
        _pool = new Sprite[Math.Max(0, Math.Min(count, 4096))];
    }

    /// <summary>
    /// <c>NewSprite</c>: the first dead slot takes the sprite and a fresh direction, or nothing when the
    /// pool is full. A mode-7 sprite sets the visual's gravity to its band bottom either way.
    /// </summary>
    public bool NewSprite(in Sprite sprite)
    {
        bool placed = false;
        for (int i = 0; i < _pool.Length; i++)
        {
            if (_pool[i].Alive)
                continue;
            _pool[i] = sprite;
            _pool[i].Alive = true;
            // 10026231: the visual's own random source (100844df).
            _pool[i].DirX = (float)(_rand01() * 2.0 - 1.0);
            _pool[i].DirY = (float)(_rand01() * 0.89 + 0.1);
            _pool[i].DirZ = (float)(_rand01() * 2.0 - 1.0);
            placed = true;
            break;
        }

        // 100263af.
        if (sprite.WindMode == SteeredMode)
            Gravity = sprite.WindLow;
        return placed;
    }

    public void ProcessSprites(float dt, float windX, float windY, float windZ)
    {
        int live = 0;
        for (int i = 0; i < _pool.Length; i++)
        {
            ref Sprite p = ref _pool[i];
            if (!p.Alive)
                continue;

            p.Life -= dt;
            if (!(0f <= p.Life))
            {
                p.Alive = false;
                continue;
            }

            bool steered = p.WindMode == SteeredMode;
            if (steered)
            {
                // 1002682b: the band top is the speed.
                p.VX = p.DirX * p.WindHigh;
                p.VY = p.DirY * p.WindHigh;
                p.VZ = p.DirZ * p.WindHigh;
            }
            else if (p.WindMode != 0)
            {
                Drift(ref p, dt, windX, windY, windZ);
            }

            p.X += p.VX * dt;
            p.Y += p.VY * dt;
            p.Z += p.VZ * dt;
            p.VY = Gravity * dt + p.VY;
            if (steered)
            {
                // 10026a03: the direction follows the velocity, at length 1 (10005229, 100048e8).
                float lengthSq = (float)((double)p.VX * p.VX + (double)p.VY * p.VY + (double)p.VZ * p.VZ);
                float inv = (float)(1.0 / (float)Math.Sqrt(lengthSq));
                p.DirX = p.VX * inv;
                p.DirY = p.VY * inv;
                p.DirZ = p.VZ * inv;
            }
            live++;

            p.A = p.DA * dt + p.A;
            p.R = p.DR * dt + p.R;
            p.G = p.DG * dt + p.G;
            p.B = p.DB * dt + p.B;
            p.Argb = Pack(p.A, p.R, p.G, p.B);
            p.Frame = p.FrameRate * dt + p.Frame;
            p.Width = p.DWidth * dt + p.Width;
            p.Height = p.DHeight * dt + p.Height;
        }
        LiveCount = live;
    }

    /// <summary><c>10026852</c>..<c>100269a5</c>: the wind modes.</summary>
    void Drift(ref Sprite p, float dt, float windX, float windY, float windZ)
    {
        float h = (float)((p.Y - p.WindLow) / (double)(p.WindHigh - p.WindLow));
        if (!(0f < h))
            h = 0f;
        if (1f < h)
            h = 1f;

        float f;
        switch (p.WindMode)
        {
            case 1: f = 1f; break;
            case 2: f = h; break;
            case 3: f = h * h; break;
            case 4: f = (float)((h * h + h) * 0.5); break;
            case 5:
                f = 1f;
                p.Y = (float)(dt * 1.5 + p.Y);
                break;
            case 6:
                f = h;
                float ry = (float)(_rand01() - 0.5);
                float rx = (float)(_rand01() - 0.5);
                p.Y = ry * dt + p.Y;
                p.X = rx * dt + p.X;
                break;
            default: f = 0f; break;
        }

        float k = f * p.WindScale * dt;
        p.X += windX * k;
        p.Y += windY * k;
        p.Z += windZ * k;
    }

    /// <summary>
    /// The quad's texture cell (<c>100254f7</c>): column = cell % columns, but row = cell / rows, which
    /// is only the usual row for a square grid. Returned as a row-major cell index.
    /// </summary>
    public static int Cell(float frame, int cols, int rows)
    {
        cols = Math.Max(1, cols);
        rows = Math.Max(1, rows);
        int cell = (int)frame;
        return (cell / rows) * cols + cell % cols;
    }

    static uint Pack(float a, float r, float g, float b)
    {
        // 10026a87: c * 255 is stored as a float before the subtract.
        int ia = Fistp((float)(a * 255.0) - 0.49999);
        int ir = Fistp((float)(r * 255.0) - 0.49999);
        int ig = Fistp((float)(g * 255.0) - 0.49999);
        int ib = Fistp((float)(b * 255.0) - 0.49999);
        return unchecked((uint)(((ia << 8 | ir) << 8 | ig) << 8 | ib));
    }

    static int Fistp(double v) => (int)Math.Round(v, MidpointRounding.ToEven);
}
