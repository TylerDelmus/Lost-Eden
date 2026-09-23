using System;

/// <summary>
/// Stock <c>GfxControlBeam_t</c> (typeCode 3022, 0xbce; vftable <c>Gamecode 1016ef3c</c>, Process
/// <c>10109c0c</c>, slot 6 <c>101096c2</c>, dynel ctor <c>1010a1cf</c>, build + field loader
/// <c>101096ee</c> / <c>10109817</c>): a star of flat blades standing on a locator, widening or
/// narrowing from one radius at its foot to another at its tip, its two ends separately coloured.
/// It fades in, holds, fades out, and can repeat that whole cycle with a blank gap between takes.
/// Nothing here depends on Unity; <see cref="BeamBlades"/> builds the geometry.
///
/// Fields: 0 flags, 1-6 the locator offset and turn, 7 attach, 8 duration, 9 material, 10 the gate on
/// the falling anchor, 11 the fade-in length, 12 the fade-out length, 13 the repeat count, 14 the
/// number of blades, 15 the u the texture runs to, 16 the v it spans, 17 the length, 18-23 three
/// (foot, tip) radius pairs, 24-29 six packed ARGB colours as two three-stop ramps, 30 the spin,
/// 31-38 the wobble, 39 a second material, 40 where along the beam that material's quad sits, 41 the
/// gap between repeats.
///
/// <b>Duration.</b> A field 8 of zero or less is not "forever": the build reads it as
/// <c>f11 + f12</c> (<c>101099fa</c>), so such a record lasts exactly as long as its two fades.
///
/// <b>The cycle.</b> Stock keeps its own age (the base timer's <c>+0xc</c>) and resets it to zero to
/// start a repeat, so the age here is the age within the current take, not the control's. On reaching
/// the duration it blanks both colours and holds for field 41, then spends one of its repeats and
/// starts over; when they run out it ends. Field 13 is decremented in place, so a record's repeat
/// count is consumed, not remembered.
///
/// <b>Rate.</b> Almost everything is read from the age, but the repeat machine, the per-call jitter
/// and the falling anchor are stepped per call, and they change what is on screen: the weapon family
/// runs 0.02 s takes with 0.04 s gaps, which at 30 Hz is one frame on and one off and at 300 FPS
/// would be six on and twelve off. <see cref="GfxControlBeam"/> therefore replays this on the fixed
/// stock clock (Docs §3.7 group B).
/// </summary>
public sealed class BeamSim
{
    /// <summary><c>1010a15c</c>: an extra quarter turn about x before the spin.</summary>
    public const int FlagQuarterTurn = 0x20000;

    /// <summary><c>10109d1b</c>: the anchor is taken from the locator every call.</summary>
    public const int FlagFollowLocator = 0x800;

    /// <summary><c>10109d42</c>: the anchor's height is snapped to the ground under it.</summary>
    public const int FlagGroundSnap = 0x2000;

    /// <summary><c>10109d61</c>: with the snap, the length becomes the climb. No record sets it.</summary>
    public const int FlagGroundRateLimit = 0x4000;

    /// <summary><c>10109fd5</c>: both radii get a travelling sine and a per-call jitter.</summary>
    public const int FlagWobble = 0x400;

    /// <summary>Visual ctor argument 8 (<c>10109740</c>), the same bit Spiral2 draws additive on.</summary>
    public const int FlagAdditive = 0x200;

    /// <summary><c>10109cbe</c>: a field 30 of exactly 999 means a random angle, re-picked per take.</summary>
    public const float RandomSpinAngle = 999f;

    /// <summary><c>10109cd7</c>: the random angle is scaled by this. It is used as radians, so it wraps.</summary>
    public const float RandomAngleScale = 360f;

    /// <summary><c>10109a6e</c>: the anchor's fall, units per second per second.</summary>
    public const float FallAcceleration = -800f;

    /// <summary>Slot 6's fade takes exactly this long (<c>101096d5</c>: duration = age + 3).</summary>
    public const float StopSeconds = 3f;

    /// <summary><c>10109844</c>: fields 37 and 38 are degrees, turned into radians like this.</summary>
    public const double DegreesToRadians = 0.7853981633974483 / 45.0;

    /// <summary>
    /// <c>+0xd4</c>: set to 1 by the shared sub-ctor (<c>10109a84</c>) and never written again — the
    /// vftable has no setter for it and none of the four ctors touch it — so it is a constant.
    /// </summary>
    public const float Scale = 1f;

    readonly Func<float> _random;

    readonly float _fadeIn;                 // field 11, +0x38
    float _fadeOut;                         // field 12, +0x3c — slot 6 rewrites it
    readonly float _length;                 // field 17, +0x50
    readonly float _footStart, _tipStart;   // fields 18, 19
    readonly float _footHold, _tipHold;     // fields 20, 21
    readonly float _footEnd, _tipEnd;       // fields 22, 23
    readonly uint _c1Start, _c2Start;       // fields 24, 25
    readonly uint _c1Hold, _c2Hold;         // fields 26, 27
    readonly uint _c1End, _c2End;           // fields 28, 29
    readonly float _spin;                   // field 30, +0x84
    readonly float _footJitter, _tipJitter; // fields 31, 32
    readonly float _footSwing, _tipSwing;   // fields 33, 34
    readonly float _footRate, _tipRate;     // fields 35, 36
    readonly float _footPhase, _tipPhase;   // fields 37, 38, in radians
    readonly float _gap;                    // field 41, +0xb0
    readonly int _fallGate;                 // field 10, +0x34

    public int Flags { get; }
    public int Material { get; }
    public int SecondMaterial { get; }
    public int BladeCount { get; }
    public float UMax { get; }
    public float VSpan { get; }

    /// <summary>Field 40: where along the beam the second material's quad sits, 0 foot to 1 tip.</summary>
    public float SecondQuadHeight { get; }

    public float Duration { get; private set; }

    /// <summary>Stock <c>+0xc</c>: the age within the current take, reset to 0 by a repeat.</summary>
    public float CycleAge { get; private set; }

    /// <summary>Stock <c>+0x40</c>, field 13, decremented in place as takes are spent.</summary>
    public int RepeatsLeft { get; private set; }

    /// <summary>Stock <c>+0x14</c> set to 1: the control is finished.</summary>
    public bool Dead { get; private set; }

    /// <summary>In the gap between takes (<c>10109c74</c>): both colours are zeroed, nothing shows.</summary>
    public bool Blanked { get; private set; }

    /// <summary>Stock <c>+0xcc</c>, first picked by the sub-ctor (<c>10109a4f</c>).</summary>
    public float RandomAngle { get; private set; }

    /// <summary>Stock <c>+0xd0</c>, the anchor's fall speed. Starts at 0 (<c>10109a7a</c>).</summary>
    public float FallVelocity { get; private set; }

    /// <summary>Stock <c>+0xb8</c>..<c>+0xc0</c>, the world point the beam stands on.</summary>
    public float AnchorX { get; private set; }
    public float AnchorY { get; private set; }
    public float AnchorZ { get; private set; }

    /// <summary>What the last <see cref="Step"/> handed the visual.</summary>
    public State Current;

    /// <summary>The step before it, for the port's interpolation.</summary>
    public State Previous;

    /// <summary>
    /// The ground height under a point, for <see cref="FlagGroundSnap"/> (stock <c>100d33f3</c>, which
    /// asks n3Playfield). Null leaves the anchor where the locator put it; only record 71064 asks.
    /// </summary>
    public Func<float, float, float> GroundHeight { get; set; }

    public BeamSim(float[] fields, Func<float> random = null)
    {
        _random = random;

        Flags = Int(fields, 0);
        Material = Int(fields, 9);
        _fallGate = Int(fields, 10);
        _fadeIn = F(fields, 11);
        _fadeOut = F(fields, 12);
        RepeatsLeft = Int(fields, 13);
        BladeCount = Int(fields, 14);
        UMax = F(fields, 15);
        VSpan = F(fields, 16);
        _length = F(fields, 17);

        _footStart = F(fields, 18);
        _tipStart = F(fields, 19);
        _footHold = F(fields, 20);
        _tipHold = F(fields, 21);
        _footEnd = F(fields, 22);
        _tipEnd = F(fields, 23);

        _c1Start = UInt(fields, 24);
        _c2Start = UInt(fields, 25);
        _c1Hold = UInt(fields, 26);
        _c2Hold = UInt(fields, 27);
        _c1End = UInt(fields, 28);
        _c2End = UInt(fields, 29);

        _spin = F(fields, 30);
        _footJitter = F(fields, 31);
        _tipJitter = F(fields, 32);
        _footSwing = F(fields, 33);
        _tipSwing = F(fields, 34);
        _footRate = F(fields, 35);
        _tipRate = F(fields, 36);
        _footPhase = (float)(F(fields, 37) * DegreesToRadians);
        _tipPhase = (float)(F(fields, 38) * DegreesToRadians);

        SecondMaterial = Int(fields, 39);
        SecondQuadHeight = F(fields, 40);
        _gap = F(fields, 41);

        // 101099fa: a duration of zero or less is the two fades back to back, not "forever".
        float duration = F(fields, 8);
        Duration = duration <= 0f ? (float)(_fadeIn + (double)_fadeOut) : duration;

        // 10109a4f: the sub-ctor picks the first random angle whether or not field 30 asks for one.
        RandomAngle = (float)(Next() * (double)RandomAngleScale);
    }

    public bool Additive => (Flags & FlagAdditive) != 0;
    public bool FollowsLocator => (Flags & FlagFollowLocator) != 0;
    public bool QuarterTurn => (Flags & FlagQuarterTurn) != 0;

    /// <summary>Stock slot 8 (<c>1010340b</c>, the base's): <c>+0x10</c> is the duration.</summary>
    public void SetDuration(float seconds) => Duration = seconds;

    /// <summary>
    /// Stock slot 6 (<c>101096c2</c>), which is not an ending: while there is more than three seconds
    /// left it pulls the duration in to <paramref name="age"/> + 3 and makes the fade-out exactly three
    /// seconds, so the beam leaves through its own end ramp. Past that point it does nothing.
    /// </summary>
    public void TerminateGracefully(float age)
    {
        if (age > Duration - StopSeconds)
            return;
        Duration = (float)(age + (double)StopSeconds);
        _fadeOut = StopSeconds;
    }

    /// <summary>
    /// One Process call (<c>10109c0c</c>) of <paramref name="dt"/> seconds. The locator arguments are
    /// stock's <c>10106306</c> result and <paramref name="locatorValid"/> its <c>10106591</c>.
    /// </summary>
    public void Step(float dt, bool locatorValid, float lx, float ly, float lz)
    {
        if (Dead)
            return;

        Previous = Current;

        // The base timer (100d2a86) runs before the body and owns the age.
        CycleAge = (float)(CycleAge + (double)dt);
        Blanked = false;

        if (Duration <= CycleAge)
        {
            // 10109c46: out of takes before it even looks at the gap.
            if (RepeatsLeft == 0)
            {
                Dead = true;
                return;
            }

            if (CycleAge < Duration + (double)_gap)
            {
                // 10109c74: both colours to nothing. The control stays alive through the gap.
                Blanked = true;
                Current = Current.Blank();
                return;
            }

            // 10109ca5: one take spent. Stock decrements the field itself.
            if (--RepeatsLeft == 0)
            {
                Dead = true;
                return;
            }

            CycleAge = 0f;
            if (_spin == RandomSpinAngle)
                RandomAngle = (float)(Next() * (double)RandomAngleScale);
        }

        // 10109ce9: stock takes the age modulo the duration. The repeat machine above has already
        // brought the age back inside the take, so it only ever hands back the age itself.
        float t = Duration != 0f ? CycleAge % Duration : CycleAge;

        float length = _length;

        if (FollowsLocator)
        {
            // 10109d21: a locator that no longer resolves stops the control.
            if (!locatorValid)
            {
                Dead = true;
                return;
            }

            AnchorX = lx;
            AnchorY = ly;
            AnchorZ = lz;

            if ((Flags & FlagGroundSnap) != 0)
            {
                float ground = GroundHeight != null ? GroundHeight(AnchorX, AnchorZ) : AnchorY;
                if ((Flags & FlagGroundRateLimit) != 0)
                {
                    // 10109d6b: how far it would climb this call, or nothing if that is backwards or
                    // further than the beam is long.
                    float climb = (float)(AnchorY - (double)ground);
                    length = climb < 0f || _length < climb ? 0f : climb;
                }
                AnchorY = ground;
            }
        }

        // 10109db4: with field 10 at zero the anchor falls. With the locator followed it is refilled
        // every call, so only a record that does not follow one (71862) accumulates the drop.
        if (_fallGate == 0)
        {
            AnchorY = (float)(FallVelocity * (double)dt + AnchorY);
            FallVelocity = (float)(FallAcceleration * (double)dt + FallVelocity);
        }

        // 10109d03: the hold pair and the hold colours are what both fades start from.
        var state = new State
        {
            Length = (float)(Scale * (double)length),
            Foot = _footHold,
            Tip = _tipHold,
            Colour1 = _c1Hold,
            Colour2 = _c2Hold,
            AnchorX = AnchorX,
            AnchorY = AnchorY,
            AnchorZ = AnchorZ,
        };

        float fadeOutStart = (float)(Duration - (double)_fadeOut);

        if (t < _fadeIn)
        {
            float u = (float)(t / (double)_fadeIn);
            state.Foot = Mix(_footStart, _footHold, u);
            state.Tip = Mix(_tipStart, _tipHold, u);
            state.Colour1 = MixColour(_c1Start, _c1Hold, u);
            state.Colour2 = MixColour(_c2Start, _c2Hold, u);
        }
        else if (fadeOutStart <= t)
        {
            float u = (float)((t - (double)fadeOutStart) / _fadeOut);
            state.Foot = Mix(_footHold, _footEnd, u);
            state.Tip = Mix(_tipHold, _tipEnd, u);
            state.Colour1 = MixColour(_c1Hold, _c1End, u);
            state.Colour2 = MixColour(_c2Hold, _c2End, u);
        }

        if ((Flags & FlagWobble) != 0)
        {
            // 10109fe2: a sine that travels as the take ages, then a fresh jitter — in that order, and
            // the foot's pair before the tip's, which is the order the randoms are drawn in.
            state.Foot = (float)(Math.Sin(Swing(_footRate, t, _footPhase)) * _footSwing + state.Foot);
            state.Foot = (float)(Next() * (double)_footJitter + state.Foot);
            state.Tip = (float)(Math.Sin(Swing(_tipRate, t, _tipPhase)) * _tipSwing + state.Tip);
            state.Tip = (float)(Next() * (double)_tipJitter + state.Tip);
        }

        // 1010a0fb: a field 30 of 999 holds the angle picked for this take; otherwise it turns.
        state.Angle = _spin == RandomSpinAngle ? RandomAngle : (float)(_spin * (double)t);

        Current = state;
    }

    /// <summary><c>10109fe8</c>: 2 · pi · rate · t + phase.</summary>
    static double Swing(float rate, float t, float phase)
    {
        double x = rate * (double)t;
        return (x + x) * Math.PI + phase;
    }

    /// <summary><c>10109e1f</c>: to · u + from · (1 - u), the whole thing in one x87 expression.</summary>
    static float Mix(float from, float to, float u) => (float)(to * (double)u + from * (1.0 - u));

    /// <summary>
    /// randy31's <c>Color_t::operator*</c> (<c>10019c93</c>) then <c>operator+</c> (<c>10019bb1</c>):
    /// each of the four bytes scaled with a half added and truncated, clamped, then added and clamped
    /// again. The scale reaches the multiply as a float, so the weight is rounded before it is used.
    /// </summary>
    public static uint MixColour(uint from, uint to, float u)
    {
        float v = (float)(1.0 - u);
        uint result = 0;
        for (int shift = 0; shift < 32; shift += 8)
        {
            int sum = Channel((from >> shift) & 0xff, v) + Channel((to >> shift) & 0xff, u);
            if (sum < 0)
                sum = 0;
            if (sum > 255)
                sum = 255;
            result |= (uint)sum << shift;
        }
        return result;
    }

    static int Channel(uint value, float scale)
    {
        int c = (int)(value * (double)scale + 0.5);
        if (c < 0)
            c = 0;
        return c > 255 ? 255 : c;
    }

    float Next() => _random != null ? _random() : 0f;

    static float F(float[] f, int i) => f != null && i >= 0 && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i)
        => f != null && i >= 0 && i < f.Length ? BitConverter.SingleToInt32Bits(f[i]) : 0;

    static uint UInt(float[] f, int i) => unchecked((uint)Int(f, i));

    /// <summary>One call's worth of what stock hands the visual.</summary>
    public struct State
    {
        public float Length;
        public float Foot;
        public float Tip;
        public uint Colour1;
        public uint Colour2;
        public float Angle;
        public float AnchorX, AnchorY, AnchorZ;

        /// <summary><c>10109c7a</c>: the gap zeroes both colours and leaves everything else alone.</summary>
        public State Blank()
        {
            State copy = this;
            copy.Colour1 = 0;
            copy.Colour2 = 0;
            return copy;
        }
    }

    /// <summary>
    /// Port-only: the drawn state between two fixed steps, so the beam is as smooth as the frame rate
    /// allows while its timing stays stock's.
    /// </summary>
    public static State Blend(State a, State b, float t) => new State
    {
        Length = a.Length + (b.Length - a.Length) * t,
        Foot = a.Foot + (b.Foot - a.Foot) * t,
        Tip = a.Tip + (b.Tip - a.Tip) * t,
        Colour1 = MixColour(a.Colour1, b.Colour1, t),
        Colour2 = MixColour(a.Colour2, b.Colour2, t),
        Angle = a.Angle + (b.Angle - a.Angle) * t,
        AnchorX = a.AnchorX + (b.AnchorX - a.AnchorX) * t,
        AnchorY = a.AnchorY + (b.AnchorY - a.AnchorY) * t,
        AnchorZ = a.AnchorZ + (b.AnchorZ - a.AnchorZ) * t,
    };
}
