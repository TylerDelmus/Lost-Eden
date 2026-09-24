using System;

/// <summary>
/// Stock <c>_GfxControlSprite_t</c> (typeCode 1012, 0x3f4; vftable <c>Gamecode 1016dd54</c>, loader
/// <c>100f6369</c>, init <c>100f6483</c> / <c>100f64a7</c>, Process <c>100f6d2b</c>): one quad on the
/// locator, a DisplaySystem <c>GfxVisualSprite2</c> (camera-facing, ctor <c>10023c6a</c>, draw
/// <c>10023ea4</c>) or <c>GfxVisualSprite3</c> (turned with its frame, ctor <c>100283ec</c>, draw
/// <c>10028606</c>), cycling a size, colour and frame over a period. E.g. 43607, the buff of 19 Sanctifier
/// and Reaper nanos, 80006-80009 (the halos) and 61085-61087 (the faction beams).
///
/// Fields: 0 the locator's flags, 8 duration (the base's), 9 material, 10 the sprite's flags, 11/12 width
/// from/to, 13/14 height from/to, 15-18 start colour A,R,G,B, 19-22 stop colour, 23 period (0 counts as 1),
/// 24 repeats (negative: forever), 25/26 pulse amplitude and rate (Hz).
/// Sprite flags: bits 0-1 the visual (0 Sprite2, 3 Sprite3, else none and the control is ready at once);
/// 4 alpha blend instead of additive; 8 Sprite3 takes the locator's turn; 0x10 follow the locator; 0x20
/// the frame runs first → last over each period; 0x40 repeat (field 24 times); 0x80 colour start → stop;
/// 0x100 size from → to; 0x800 a pulse added to both sizes; 0x1000 no blending; 0x2000 Sprite3 turns about
/// y to face the camera.
///
/// Per call: p = age / period, n = _ftol(p), f = p - n. Past the last period (n &gt; field 24 with 0x40,
/// else n &gt; 0) it is ready. Otherwise with 0x20 the frame is _ftol((last - first) f + first); with 0x80
/// the colour is each channel's (stop - start) f + start packed as fistp(c 255 - 0.49999); with 0x100 the
/// size is (to - from) f + from, plus sin(field 26 · age · 2pi) · field 25 with 0x800.
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class SpriteSim
{
    public const int FlagAlphaBlend = 4;
    public const int FlagTurn = 8;
    public const int FlagFollow = 0x10;
    public const int FlagFrames = 0x20;
    public const int FlagRepeat = 0x40;
    public const int FlagColour = 0x80;
    public const int FlagSize = 0x100;
    public const int FlagPulse = 0x800;
    public const int FlagOpaque = 0x1000;
    public const int FlagFaceCamera = 0x2000;

    // 1015f810.
    const double TwoPi = 6.2831854820251465;

    public enum Kind { None, Sprite2, Sprite3 }

    readonly float _w0, _w1, _h0, _h1, _pulseAmp, _pulseHz;
    readonly int _repeats;

    public int LocatorFlags { get; }
    public float Duration { get; }
    public int Material { get; }
    public int Flags { get; }
    public Kind Visual { get; }
    public float Period { get; }

    /// <summary>+0x54..+0x60 and +0x64..+0x70: A, R, G, B in 0..1.</summary>
    public readonly float[] Start = new float[4];
    public readonly float[] Stop = new float[4];

    // What the visual shows (its +0x1a8 / +0x1ac, +0x18c and +0x180).
    public float Width { get; private set; }
    public float Height { get; private set; }
    public uint Argb { get; private set; }
    public int Frame { get; private set; }

    public int FirstFrame { get; }
    public int LastFrame { get; }

    public bool Additive => (Flags & FlagAlphaBlend) == 0;
    public bool Blended => (Flags & FlagOpaque) == 0;

    /// <param name="firstFrame">The material's first frame (<c>100cdfa1</c>).</param>
    /// <param name="lastFrame">The material's last frame (<c>100cdfb7</c>).</param>
    public SpriteSim(float[] fields, int firstFrame, int lastFrame)
    {
        LocatorFlags = Int(fields, 0);
        Duration = F(fields, 8);
        Material = Int(fields, 9);
        Flags = Int(fields, 10);
        _w0 = F(fields, 11);
        _w1 = F(fields, 12);
        _h0 = F(fields, 13);
        _h1 = F(fields, 14);
        for (int i = 0; i < 4; i++)
        {
            Start[i] = F(fields, 15 + i);
            Stop[i] = F(fields, 19 + i);
        }
        float period = F(fields, 23);
        Period = period == 0f ? 1f : period;
        _repeats = Int(fields, 24);
        _pulseAmp = F(fields, 25);
        _pulseHz = F(fields, 26);

        // 100f6483: flags & 3 picks the visual.
        int mode = Flags & 3;
        Visual = mode == 0 ? Kind.Sprite2 : mode == 3 ? Kind.Sprite3 : Kind.None;

        FirstFrame = firstFrame;
        LastFrame = lastFrame;
        // 100f64a7: the visual starts at the ctor's size, the start colour and the first frame.
        Width = _w0;
        Height = _h0;
        Argb = Pack(Start);
        Frame = firstFrame;
    }

    /// <summary>Slot 11 (<c>100f5fa3</c>): the start colour, shown at once.</summary>
    public void SetStart(float a, float r, float g, float b)
    {
        Start[0] = a; Start[1] = r; Start[2] = g; Start[3] = b;
        Argb = Pack(Start);
    }

    /// <summary>Slot 12 (<c>100f60e3</c>): the stop colour.</summary>
    public void SetStop(float a, float r, float g, float b)
    {
        Stop[0] = a; Stop[1] = r; Stop[2] = g; Stop[3] = b;
    }

    /// <summary>One Process call's body (<c>100f6db6</c>..): false once the last period is over.</summary>
    public bool Step(float age)
    {
        float p = (float)(age / (double)Period);
        int n = Ftol(p);
        float f = (float)(p - (double)n);

        if ((Flags & FlagRepeat) != 0)
        {
            if (_repeats >= 0 && n > _repeats)
                return false;
        }
        else if (n > 0)
            return false;

        if ((Flags & FlagFrames) != 0)
            Frame = Ftol(((double)LastFrame - FirstFrame) * f + FirstFrame);

        if ((Flags & FlagColour) != 0)
        {
            // 100f6fd0: ((stop - start) f + start) · 255 stored as a float, then fistp(x - 0.49999).
            int a = Lerp(0, f), r = Lerp(1, f), g = Lerp(2, f), b = Lerp(3, f);
            Argb = (uint)((((a << 8) | r) << 8 | g) << 8 | b);
        }

        if ((Flags & FlagSize) != 0)
        {
            float w = (float)(((double)_w1 - _w0) * f + _w0);
            float h = (float)(((double)_h1 - _h0) * f + _h0);
            if ((Flags & FlagPulse) != 0)
            {
                float angle = (float)(_pulseHz * (age * TwoPi));
                float s = (float)((float)Math.Sin(angle) * (double)_pulseAmp);
                w = (float)(s + (double)w);
                h = (float)(s + (double)h);
            }
            Width = w;
            Height = h;
        }
        return true;
    }

    /// <summary>A, R, G, B shifted in unmasked, each fistp(c · 255 - 0.49999) (round to even).</summary>
    static uint Pack(float[] c)
        => (uint)((((Channel(c[0]) << 8) | Channel(c[1])) << 8 | Channel(c[2])) << 8 | Channel(c[3]));

    int Lerp(int i, float f)
        => (int)Math.Round((float)((((double)Stop[i] - Start[i]) * f + Start[i]) * 255.0) - 0.49999, MidpointRounding.ToEven);

    static int Channel(float c) => (int)Math.Round((float)(c * 255.0) - 0.49999, MidpointRounding.ToEven);

    static int Ftol(double v) => double.IsNaN(v) ? int.MinValue : (int)v;

    static float F(float[] f, int i) => f != null && i >= 0 && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i >= 0 && i < f.Length ? GfxBits.Of(f, i) : 0;
}
