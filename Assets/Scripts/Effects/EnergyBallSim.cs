using System;

/// <summary>
/// Stock <c>GfxControlEnergyBall_t</c> (type 3023, 0xbcf; vftable <c>Gamecode 1016f54c</c>, object 0x8c,
/// point ctor <c>1010e56c</c>, dynel ctor <c>1010e5e1</c>, visual ctor <c>1010e659</c>, loader
/// <c>1010de89</c>, build <c>1010dd3f</c>, Process <c>1010e039</c>) and the DisplaySystem
/// <c>GfxVisualEnergyBall</c> it drives (ctor <c>100123c5</c>, render <c>10012264</c>, one quad
/// <c>10011e31</c>).
///
/// A ball of flat blades: <b>three orthogonal fans of N blades</b> (3N quads), all centred on the
/// emitter. Fan k spins about the x, y and z axis in turn, blade i of each sitting at
/// <c>i * pi / N</c>. Every blade is one square of half-size <i>radius</i>, its top two corners in one
/// colour and its bottom two in another, so the ball reads as a shaded sphere of intersecting sheets.
///
/// Fields: 0 flags, 8 duration, 9 material, 10 rise seconds, 11 fall seconds, 12 N, 13/14 handed to the
/// visual but never read by its render, 15/16/17 the radius at the start, the peak and the end,
/// 18/19 the starting top and bottom colour, 20/21 the ones they reach, 22 the ease mode, 23 how far
/// the ball wanders, 24 how much the radius pulses, 25 how far it climbs, 26 a random spawn offset.
/// Flags: 0x100 swaps the blade's UVs, 0x200 a flag on the visual's base, 0x2000 snaps the spawn to
/// the ground.
///
/// Life is <c>field 10 + field 11</c>, not field 8 (every record's field 8 is -1). The eased factor
/// <c>f</c> runs 0 to 1 over the rise and 1 back to 0 over the fall, so the two halves join smoothly.
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class EnergyBallSim
{
    /// <summary>
    /// Flags bit 8 goes to the visual's <c>+0x1c0</c> (<c>1010dd8c</c>) and swaps every blade's UV
    /// corners (<c>10011ecb</c>). Every shipped record sets it.
    /// </summary>
    public const int FlagSwapUv = 0x100;

    /// <summary>
    /// Flags bit 9 is the visual base constructor's second argument (<c>1010dd6d</c>), the same slot
    /// GroundGrid's additive flag uses. Every shipped record sets it, so every ball is additive.
    /// </summary>
    public const int FlagAdditive = 0x200;

    /// <summary>1010de5d: the spawn point is dropped to the ground.</summary>
    public const int FlagGroundSnap = 0x2000;

    /// <summary>1003dfc0's exponent on the rise and fall curves.</summary>
    public const int EaseExponent = 6;

    public readonly int Flags;
    public readonly int Material;
    public readonly int Blades;
    public readonly int EaseMode;
    public readonly float Duration;
    public readonly float RiseSeconds;
    public readonly float FallSeconds;
    public readonly float VisualField13;
    public readonly float VisualField14;
    public readonly float StartRadius, PeakRadius, EndRadius;
    public readonly uint TopFrom, BottomFrom, TopTo, BottomTo;
    public readonly float Wander;      // field 23
    public readonly float RadiusPulse; // field 24
    public readonly float Climb;       // field 25
    public readonly float SpawnJitter; // field 26

    /// <summary>The whole life, <c>field 10 + field 11</c> (1010e05e).</summary>
    public float Life => RiseSeconds + FallSeconds;

    /// <summary>3N: N blades in each of the three fans (10012264).</summary>
    public int QuadCount => 3 * Math.Max(0, Blades);

    public bool SwapUv => (Flags & FlagSwapUv) != 0;
    public bool Additive => (Flags & FlagAdditive) != 0;
    public bool GroundSnap => (Flags & FlagGroundSnap) != 0;

    /// <summary><c>t / life</c>, the visual's clock for everything that wobbles.</summary>
    public float Phase { get; private set; }

    public float Radius { get; private set; }
    public uint TopColour { get; private set; } = 0xffffffffu;
    public uint BottomColour { get; private set; } = 0xffffffffu;

    /// <summary>How far the ball has climbed off the emitter (1010e2eb).</summary>
    public float Climbed { get; private set; }

    /// <summary>The wander offset, before it is added to the emitter (1010e359).</summary>
    public float WanderX { get; private set; }
    public float WanderY { get; private set; }
    public float WanderZ { get; private set; }

    /// <summary>The spin axis, normalised, and the angle about it in radians (1010e463).</summary>
    public float AxisX { get; private set; }
    public float AxisY { get; private set; }
    public float AxisZ { get; private set; }
    public float SpinRadians { get; private set; }

    /// <summary>1010e4f0: the age has run past the whole life.</summary>
    public bool Finished { get; private set; }

    public EnergyBallSim(float[] fields)
    {
        Flags = GfxBits.Of(fields, 0);
        Duration = F(fields, 8);
        Material = GfxBits.Of(fields, 9);
        RiseSeconds = F(fields, 10);
        FallSeconds = F(fields, 11);
        Blades = GfxBits.Of(fields, 12);
        VisualField13 = F(fields, 13);
        VisualField14 = F(fields, 14);
        StartRadius = F(fields, 15);
        PeakRadius = F(fields, 16);
        EndRadius = F(fields, 17);
        TopFrom = GfxBits.UOf(fields, 18);
        BottomFrom = GfxBits.UOf(fields, 19);
        TopTo = GfxBits.UOf(fields, 20);
        BottomTo = GfxBits.UOf(fields, 21);
        EaseMode = GfxBits.Of(fields, 22);
        Wander = F(fields, 23);
        RadiusPulse = F(fields, 24);
        Climb = F(fields, 25);
        SpawnJitter = F(fields, 26);
    }

    /// <summary>
    /// The whole Process (<c>1010e039</c>). Everything is a function of the age, so this can be called
    /// with any age and needs no state of its own.
    /// </summary>
    public void Advance(float age)
    {
        float life = Life;
        // 1010e067: past the end the control is done and nothing else is touched.
        if (age > life)
        {
            Finished = true;
            return;
        }

        float t = age < life ? age : life;   // 1013f2ec, fmin
        float u = t / life;
        Phase = u;

        // 1010e09b: the rise while t is under field 10, the fall after it. f leaves both halves at 1
        // where they meet, so the ramps are continuous.
        bool rising = RiseSeconds > t;
        float f = rising ? RiseEase(t / RiseSeconds) : FallEase((t - RiseSeconds) / FallSeconds);

        // 1010e102 / 1010e225: the same two colour pairs in both halves.
        TopColour = Lerp(TopFrom, TopTo, f);
        BottomColour = Lerp(BottomFrom, BottomTo, f);

        // 1010e1af / 1010e2d8: start -> peak over the rise, then peak -> end over the fall.
        float from = rising ? StartRadius : EndRadius;
        float baseRadius = from * (1f - f) + PeakRadius * f;

        // 1010e2eb: field 25 both lifts the ball and shapes the radius envelope.
        float envelope;
        if (Climb > 0f)
        {
            Climbed = Climb * (1f - Pow(u, 8));
            envelope = u;
        }
        else if (Climb < 0f)
        {
            Climbed = Climb * (Pow(1f - u, 4) - 1f);
            envelope = 1f - u;
        }
        else
        {
            Climbed = 0f;
            envelope = 1f;
        }

        // 1010e415: the pulse rides on the blended radius, then the envelope scales the lot.
        Radius = ((float)Math.Cos(2.0 * Math.PI * u) * RadiusPulse + baseRadius) * envelope;

        // 1010e359: three different rates, so the ball wanders rather than orbiting. 1007030f is a
        // plain scale, not a set-length — the three sines are not a unit vector, so the wander's
        // reach breathes between 0 and sqrt(3) times field 23 rather than holding at it.
        WanderX = (float)Math.Sin(8.0 * Math.PI * u) * Wander;
        WanderY = (float)Math.Cos(6.0 * Math.PI * u) * Wander;
        WanderZ = (float)Math.Sin(2.0 * Math.PI * u) * Wander;

        // 1010e463: the spin axis wanders too, and the ball turns a whole revolution over its life.
        float ax = (float)Math.Cos(0.5 * Math.PI * u);
        float ay = (float)Math.Cos(1.5 * Math.PI * u);
        float az = (float)Math.Sin(3.0 * Math.PI * u);
        // 100439aa, unlike 1007030f, does normalise before it scales.
        SetLength(ref ax, ref ay, ref az, 1f);
        AxisX = ax;
        AxisY = ay;
        AxisZ = az;
        SpinRadians = (float)(2.0 * Math.PI * u);
    }

    /// <summary>1010e0ae: field 22 picks the curve; 2 is the straight line.</summary>
    public float RiseEase(float x) => EaseMode switch
    {
        1 => Pow(x, EaseExponent),
        2 => x,
        _ => 1f - Pow(1f - x, EaseExponent),
    };

    /// <summary>1010e1c0: the mirror of the rise, running 1 down to 0.</summary>
    public float FallEase(float x) => EaseMode switch
    {
        1 => Pow(1f - x, EaseExponent),
        2 => 1f - x,
        _ => 1f - Pow(x, EaseExponent),
    };

    /// <summary>
    /// The unrotated basis of blade <paramref name="index"/> (0 .. <see cref="QuadCount"/> - 1) and the
    /// axis it is spun about. 10012264 walks blade i of fan x, then y, then z, so the fan is
    /// <c>index % 3</c> and the blade <c>index / 3</c>.
    /// </summary>
    public void Blade(
        int index,
        out float axisX, out float axisY, out float axisZ,
        out float angle,
        out float ax, out float ay, out float az,
        out float bx, out float by, out float bz)
    {
        int fan = index % 3;
        int blade = index / 3;
        angle = Blades > 0 ? (float)(Math.PI / Blades) * blade : 0f;

        axisX = fan == 0 ? 1f : 0f;
        axisY = fan == 1 ? 1f : 0f;
        axisZ = fan == 2 ? 1f : 0f;

        // 10011e55: only the z fan is drawn with the argument flag set, which swings the blade's
        // first axis from x to z. This is NOT the record's 0x100 — that one picks the UVs instead,
        // for every blade at once (10011ecb reads the visual's own +0x1c0).
        bool zFan = fan == 2;
        ax = zFan ? 0f : 1f;
        ay = 0f;
        az = zFan ? 1f : 0f;
        bx = 0f;
        by = 1f;
        bz = 0f;

        Rotate(axisX, axisY, axisZ, angle, ref ax, ref ay, ref az);
        Rotate(axisX, axisY, axisZ, angle, ref bx, ref by, ref bz);
    }

    /// <summary>
    /// A blade's four corners, in the ball's own frame: <c>+B+A</c>, <c>+B-A</c>, <c>-B+A</c>,
    /// <c>-B-A</c> times the radius (10011f33..10012074), which is the order a 4-vertex D3D triangle
    /// strip wants.
    /// </summary>
    public static void Corner(
        int corner, float radius,
        float ax, float ay, float az,
        float bx, float by, float bz,
        out float x, out float y, out float z)
    {
        float sa = (corner & 1) == 0 ? radius : -radius;
        float sb = corner < 2 ? radius : -radius;
        x = bx * sb + ax * sa;
        y = by * sb + ay * sa;
        z = bz * sb + az * sa;
    }

    /// <summary>
    /// The blade's UVs. 10011ec9: the plain layout is (0,0) (0,1) (1,0) (1,1) and the swapped one
    /// (0,1) (1,1) (0,0) (1,0) — a quarter turn, not a mirror.
    /// </summary>
    public static void Uv(int corner, bool swapped, out float u, out float v)
    {
        if (!swapped)
        {
            u = corner < 2 ? 0f : 1f;
            v = (corner & 1) == 0 ? 0f : 1f;
        }
        else
        {
            u = corner == 0 || corner == 2 ? 0f : 1f;
            v = corner < 2 ? 1f : 0f;
        }
    }

    /// <summary>Rodrigues, the same turn stock gets from its axis-angle quaternion (10004840).</summary>
    public static void Rotate(float axisX, float axisY, float axisZ, float angle, ref float x, ref float y, ref float z)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        double dot = (double)axisX * x + (double)axisY * y + (double)axisZ * z;
        double cx = (double)axisY * z - (double)axisZ * y;
        double cy = (double)axisZ * x - (double)axisX * z;
        double cz = (double)axisX * y - (double)axisY * x;
        x = (float)(x * c + cx * s + axisX * dot * (1.0 - c));
        y = (float)(y * c + cy * s + axisY * dot * (1.0 - c));
        z = (float)(z * c + cz * s + axisZ * dot * (1.0 - c));
    }

    /// <summary>randy31's <c>SetLength</c> (1007030f): a zero-length vector is left alone.</summary>
    static void SetLength(ref float x, ref float y, ref float z, float length)
    {
        double m = Math.Sqrt((double)x * x + (double)y * y + (double)z * z);
        if (m <= 0.0)
            return;
        double k = length / m;
        x = (float)(x * k);
        y = (float)(y * k);
        z = (float)(z * k);
    }

    /// <summary>
    /// <c>a * (1 - f) + b * f</c> the way randy31's Color_t does it (<c>10019c93</c> scales,
    /// <c>10019bb1</c> adds): each of the four bytes on its own, rounded by adding a half and
    /// truncating, and clamped to 0..255 at both steps. Alpha rides along with the colour.
    /// </summary>
    public static uint Lerp(uint from, uint to, float f)
    {
        uint result = 0;
        for (int shift = 0; shift < 32; shift += 8)
        {
            int a = Channel((from >> shift) & 0xff, 1f - f);
            int b = Channel((to >> shift) & 0xff, f);
            int sum = a + b;
            if (sum < 0)
                sum = 0;
            else if (sum > 255)
                sum = 255;
            result |= (uint)sum << shift;
        }
        return result;
    }

    static int Channel(uint value, float f)
    {
        int scaled = (int)(value * f + 0.5f);
        if (scaled < 0)
            return 0;
        return scaled > 255 ? 255 : scaled;
    }

    static float Pow(float x, int e) => (float)Math.Pow(x, e);

    static float F(float[] f, int i) => f != null && i >= 0 && i < f.Length ? f[i] : 0f;
}
