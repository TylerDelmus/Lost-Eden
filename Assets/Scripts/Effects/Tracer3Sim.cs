using System;

/// <summary>
/// Stock <c>_GfxControlTracer3_t</c> (typeCode 1022, 0x3fe; vftable <c>Gamecode 1016dfec</c>, hit-location
/// ctor <c>100ff996</c>, loader <c>100ff657</c>, init <c>100ff7f3</c>, child build <c>100ff6e2</c>,
/// Process <c>100ff91b</c> / step <c>100ff754</c>): it carries a child effect from the hit location's
/// start to its end, e.g. 2662, the tracer of 28609 Freezing Surge, whose child is a Nano0 trail.
///
/// Fields: 8 duration (the base's), 9 unused here, 10 speed (+0x38), 11-14 the child's colour A,R,G,B
/// (+0x3c..+0x48, also packed into +0x50), 15 the child effect (+0x4c).
///
/// Init: start and end are read once. The direction is end - start; its length L (+0x78) under
/// 0.01 readies the tracer. Otherwise the direction is set to length 1 and the speed becomes
/// min(field 10, 5 L), so a flight takes at least 0.2 s. The child is created at the start
/// (<c>100cea4b</c>, by position) and given start colour fields 11-14 and stop colour (0, 12, 13, 14).
///
/// Each Process at age a: d = speed * a, capped at L, which readies the tracer; the child is moved to
/// start + dir * d (slot 4) and processed. Deleting the tracer deletes the child, so its trail goes when
/// it arrives. Slot 6 readies it at once; slot 8 (SetDuration) does nothing; slot 15 sets the speed.
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class Tracer3Sim
{
    /// <summary>100ff8c8: a line shorter than this is ready at once.</summary>
    const float MinLength = 0.009999999776482582f;

    readonly float _sx, _sy, _sz;
    readonly float _dx, _dy, _dz;

    public int ChildId { get; }

    /// <summary>Fields 11-14 (+0x3c..+0x48), A,R,G,B.</summary>
    public float A { get; }
    public float R { get; }
    public float G { get; }
    public float B { get; }

    /// <summary>+0x50: fields 11-14 packed, each fistp(c * 255 - 0.49999).</summary>
    public uint Argb { get; }

    /// <summary>+0x78.</summary>
    public float Length { get; }

    /// <summary>+0x38 after init: min(field 10, 5 L).</summary>
    public float Speed { get; set; }

    /// <summary>The line was too short: stock sets the ready flag in init.</summary>
    public bool ReadyAtStart { get; }

    public Tracer3Sim(float[] fields, float sx, float sy, float sz, float ex, float ey, float ez)
    {
        Speed = F(fields, 10);
        A = F(fields, 11);
        R = F(fields, 12);
        G = F(fields, 13);
        B = F(fields, 14);
        ChildId = Int(fields, 15);
        Argb = (uint)((((Channel(A) << 8) | Channel(R)) << 8 | Channel(G)) << 8 | Channel(B));

        _sx = sx;
        _sy = sy;
        _sz = sz;
        // 100ff89d: end - start; 10023bfd / 1013f4f0 its length.
        float dx = ex - sx, dy = ey - sy, dz = ez - sz;
        Length = (float)Math.Sqrt((float)(dx * dx + dy * dy + dz * dz));
        if (!(MinLength < Length || MinLength == Length))
        {
            ReadyAtStart = true;
            return;
        }

        float scale = (float)(1.0 / Length);
        _dx = dx * scale;
        _dy = dy * scale;
        _dz = dz * scale;

        // 100ff8ec: never slower than the line's length in 0.2 s.
        float atLeast = (float)(Length * 5.0);
        if (atLeast < Speed)
            Speed = atLeast;
    }

    /// <summary>
    /// The step (<c>100ff754</c>) at <paramref name="age"/>: where the child goes, and whether it has
    /// arrived (which readies the tracer).
    /// </summary>
    public bool Position(float age, out float x, out float y, out float z)
    {
        float d = (float)((double)Speed * age);
        bool arrived = false;
        if (Length < d || Length == d)
        {
            d = Length;
            arrived = true;
        }
        x = _dx * d + _sx;
        y = _dy * d + _sy;
        z = _dz * d + _sz;
        return arrived;
    }

    /// <summary>100ff803..100ff869: fistp(c * 255 - 0.49999), round to even, shifted in unmasked.</summary>
    static int Channel(float c)
    {
        float scaled = (float)(c * 255.0);
        return (int)Math.Round(scaled - 0.49999, MidpointRounding.ToEven);
    }

    static float F(float[] f, int i) => f != null && i >= 0 && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i >= 0 && i < f.Length ? GfxBits.Of(f, i) : 0;
}
