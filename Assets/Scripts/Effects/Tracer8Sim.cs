using System;

/// <summary>
/// Stock <c>GfxControlTracer8_t</c> (type 3026, 0xbd2; vftable <c>Gamecode 1016fa9c</c>, object 0xc0,
/// point ctor <c>10114e83</c>, dynel ctor <c>10114f29</c>, loader <c>10114c68</c>, init <c>10114d6d</c>,
/// build <c>10114ce4</c>, Process <c>10114b73</c>, teardown <c>10114b42</c>).
///
/// It is the fourth arm of the tracer factory (<c>100d18eb</c>: <c>sub ecx,0x401</c> then three
/// decrements and <c>sub ecx,0x7cf</c>), so it is built from a hit location like Tracer5, 6 and 7. It
/// draws nothing itself: it carries **one whole child effect** (field 11) from the start of the flight
/// line to the end, and when it goes away it fires a second effect (field 14) back at the start.
///
/// Fields: 0 flags, 8 duration, 10 (loaded to +0x38, never read), 11 the child effect id, 12 the
/// flight speed, 13 (loaded to +0x40, never read), 14 the effect fired on teardown. Fields 1-7 are the
/// locator template.
///
/// Per call: <c>d = speed * age</c>, capped at the line's length — reaching it ends the tracer. The
/// child sits at <c>from + dir * d</c> and is turned to face along the line, then processed. Stock hands
/// it the tracer's own ref frame (<c>10114c40</c> calls the child's slot 5, a straight 64-byte copy of
/// the matrix), so the child inherits the turn as well as the position.
///
/// <c>+0xb8</c> is cleared by the init and never set, so the <c>+0xbc</c> timeout the Process checks
/// (<c>10114b95</c>) is dead code; the flight's own end is what stops it.
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class Tracer8Sim
{
    /// <summary>A line this short readies the tracer at once (10114ddd).</summary>
    public const float MinLength = 0.01f;

    /// <summary>10114e0a: the speed can never carry the flight across in under a fifth of a second.</summary>
    public const float SpeedCapPerLength = 5f;

    public readonly int Flags;
    public readonly float Duration;
    public readonly int ChildId;
    public readonly int EndEffectId;
    public readonly float Speed;
    public readonly float Length;
    public readonly float FromX, FromY, FromZ;
    public readonly float DirX, DirY, DirZ;

    /// <summary>Fields 10 and 13: the loader reads them (+0x38, +0x40) and nothing ever looks again.</summary>
    public readonly int UnusedField10;
    public readonly float UnusedField13;

    /// <summary>10114ded: under <see cref="MinLength"/> the control is done before it starts.</summary>
    public readonly bool ReadyAtStart;

    public Tracer8Sim(float[] fields, float fromX, float fromY, float fromZ, float toX, float toY, float toZ)
    {
        Flags = GfxBits.Of(fields, 0);
        Duration = F(fields, 8);
        UnusedField10 = GfxBits.Of(fields, 10);
        ChildId = GfxBits.Of(fields, 11);
        UnusedField13 = F(fields, 13);
        EndEffectId = GfxBits.Of(fields, 14);

        FromX = fromX;
        FromY = fromY;
        FromZ = fromZ;

        // 10114daf..10114dd7: the direction is to - from, its length kept before it is normalised.
        float dx = toX - fromX, dy = toY - fromY, dz = toZ - fromZ;
        Length = (float)Math.Sqrt((double)dx * dx + (double)dy * dy + (double)dz * dz);

        // 10114ddd: stock's compare skips the ready when the length is greater than, equal to or
        // unordered with 0.01, so a NaN length flies rather than stopping.
        ReadyAtStart = MinLength > Length;

        float speed = F(fields, 12);
        if (!ReadyAtStart)
        {
            float inv = Length != 0f ? 1f / Length : 0f;
            DirX = dx * inv;
            DirY = dy * inv;
            DirZ = dz * inv;

            // 10114e13: only ever downwards, so a record cannot outrun its own line.
            float cap = Length * SpeedCapPerLength;
            if (speed > cap)
                speed = cap;
        }
        Speed = speed;
    }

    /// <summary>
    /// 10114bb9..10114c04: where the child is at this age, and whether the flight has landed. The
    /// distance is clamped to the line's length on the same call that ends it, so the last frame draws
    /// the child at the target rather than past it.
    /// </summary>
    public bool Position(float age, out float x, out float y, out float z)
    {
        float distance = Speed * age;
        // 10114bd1's jp keeps flying when the two are unordered, which `<=` matches and `!(>)` would not.
        bool landed = Length <= distance;
        if (landed)
            distance = Length;

        x = FromX + DirX * distance;
        y = FromY + DirY * distance;
        z = FromZ + DirZ * distance;
        return landed;
    }

    static float F(float[] f, int i) => f != null && i >= 0 && i < f.Length ? f[i] : 0f;
}
