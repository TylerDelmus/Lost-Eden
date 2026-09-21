using System;

/// <summary>
/// Pure spawn maths shared by the stock sprite emitters (_GfxControlFlare_t at FUN_100dd0c4 and
/// _GfxControlSparks_t at FUN_100f1997). Both build a sprite the same way, so the arithmetic lives
/// here without Unity types and can be locked by unit tests.
///
/// Field layout (offsets from the shared field loader FUN_100dccb4):
///   0 flags, 8 control duration, 9 material, 10 emit rate, 12..15 size pairs,
///   16..19 start ARGB, 20..23 end ARGB, 24 rand mask, 25..26 elevation range,
///   27..28 azimuth range, 29..30 speed magnitude range, 31 pool size, 32 speed scale,
///   34..35 sprite life range. Sparks additionally uses 40 as gravity.
/// </summary>
public static class SpriteEmitterMath
{
    /// <summary>
    /// Stock direction build-up: elevation and azimuth are angles in radians, magnitude scales the
    /// unit direction, and <paramref name="speedScale"/> (field 32) scales the whole vector. Records
    /// such as Flare 43721 carry field 32 = 0, which pins every sprite to the locator.
    /// </summary>
    public static void SpawnVelocity(
        float elevation, float azimuth, float magnitude, float speedScale,
        out float x, out float y, out float z)
    {
        float horizontal = (float)Math.Cos(elevation) * magnitude;
        x = (float)Math.Sin(azimuth) * horizontal * speedScale;
        y = (float)Math.Sin(elevation) * magnitude * speedScale;
        z = (float)Math.Cos(azimuth) * horizontal * speedScale;
    }

    /// <summary>
    /// Flags bit 11. GfxVisualSprite2Type0's ctor takes bool additive = (flags &amp; 0x800) == 0 and
    /// sets D3DRS_DESTBLEND to D3DBLEND_ONE (additive) or D3DBLEND_INVSRCALPHA (ordinary alpha)
    /// with D3DRS_SRCBLEND pinned to D3DBLEND_SRCALPHA. Every Sparks (1018) and FlareAlt (1006)
    /// template in gfxtweak.bin sets this bit, so those families never glow in stock.
    /// </summary>
    public const int FlagAlphaBlend = 0x800;

    /// <summary>True when the template wants additive (glowing) blending.</summary>
    public static bool IsAdditive(int flags) => (flags & FlagAlphaBlend) == 0;

    /// <summary>
    /// Height/width ratio above which a sprite is long enough for its orientation to matter. Below it
    /// the quad is near enough to square that no one can tell, so those sprites keep the cheaper
    /// screen-aligned path.
    /// </summary>
    public const float ElongationThreshold = 1.2f;

    /// <summary>
    /// Template size channel 0 is width and channel 1 is height. Confirmed independent by
    /// ProcessSprites at 0x100267c1, which animates the two per-sprite scalars at +0x24 and +0x28
    /// by their own separate rates rather than treating them as one size's start and end.
    ///
    /// Flare templates are usually strongly elongated — 46002's hand spikes are 0.05 x 1.0 — while
    /// Sparks templates such as 43720 (0.67 square) are not.
    ///
    /// A streak that has travelled away from its emitter is laid along that offset, so it reads as a
    /// spike rooted in the emitter and pointing outward. One that has not — Cord, whose spread has
    /// zero speed — has no direction to use and stays camera-facing on its own per-sprite roll.
    /// </summary>
    public static bool IsStreak(float width, float height)
        => width > 0f && height > width * ElongationThreshold;


    /// <summary>
    /// Splits the two size channels into a width and a height. Either may legitimately be zero
    /// mid-animation — every Cord template runs its length from 0 to 1 — so a zero is passed
    /// through rather than replaced. Only a template with nothing usable at all falls back to 1.
    /// </summary>
    public static void ResolveSizeChannels(
        float size0, float size1, out float width, out float height)
    {
        bool hasWidth = IsUsableSize(size0);
        bool hasHeight = IsUsableSize(size1);
        if (!hasWidth && !hasHeight)
        {
            width = 1f;
            height = 1f;
            return;
        }
        width = hasWidth ? size0 : 0f;
        height = hasHeight ? size1 : 0f;
    }

    /// <summary>Matches GfxControl.ClampScale's window: stock sizes run from 0.05 up to a few units.</summary>
    static bool IsUsableSize(float v)
        => !float.IsNaN(v) && !float.IsInfinity(v) && v > 0f && v <= 8f;


    /// <summary>
    /// Stock composes the spawn direction from the locator's own basis axes:
    /// axisRight * x + axisUp * y + axisForward * z. The cone is therefore oriented by the locator,
    /// but the result is a world-space velocity — which is why gravity must NOT be rotated with it.
    /// </summary>
    public static void ComposeWorldVelocity(
        float rightX, float rightY, float rightZ,
        float upX, float upY, float upZ,
        float forwardX, float forwardY, float forwardZ,
        float localX, float localY, float localZ,
        out float x, out float y, out float z)
    {
        x = rightX * localX + upX * localY + forwardX * localZ;
        y = rightY * localX + upY * localY + forwardY * localZ;
        z = rightZ * localX + upZ * localY + forwardZ * localZ;
    }

    /// <summary>Stock reads ranges as min + (max - min) * rand01, so an inverted pair is still valid.</summary>
    public static float Range(float a, float b, float unit01) => a + (b - a) * unit01;

    /// <summary>
    /// Stock emit target is round(field10 * age), and the spawn counter starts at -field31.
    /// With field10 = 0 that makes the first frame release exactly <paramref name="pool"/> sprites
    /// and nothing afterwards — a burst rather than a trickle.
    /// </summary>
    public static int TargetSpawnCount(float emitRate, float age)
        => (int)Math.Round(emitRate * age, MidpointRounding.AwayFromZero);

    public static int InitialSpawnCounter(int burstCount) => -burstCount;

    /// <summary>
    /// Headroom stock allows over the steady-state population, so a slot is always free to spawn
    /// into. <see cref="PoolCapacity"/>.
    /// </summary>
    public const float PoolHeadroom = 1.5f;

    /// <summary>Swaps which template field drives which end of a spike. <see cref="SpikeEndScales"/>.</summary>
    public const int FlagSwapSpikeEnds = 0x100;

    /// <summary>
    /// How far each end of a Flare spike travels along its spawn direction, as a multiple of the
    /// direction vector (which already carries the magnitude from fields 29/30).
    ///
    /// A Flare sprite tracks two points, not one. Its spawn (Gamecode FUN_100dd0c4) hands the visual
    /// two positions and two rates:
    ///
    /// <code>
    /// if ((flags &amp; 0x100) == 0) { a = dir * field32; other = field33; }
    /// else                        { a = dir * field33; other = field32; }
    /// b = dir * other;
    /// endA = 0; endB = 0;                 // both ends start on the emitter
    /// rateA = a * (1/life); rateB = b * (1/life);
    /// </code>
    ///
    /// So both ends begin together on the hand and slide outward along the same random direction at
    /// different rates. The two points do <em>not</em> bound a stretched quad, though: the builder at
    /// DisplaySystem FUN_1001364b projects both, normalises the screen-space direction between them,
    /// and uses it only to orient a square whose half-extent along both axes is size0:
    ///
    /// <code>
    /// 10013944  LEA EAX, [ECX + EAX*1 + 0x30]   ; &amp;sprite.size0
    /// 10013948  FLD float ptr [EAX]             ; U *= size0
    /// 1001397c  FLD float ptr [EAX]             ; V *= size0   (same address)
    /// 10013a08  ADD ECX, [EBP + -0x10]          ; quad centre = sprite + 0 = the leading point
    /// </code>
    ///
    /// So <paramref name="endA"/> is the reach — the leading point ends its life at
    /// <c>field33 * magnitude</c> from the hand — and <paramref name="endB"/> only places the trailing
    /// point that the roll is measured from. Nothing here sets a length; that is <c>2 * size0</c>,
    /// which is why the whole 46000..46017 ladder leaves size1 pinned at 1.0 and never reads it.
    ///
    /// By that reading the ladder buys spread, not size: 46002 would scatter its sprites over 0.35..0.85
    /// units and 46016 over 2.1..3.1, each one a 0.1..0.2 square rolled to point back at the hand.
    ///
    /// The three unknowns that kept this divergence open are now closed, and none of them was the
    /// geometry. The atlas frame is not in play: both flare templates name material 15 (s_bullet.png)
    /// which is 1x1 with a single frame. The Nano0/Nano1 children cannot be contributing anything at
    /// all — Spell1 only reaches fields 31/32 from its window 3 and 4 enter handlers, and every one of
    /// the 178 Spell1 records in gfxtweak.bin starts window 3 at 20 seconds or later. And no parent
    /// scale reaches the sprite: the window 1 enter at Gamecode FUN_100f27c3 calls only vtable +0x2c
    /// and +0x30 on each child, which are its two colour blocks.
    ///
    /// So the square is what stock draws, and the reason it reads as a glowing core is population
    /// rather than size. 46016 keeps ~128 sprites alive at 64/s over a 1.5..2s life, so the youngest
    /// are all still within a fraction of a unit of the hand; a heap of additive 0.1..0.2 squares there
    /// is the core, thinning outward into the spray. Stretching each one into a 1.4-unit sliver, which
    /// is what our renderer used to do, spreads that same energy over ten times the area and leaves
    /// only the sparks.
    /// </summary>
    public static void SpikeEndScales(
        int flags, float field32, float field33, out float endA, out float endB)
    {
        if ((flags & FlagSwapSpikeEnds) != 0)
        {
            endA = field33;
            endB = field32;
        }
        else
        {
            endA = field32;
            endB = field33;
        }
    }

    /// <summary>
    /// Side of the square a Flare spike is drawn as. The builder at DisplaySystem FUN_1001364b scales
    /// both quad axes by the sprite's own size0, and the half-extents it adds and subtracts give a
    /// full side of <c>2 * size0</c> — so 46002 and 46016, whose size0 animates 0.05 -> 0.1, draw a
    /// 0.1 unit square growing to 0.2. size1 never reaches the builder, which is why the whole
    /// 46000..46017 ladder leaves it pinned at 1.0.
    /// </summary>
    public static float SpikeQuadSize(float size0) => 2f * size0;

    /// <summary>
    /// Roll for a Flare spike, in degrees about the view axis. Stock projects both of the sprite's
    /// points to screen space, normalises the direction between them and uses that as one quad axis
    /// with its perpendicular as the other, so the square carries the spike's screen-space heading
    /// however the camera moves. That heading is the arctangent of the projected delta.
    ///
    /// Both points project to the same pixel while the spike still has no length, which is every
    /// sprite on its spawn frame; there is no direction to measure then, so the roll is zero.
    /// </summary>
    public static float SpikeRollDegrees(float screenDx, float screenDy)
    {
        if (screenDx == 0f && screenDy == 0f)
            return 0f;
        return MathF.Atan2(screenDy, screenDx) * (180f / MathF.PI);
    }


    /// <summary>
    /// How many sprites the pool holds, exactly as stock sizes it in the sprite-family create
    /// (Gamecode FUN_100f1712 at 100f187e):
    ///
    /// <code>
    /// FLD  [ESI + 0x3c]          ; field 10, the emit rate
    /// FMUL [0x10161928]          ; * 1.5
    /// FMUL [ESI + 0xa0]          ; * field 35, the longest sprite life
    /// CALL 1013f010              ; round to int
    /// CMP  EAX, [ESI + 0x90]     ; pool = max(that, field 31)
    /// </code>
    ///
    /// The steady-state population is rate * life, and the 1.5 is headroom: NewSprite scans for a
    /// free slot and silently drops the spawn when the pool is full, so a pool sized to exactly
    /// rate * life would keep losing sprites to rounding.
    ///
    /// It is a max rather than a sum, and field 31 is the first-frame burst — the create seeds the
    /// spawn counter with <c>0xb8 -= field31</c>. Pure bursts therefore come out exactly: with
    /// field 10 at 0 the first term vanishes, leaving Flare 43721 its 16 slots and Sparks 43720
    /// its 120.
    /// </summary>
    public static int PoolCapacity(int burstCount, float emitRate, float maxLife, int maxSprites)
    {
        if (maxSprites < 1)
            return 1;

        int total = burstCount > 0 ? burstCount : 0;

        bool rateUsable = emitRate > 0f && !float.IsNaN(emitRate) && !float.IsInfinity(emitRate);
        bool lifeUsable = maxLife > 0f && !float.IsNaN(maxLife) && !float.IsInfinity(maxLife);
        if (rateUsable && lifeUsable)
        {
            double steady = Math.Round((double)emitRate * PoolHeadroom * maxLife,
                MidpointRounding.AwayFromZero);
            int trickle = steady >= maxSprites ? maxSprites : (int)steady;
            if (trickle > total)
                total = trickle;
        }

        if (total < 1)
            return 1;
        return total > maxSprites ? maxSprites : total;
    }

    /// <summary>Stock gate: (field24 &amp; rand()) == 0.</summary>
    public static bool RandMaskPasses(int randMask, int randomValue) => (randMask & randomValue) == 0;

    /// <summary>
    /// Stock emission window: infinite duration always emits, otherwise emission stops one
    /// maximum sprite lifetime before the control ends so nothing is cut off mid-flight.
    /// </summary>
    public static bool WithinEmitWindow(float duration, float age, float maxSpriteLife)
        => duration < 0f || age < duration - maxSpriteLife;

    /// <summary>Per-sprite animation rate stock precomputes as (end - start) / life.</summary>
    public static float RatePerSecond(float start, float end, float life)
        => life > 1e-4f ? (end - start) / life : 0f;
}
