using Xunit;

/// <summary>
/// Locks <see cref="StarsCase3"/> to stock starType 3 (Gamecode FUN_100f8491, case at 100f89dd),
/// using the values of 43168 — the only child of nano cast effect 47299.
/// 43168: field 18-21 = 1, .45, .4, .5; 22-25 = .4, .961, .753, .227; 28 = 2.4; 29 = 1.3; 30 = 900 (int).
/// </summary>
public class StarsCase3Tests
{
    static readonly float[] Start43168 = { 1f, 0.45f, 0.4f, 0.5f };
    static readonly float[] End43168 = { 0.4f, 0.961f, 0.753f, 0.227f };
    const float Life43168 = 0.9f;
    const float Curve43168 = 2.4f;
    const float Radius43168 = 1.3f;
    const float Step = 1f / 30f;

    static StarsCase3 Make43168(System.Func<int> rand)
        => new StarsCase3(Life43168, Curve43168, Radius43168, Start43168, End43168, rand);

    static StarsCase3 Make43168(uint seed = 1)
        => Make43168(new StarsCase3.MsvcRand(seed).Next);

    /// <summary>rand() value that GetRandomPointInSphere turns into <paramref name="axis"/>.</summary>
    static int RandFor(float axis) => (int)((axis + 1f) * 16384f);

    [Fact]
    public void MsvcRand_MatchesCrtSequenceForSeedOne()
    {
        var r = new StarsCase3.MsvcRand(1);
        Assert.Equal(41, r.Next());
        Assert.Equal(18467, r.Next());
        Assert.Equal(6334, r.Next());
        Assert.Equal(26500, r.Next());
    }

    [Fact]
    public void RandomPoint_QuantisesLikeStockAndRejectsTheShell()
    {
        // First triple lies outside the ball (x = y = z = 0.75) and must be redrawn.
        int[] seq = { RandFor(0.75f), RandFor(0.75f), RandFor(0.75f), RandFor(0.5f), RandFor(-0.25f), RandFor(0f) };
        int i = 0;
        StarsCase3.RandomPointInUnitBall(() => seq[i++], out float x, out float y, out float z);
        Assert.Equal(6, i);
        Assert.Equal(0.5f, x);
        Assert.Equal(-0.25f, y);
        Assert.Equal(0f, z);
    }

    [Fact]
    public void FrameIndex_TruncatesLikeFtol()
    {
        // (timer - age) * 16 / life: 16 -> -1 -> clamp 0; 15.5 -> 15 -> 0; 14.99 -> 14 -> 1; 0.5 -> 0 -> 15.
        Assert.Equal(0, StarsCase3.FrameIndex(Life43168, 0f, Life43168));
        Assert.Equal(0, StarsCase3.FrameIndex(Life43168 * 15.5f / 16f, 0f, Life43168));
        Assert.Equal(1, StarsCase3.FrameIndex(Life43168 * 14.99f / 16f, 0f, Life43168));
        Assert.Equal(15, StarsCase3.FrameIndex(Life43168 * 0.5f / 16f, 0f, Life43168));
        // Rounding (the earlier port) would have given 14 here, not 15.
        Assert.Equal(15, StarsCase3.FrameIndex(Life43168 * 0.9f / 16f, 0f, Life43168));
    }

    [Fact]
    public void Size_FollowsStockCurveWithoutAFloor()
    {
        Assert.Equal(0.48f, StarsCase3.Size(0f, Curve43168), 5);
        Assert.Equal(1.26f, StarsCase3.Size(0.5f, Curve43168), 5);
        Assert.Equal(0f, StarsCase3.Size(1f, Curve43168), 5);
    }

    [Fact]
    public void PackArgb_TruncatesEachChannelOfTheLinearRamp()
    {
        StarsCase3 s = Make43168();
        // t=0: 1*255=255, .45*255=114.75, .4*255=102.0000015, .5*255=127.5
        Assert.Equal(0xFF72667Fu, s.PackArgb(0f));
        // t=1 alpha: stock stores delta = .4f - 1 as a float (FSTP [ESI+0x24]); that is an exact tie and
        // rounds to -0.600000024, so 1 + delta = 0.399999976 -> 101.99999 -> 101, not 102.
        // R .961*255=245.05, G .753*255=192.01, B .227*255=57.88.
        Assert.Equal(0x65F5C039u, s.PackArgb(1f));
    }

    [Fact]
    public void FirstCall_OpensTwoSlotsAndDrawsNothingYet()
    {
        StarsCase3 s = Make43168();
        s.Step(0f, 0f, 0f, 0f);

        Assert.Equal(2, s.LastCount);
        foreach (StarsCase3.Sprite sprite in s.Sprites)
            Assert.False(sprite.Visible); // the spawn call does not write the sprite record

        s.Step(Step, 0f, 0f, 0f);
        Assert.Equal(4, s.LastCount); // slots 0,1 alive + slots 2,3 opened
        Assert.True(s.Sprites[0].Visible);
        Assert.True(s.Sprites[1].Visible);
        Assert.False(s.Sprites[2].Visible);
    }

    [Fact]
    public void Spring_IntegratesVelocityThenPosition_OnXZOnlyScaledSpawn()
    {
        // One slot's worth of rand: offset (0.5, 0.25, 0) -> x scaled by 1.3 = 0.65, y stays 0.25.
        int[] seq = { RandFor(0.5f), RandFor(0.25f), RandFor(0f) };
        int i = 0;
        StarsCase3 s = Make43168(() => seq[i++ % seq.Length]);

        s.Step(0f, 10f, 20f, 30f);
        s.Step(Step, 10f, 20f, 30f);

        // Spawn: pos = origin + (0.65, 0.25, 0); vel = (z, 0, -x) = (0, 0, -0.65).
        // Next call: vel += (origin - pos) * 0.1 -> (-0.065, -0.025, -0.65); pos += vel * 0.1.
        StarsCase3.Sprite p = s.Sprites[0];
        Assert.Equal(10.65f - 0.0065f, p.X, 4);
        Assert.Equal(20.25f - 0.0025f, p.Y, 4);
        Assert.Equal(30f - 0.065f, p.Z, 4);
    }

    [Fact]
    public void Blend_DrawsBetweenThePreviousAndLastStep()
    {
        StarsCase3 s = Make43168();
        s.Step(0f, 0f, 0f, 0f);
        s.Step(Step, 0f, 0f, 0f);
        StarsCase3.Sprite before = s.Sprites[0];
        s.Step(2 * Step, 0f, 0f, 0f);
        StarsCase3.Sprite after = s.Sprites[0];
        Assert.NotEqual(before.X, after.X);

        StarsCase3.Sprite at0 = s.Blend(0, 0f), half = s.Blend(0, 0.5f), at1 = s.Blend(0, 1f);
        Assert.Equal(before.X, at0.X, 6);
        Assert.Equal(before.Size, at0.Size, 6);
        Assert.Equal((before.Z + after.Z) * 0.5f, half.Z, 6);
        Assert.Equal(after.X, at1.X, 6);
        Assert.Equal(after.Argb, half.Argb); // colour is the last step's
    }

    [Fact]
    public void Blend_DoesNotSweepARecycledSlotFromItsOldParticle()
    {
        StarsCase3 s = Make43168();
        float age = 0f;
        s.Step(age, 0f, 0f, 0f);
        age += Step;
        s.Step(age, 0f, 0f, 0f);
        int first = s.Sprites[0].Serial;
        Assert.NotEqual(0, first);

        // Run until slot 0's next particle writes the record for the first time.
        StarsCase3.Sprite old = s.Sprites[0];
        for (int n = 0; n < 60 && s.Sprites[0].Serial == first; n++)
        {
            old = s.Sprites[0];
            age += Step;
            s.Step(age, 0f, 0f, 0f);
        }
        StarsCase3.Sprite fresh = s.Sprites[0];
        Assert.NotEqual(first, fresh.Serial);
        Assert.NotEqual(old.X, fresh.X);

        StarsCase3.Sprite drawn = s.Blend(0, 0f);
        Assert.Equal(fresh.X, drawn.X);
        Assert.Equal(fresh.Y, drawn.Y);
        Assert.Equal(fresh.Z, drawn.Z);
    }

    [Fact]
    public void SteadyState_At30Hz_HoldsAbout54Particles()
    {
        StarsCase3 s = Make43168();
        float age = 0f;
        s.Step(age, 0f, 0f, 0f);
        for (int n = 0; n < 90; n++)
        {
            age += Step;
            s.Step(age, 0f, 0f, 0f);
        }

        // Each slot lives ~27 calls (0.9 s at 30 Hz) and two open per call.
        Assert.InRange(s.LastCount, 52, 56);
        int visible = 0;
        foreach (StarsCase3.Sprite sprite in s.Sprites)
            if (sprite.Visible)
                visible++;
        Assert.InRange(visible, 50, 56);
    }

    [Fact]
    public void Terminating_StopsSpawningAndDrainsWithinOneLife()
    {
        StarsCase3 s = Make43168();
        float age = 0f;
        s.Step(age, 0f, 0f, 0f);
        for (int n = 0; n < 60; n++)
        {
            age += Step;
            s.Step(age, 0f, 0f, 0f);
        }

        s.Terminating = true;
        int calls = 0;
        while (!s.Drained && calls < 100)
        {
            age += Step;
            s.Step(age, 0f, 0f, 0f);
            calls++;
        }

        Assert.True(s.Drained);
        Assert.InRange(calls, 26, 28); // the youngest particle's remaining life
    }

    [Fact]
    public void Particles_StayNearTheOrigin()
    {
        StarsCase3 s = Make43168();
        float age = 0f;
        s.Step(age, 0f, 0f, 0f);
        for (int n = 0; n < 120; n++)
        {
            age += Step;
            s.Step(age, 0f, 0f, 0f);
            foreach (StarsCase3.Sprite sprite in s.Sprites)
            {
                if (!sprite.Visible)
                    continue;
                float r = System.MathF.Sqrt(sprite.X * sprite.X + sprite.Y * sprite.Y + sprite.Z * sprite.Z);
                Assert.True(r < 2.5f, $"particle drifted to r={r}");
            }
        }
    }

    [Fact]
    public void TakeFixedSteps_CarriesRemainderAndCapsStalls()
    {
        float carry = 0f;
        Assert.Equal(0, EffectFrameRate.TakeFixedSteps(ref carry, 0.02f, Step, 8));
        Assert.Equal(1, EffectFrameRate.TakeFixedSteps(ref carry, 0.02f, Step, 8));
        Assert.InRange(carry, 0.0066f, 0.0068f);

        // A 2 s hitch runs the cap and drops the rest instead of carrying it.
        Assert.Equal(8, EffectFrameRate.TakeFixedSteps(ref carry, 2f, Step, 8));
        Assert.Equal(0f, carry);
    }
}
