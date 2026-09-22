using System;

/// <summary>
/// The sprite pool of stock <c>GfxVisualFlareType0</c> (DisplaySystem <c>Init 10013c2b</c>,
/// <c>InitSpriteDefault 10012fd0</c>, <c>NewSprite 100130a4</c>, <c>ProcessSprites 100131f3</c>),
/// shared by the controls that draw through it: Flare (1005, <see cref="FlareType0Sim"/>) and
/// Tracer1 (1019, <see cref="Tracer1Sim"/>). <see cref="SpriteQuad"/> is the quad it draws for one
/// sprite. No Unity dependency so the maths can be asserted from unit tests.
/// </summary>
public sealed class FlareType0Visual
{
    public struct Sprite
    {
        // Record layout +0x00 .. +0x70 (0x74 bytes).
        public float P1x, P1y, P1z;
        public float V1x, V1y, V1z;
        public float P2x, P2y, P2z;
        public float V2x, V2y, V2z;
        public float Size0, Size1, Size0Rate, Size1Rate;
        public float Life;
        public uint Argb;
        public float A, R, G, B;
        public float ARate, RRate, GRate, BRate;
        public float Frame, FrameRate;
        public bool Active;
    }

    readonly Sprite[] _pool;

    /// <summary>
    /// Visual +0x1e8, the life handed to InitSpriteDefault. A sprite still at exactly this life that
    /// would not survive a <see cref="ProcessSprites"/> call takes half of it instead.
    /// </summary>
    public float DefaultLife { get; set; }

    public Sprite[] Sprites => _pool;

    /// <summary>Live sprites after the last <see cref="ProcessSprites"/> (visual +0x224).</summary>
    public int LiveCount { get; private set; }

    /// <summary><c>Init(count)</c>: <paramref name="count"/> slots, all inactive.</summary>
    public FlareType0Visual(int count, float defaultLife)
    {
        _pool = new Sprite[Math.Max(0, count)];
        DefaultLife = defaultLife;
    }

    /// <summary>
    /// <c>NewSprite</c>: the first inactive slot takes every argument as given, the active flag
    /// included. A full pool drops the sprite.
    /// </summary>
    public bool NewSprite(in Sprite sprite)
    {
        for (int i = 0; i < _pool.Length; i++)
        {
            if (_pool[i].Active)
                continue;
            _pool[i] = sprite;
            return true;
        }
        return false;
    }

    /// <summary><c>ProcessSprites(dt)</c>.</summary>
    public void ProcessSprites(float dt)
    {
        int live = 0;
        for (int i = 0; i < _pool.Length; i++)
        {
            ref Sprite s = ref _pool[i];
            if (!s.Active)
                continue;

            // A sprite still at the default life that would not survive this call takes half of it
            // instead. Stock overwrites the delta itself, so the rest of the pool advances by that too.
            if (s.Life == DefaultLife && s.Life <= dt)
                dt = s.Life * 0.5f;

            s.Life -= dt;
            if (!(s.Life >= 0f))
            {
                s.Active = false;
                continue;
            }

            s.P1x += s.V1x * dt;
            s.P1y += s.V1y * dt;
            s.P1z += s.V1z * dt;
            s.P2x += s.V2x * dt;
            s.P2y += s.V2y * dt;
            s.P2z += s.V2z * dt;
            live++;

            s.A += dt * s.ARate;
            s.R += dt * s.RRate;
            s.G += dt * s.GRate;
            s.B += dt * s.BRate;
            s.Argb = PackArgb(s.A, s.R, s.G, s.B);

            s.Frame += s.FrameRate * dt;
            s.Size0 += s.Size0Rate * dt;
            s.Size1 += s.Size1Rate * dt;
        }

        LiveCount = live;
    }

    /// <summary>
    /// Per channel FISTP(c * 255 - 0.49999) under the default round-to-nearest-even, packed A,R,G,B
    /// with plain shifts and ORs, so an out-of-range channel spills into its neighbours as in stock.
    /// </summary>
    public static uint PackArgb(float a, float r, float g, float b)
    {
        int ia = Fistp(a * 255.0 - 0.49999);
        int ir = Fistp(r * 255.0 - 0.49999);
        int ig = Fistp(g * 255.0 - 0.49999);
        int ib = Fistp(b * 255.0 - 0.49999);
        return (uint)(((ia << 8 | ir) << 8 | ig) << 8 | ib);
    }

    static int Fistp(double v) => (int)Math.Round(v, MidpointRounding.ToEven);

    /// <summary>
    /// The quad <c>GfxVisualFlareType0</c> draws for one sprite (DisplaySystem <c>FUN_1001364b</c>).
    /// Both points are projected; their screen direction, mapped back onto the camera's right/up,
    /// gives the long axis A and its perpendicular B. The quad runs from P1 - A*size0 to
    /// P2 + A*size0 and is 2*size0 wide. Only size0 is used.
    /// Inputs are the two points in camera space (x right, y up, z forward, as D3D) and the camera's
    /// right and up vectors in the frame the points are drawn in. Outputs are the four corners in
    /// that frame: 0 = P1-A-B, 1 = P1-A+B, 2 = P2+A-B, 3 = P2+A+B (P1 end takes the cell's bottom
    /// edge, P2 end its top).
    /// </summary>
    public static void SpriteQuad(
        float p1x, float p1y, float p1z,
        float p2x, float p2y, float p2z,
        float c1x, float c1y, float c1z,
        float c2x, float c2y, float c2z,
        float rightX, float rightY, float rightZ,
        float upX, float upY, float upZ,
        float size0,
        float[] corners)
    {
        // Screen positions: divide by depth unless it is within 0.001 of zero.
        if (c1z <= -0.001f || c1z >= 0.001f)
        {
            c1x /= c1z;
            c1y /= c1z;
        }
        if (c2z <= -0.001f || c2z >= 0.001f)
        {
            c2x /= c2z;
            c2y /= c2z;
        }

        float dx = c2x - c1x;
        float dy = c2y - c1y;
        float ax, ay, az, bx, by, bz;
        if (dx == 0f && dy == 0f)
        {
            ax = rightX; ay = rightY; az = rightZ;
            bx = upX; by = upY; bz = upZ;
        }
        else
        {
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            dx /= len;
            dy /= len;
            ax = rightX * dx + upX * dy;
            ay = rightY * dx + upY * dy;
            az = rightZ * dx + upZ * dy;
            bx = rightX * dy - upX * dx;
            by = rightY * dy - upY * dx;
            bz = rightZ * dy - upZ * dx;
        }

        ax *= size0; ay *= size0; az *= size0;
        bx *= size0; by *= size0; bz *= size0;

        Set(corners, 0, p1x - ax - bx, p1y - ay - by, p1z - az - bz);
        Set(corners, 1, p1x - ax + bx, p1y - ay + by, p1z - az + bz);
        Set(corners, 2, p2x + ax - bx, p2y + ay - by, p2z + az - bz);
        Set(corners, 3, p2x + ax + bx, p2y + ay + by, p2z + az + bz);
    }

    static void Set(float[] c, int i, float x, float y, float z)
    {
        c[i * 3] = x;
        c[i * 3 + 1] = y;
        c[i * 3 + 2] = z;
    }
}
