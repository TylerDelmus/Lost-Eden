using System;

/// <summary>
/// Stock <c>GfxControlSpiral2_t</c> (typeCode 3033, 0xbd9; vftable <c>Gamecode 1016f924</c>, loader
/// <c>10111ebd</c>, Process <c>10111c40</c>, slot 6 <c>10111d75</c>, ctors <c>101121e9</c> /
/// <c>10112242</c>): N helical ribbons (<see cref="Spiral2Ribbon"/>) spaced evenly round a common
/// axis, all sharing one turn about the world y that spins up under a constant angular acceleration.
/// A colour curve drives their colour and a float curve their radius, both over age / duration.
/// Nothing here depends on Unity.
///
/// Fields: 0 flags, 1-6 the locator offset and turn, 7 attach, 8 duration, 9 material, 10 the number
/// of ribbons, 11 segments per ribbon, 12 the wobble rate, 13 the ribbon's half width, 14 its height,
/// 15 the start angle, 16 the angle it sweeps, 17 the u scale, 18 the angular velocity, 19 the
/// angular acceleration, 20 the fade-out width, 21 the fade-in width; then the colour curve and the
/// radius curve.
///
/// Stock loads BOTH curves with <c>101166f2</c>'s int mode (<c>10111fb9</c> / <c>10111fc8</c>) yet
/// reads the second one with the float evaluator (<c>1011689d</c>). That is harmless, not a bug: the
/// two template readers (<c>10106dc8</c> int, <c>10106de9</c> float) return the same dword and the
/// loader stores it in the same 4-byte slot either way, so the record's raw bits survive and each
/// evaluator sees what its record author wrote.
///
/// <c>+0x80</c>, which scales the segment count and the height and sweep passed to every ribbon, is
/// set to 1 by the shared ctor (<c>10112220</c>) and never written again, so it is a constant here.
/// </summary>
public sealed class Spiral2Sim
{
    /// <summary>Ctor argument 1 (<c>101120f5</c>): the ribbons are additive.</summary>
    public const int FlagAdditive = 0x200;

    /// <summary>Tested in the geometry builder (<c>10022461</c>): the alpha wobbles along the ribbon.</summary>
    public const int FlagWobble = 0x400;

    /// <summary>Stock's full turn (<c>1016c5b8</c>), a rounded 2π.</summary>
    public const float FullTurn = 6.28000020980835f;

    /// <summary>
    /// <c>10112022</c>: field 15 exactly 999 means "pick the start angle at random" rather than a
    /// fixed one, so every cast of that nano winds from somewhere else.
    /// </summary>
    public const float RandomStartAngle = 999f;

    /// <summary><c>+0x80</c>, <c>fld1</c> at <c>10112220</c> and never overwritten.</summary>
    public const float Scale = 1f;

    /// <summary>Slot 6's fade takes exactly this long (<c>10111d96</c>: duration = age + 1).</summary>
    public const float StopSeconds = 1f;

    public int Flags { get; }
    public float Duration { get; private set; }
    public int Material { get; }
    public int Segments { get; }

    /// <summary>Field 18 as loaded; <see cref="Spin"/> starts here and accelerates away from it.</summary>
    public float InitialSpin { get; }

    /// <summary>Field 19: radians per second added to <see cref="Spin"/> every second.</summary>
    public float AngularAcceleration { get; }

    public StockColorCurve ColourCurve { get; private set; }
    public StockFloatCurve RadiusCurve { get; private set; }

    /// <summary>Stock <c>+0x44</c>: the turn about the world y, radians.</summary>
    public float Angle { get; private set; }

    /// <summary>Stock <c>+0x70</c>: the current angular velocity, which field 19 keeps changing.</summary>
    public float Spin { get; private set; }

    /// <summary>The ribbons, in the order stock builds them (<c>10112064</c>).</summary>
    public Spiral2Ribbon[] Ribbons { get; }

    /// <summary>The base start angle every ribbon is offset from — field 15, or a random turn.</summary>
    public float BaseStartAngle { get; }

    public Spiral2Sim(float[] fields, Func<float> random = null)
    {
        Flags = Int(fields, 0);
        Duration = F(fields, 8);
        Material = Int(fields, 9);

        int count = Int(fields, 10);
        if (count < 0)
            count = 0;

        // 101120e2: the segment count is the only field rounded, by _ftol.
        Segments = (int)(Int(fields, 11) * (double)Scale);

        float wobbleRate = F(fields, 12);   // +0x1e0
        float halfWidth = F(fields, 13);    // +0x1c0
        float height = F(fields, 14) * Scale;   // +0x1d4
        float sweep = F(fields, 16) * Scale;    // +0x1cc, stepped by sweep / segments
        float uScale = F(fields, 17);       // +0x1c8
        float fadeOut = F(fields, 20);      // +0x1d8
        float fadeIn = F(fields, 21);       // +0x1dc

        InitialSpin = F(fields, 18);
        AngularAcceleration = F(fields, 19);
        Spin = InitialSpin;

        float start = F(fields, 15);
        BaseStartAngle = start == RandomStartAngle
            ? (float)((random != null ? random() : 0f) * (double)FullTurn)
            : start;

        int index = 22;
        ColourCurve = StockColorCurve.Read(fields, ref index);
        RadiusCurve = StockFloatCurve.Read(fields, ref index);

        Ribbons = new Spiral2Ribbon[count];
        for (int i = 0; i < count; i++)
            Ribbons[i] = new Spiral2Ribbon(
                Flags, Segments, RibbonStartAngle(i, count, BaseStartAngle),
                wobbleRate, halfWidth, height, sweep, uScale, fadeIn, fadeOut);
    }

    public bool Additive => (Flags & FlagAdditive) != 0;

    /// <summary><c>101120a9</c>: ribbon <paramref name="i"/> of <paramref name="count"/>.</summary>
    public static float RibbonStartAngle(int i, int count, float baseAngle)
        => count == 0 ? baseAngle : (float)(i * (double)FullTurn / count + baseAngle);

    /// <summary>Stock slot 8 (<c>10111d5d</c> forwards to the base): <c>+0x10</c> = seconds.</summary>
    public void SetDuration(float seconds) => Duration = seconds;

    /// <summary>
    /// One Process body (<c>10111ca2</c>) after the base timer, at <paramref name="age"/>. The angle
    /// and the spin both integrate per call, which is why the control replays this at a fixed rate.
    /// </summary>
    public void Step(float age, float dt)
    {
        // 10111cb2: angle += spin * dt, then spin += acceleration * dt — in that order, so the first
        // step turns by the loaded velocity and only afterwards picks up the acceleration.
        Angle = (float)(Spin * (double)dt + Angle);
        Spin = (float)(AngularAcceleration * (double)dt + Spin);
    }

    /// <summary>
    /// <c>10111ca2</c>: the phase both curves are read at. Stock does not clamp it, and the base timer
    /// ends the control once age passes the duration, so it only ever exceeds 1 on the frame it ends.
    /// </summary>
    public float Phase(float age) => (float)(age / (double)Duration);

    /// <summary>The ribbon colour at <paramref name="age"/> (<c>1011678b</c> into vfunc 0x54).</summary>
    public uint ColourAt(float age) => ColourCurve.Evaluate(Phase(age));

    /// <summary>The ribbon radius at <paramref name="age"/> (<c>1011689d</c> × <c>+0x80</c>).</summary>
    public float RadiusAt(float age) => (float)(RadiusCurve.Evaluate(Phase(age)) * (double)Scale);

    /// <summary>
    /// Stock slot 6 (<c>10111d75</c>). Unlike most controls this is not an ending: while there is more
    /// than a second left it pulls the duration in to <paramref name="age"/> + 1 and rewrites both
    /// curves into a three-key hold-then-go, so the ribbons keep their look until the moment they were
    /// interrupted and then take exactly one second to leave — the colour to nothing, the radius one
    /// unit wider. Both replacement values are the curves' value at t = 0, not at the current phase;
    /// that is what stock reads (<c>10111da2</c> / <c>10111e21</c> both push zero).
    ///
    /// Past that point (age &gt; duration - 1) stock does nothing at all and lets the timer run out.
    /// </summary>
    public void TerminateGracefully(float age)
    {
        if (age > Duration - StopSeconds)
            return;

        Duration = (float)(age + (double)StopSeconds);
        float hold = Phase(age);

        float radius = RadiusCurve.Evaluate(0f);
        RadiusCurve = new StockFloatCurve(
            new[] { 0f, hold, 1f },
            new[] { radius, radius, (float)(radius + 1.0) });

        uint colour = ColourCurve.Evaluate(0f);
        ColourCurve = new StockColorCurve(
            new[] { 0f, hold, 1f },
            new[] { colour, colour, 0u });
    }

    static float F(float[] f, int i) => f != null && i >= 0 && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i)
        => f != null && i >= 0 && i < f.Length ? BitConverter.SingleToInt32Bits(f[i]) : 0;
}

/// <summary>
/// One DisplaySystem <c>GfxVisualSpiral2</c> (ctor <c>1002270b</c>, vftable <c>1008b2dc</c>, draw slot
/// 13 <c>10022687</c>, geometry <c>10022407</c>): a helical ribbon drawn as a single triangle strip of
/// 2 · segments vertices through randy31's <c>render_t::RenderTriangleStrip</c> (<c>10089808</c>) in
/// D3DFVF_XYZ | DIFFUSE | TEX1 (0x142), stride 0x18.
///
/// Each of the <c>segments</c> steps emits a pair. With f running 0, 1/N, … (N-1)/N along the ribbon
/// and theta starting at the ribbon's own start angle and advancing by sweep / segments:
/// x = sin(theta) · radius, z = cos(theta) · radius, and y = height · f either side of the centre by
/// the half width. u = f · uScale on both; v is 0 on the upper vertex and 1 on the lower.
///
/// Every vertex takes the control's colour with its alpha scaled by the fade in, the fade out and,
/// with flag 0x400, a sine wobble that travels along the ribbon as the effect ages.
///
/// <c>+0x1e4</c>, which the draw gates on, is left at zero by the ctor (<c>100227a3</c> stores the
/// <c>fldz</c> that has been sitting on the x87 stack since <c>1002272e</c>) and nothing ever writes
/// it, so the gate — "draw while it is not negative" — always passes. It is not modelled.
/// </summary>
public sealed class Spiral2Ribbon
{
    readonly int _flags;

    /// <summary>Stock <c>+0x1e0</c>, field 12: radians per second the wobble travels.</summary>
    public float WobbleRate { get; }

    /// <summary>Stock <c>+0x1c0</c>, field 13: half the ribbon's width, either side of its centre.</summary>
    public float HalfWidth { get; }

    /// <summary>Stock <c>+0x1d4</c>, field 14 × scale: how far the helix climbs over its length.</summary>
    public float Height { get; }

    /// <summary>Stock <c>+0x1c4</c>: where this ribbon's helix starts, radians.</summary>
    public float StartAngle { get; }

    /// <summary>Stock <c>+0x1cc</c>, field 16 × scale: the angle the helix sweeps in total.</summary>
    public float Sweep { get; }

    /// <summary>Stock <c>+0x1d0</c>: <see cref="Sweep"/> / segments, as an integer divide.</summary>
    public float AngleStep { get; }

    /// <summary>Stock <c>+0x1c8</c>, field 17: u at the far end of the ribbon.</summary>
    public float UScale { get; }

    /// <summary>Stock <c>+0x1dc</c>, field 21: the leading fraction that fades in. 0 disables it.</summary>
    public float FadeIn { get; }

    /// <summary>Stock <c>+0x1d8</c>, field 20: the trailing fraction that fades out. 0 disables it.</summary>
    public float FadeOut { get; }

    public int Segments { get; }
    public int VertexCount => 2 * Segments;

    public readonly float[] X;
    public readonly float[] Y;
    public readonly float[] Z;
    public readonly float[] U;
    public readonly float[] V;

    /// <summary>Per-vertex D3DCOLOR, the control's colour with the faded alpha.</summary>
    public readonly uint[] Colors;

    public Spiral2Ribbon(
        int flags, int segments, float startAngle, float wobbleRate, float halfWidth, float height,
        float sweep, float uScale, float fadeIn, float fadeOut)
    {
        _flags = flags;
        Segments = segments > 0 ? segments : 0;
        StartAngle = startAngle;
        WobbleRate = wobbleRate;
        HalfWidth = halfWidth;
        Height = height;
        Sweep = sweep;
        UScale = uScale;
        FadeIn = fadeIn;
        FadeOut = fadeOut;
        // 100227bc: fidiv by the segment count, so a zero-segment ribbon never gets here.
        AngleStep = Segments > 0 ? (float)(sweep / (double)Segments) : 0f;

        int n = VertexCount;
        X = new float[n];
        Y = new float[n];
        Z = new float[n];
        U = new float[n];
        V = new float[n];
        Colors = new uint[n];
    }

    /// <summary>
    /// The geometry builder (<c>10022407</c>) at phase <paramref name="t"/> (<c>+0x1ac</c>) with the
    /// control's <paramref name="radius"/> (<c>+0x1bc</c>) and <paramref name="colour"/> (<c>+0x1b0</c>).
    /// </summary>
    public void Build(float t, float radius, uint colour)
    {
        int n = VertexCount;
        if (n == 0)
            return;

        float alpha01 = (float)((colour >> 24) / 255.0);
        uint rgb = colour & 0xffffffu;
        float theta = StartAngle;

        for (int i = 0; i < n; i += 2)
        {
            float f = (float)(i / (double)n);
            float x = (float)(Math.Sin(theta) * radius);
            float z = (float)(Math.Cos(theta) * radius);

            // 10022479: the wobble travels four times round the helix per turn of the sine.
            float wobble = 1f;
            if ((_flags & Spiral2Sim.FlagWobble) != 0)
                wobble = (float)((Math.Sin(WobbleRate * (double)t + theta * 4.0) + 1.0) * 0.5);

            uint packed = (uint)((int)(255.0 * alpha01 * wobble * Fade(f)) & 0xff) << 24 | rgb;

            float climb = (float)(Height * (double)f);
            float u = (float)(f * (double)UScale);

            X[i] = x; Y[i] = (float)(climb + HalfWidth); Z[i] = z; U[i] = u; V[i] = 0f;
            X[i + 1] = x; Y[i + 1] = (float)(climb - HalfWidth); Z[i + 1] = z; U[i + 1] = u; V[i + 1] = 1f;
            Colors[i] = packed;
            Colors[i + 1] = packed;

            theta = (float)(AngleStep + (double)theta);
        }
    }

    /// <summary>
    /// The alpha scale at <paramref name="f"/> along the ribbon (<c>100224de</c>-<c>10022556</c>).
    /// Either end can fade, and when both windows reach the same vertex the fade-out wins: stock
    /// writes them to the same slot and the fade-out is written second.
    /// </summary>
    public float Fade(float f)
    {
        float fade = 1f;
        if (FadeIn != 0f && f < FadeIn)
            fade = (float)(f / (double)FadeIn);
        if (FadeOut != 0f && f > 1f - FadeOut)
            fade = (float)(1.0 - (f - (1.0 - FadeOut)) / FadeOut);
        return fade;
    }
}
