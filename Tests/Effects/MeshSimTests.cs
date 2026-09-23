using System;
using Xunit;

/// <summary>
/// Locks <see cref="MeshSim"/> to stock <c>_GfxControlMesh_t</c> (<c>Gamecode 100e52f3</c>), using
/// the shipped records 60003 and 61041-61044.
/// </summary>
public class MeshSimTests
{
    static float I(int v) => BitConverter.Int32BitsToSingle(v);

    /// <summary>Record 61043: flags 0x303, 300 s, model 2, fades 0.2 / 1, no lift, no turn.</summary>
    static float[] R61043(int model = 2, int flags = 0x303, float duration = 300f)
    {
        var f = new float[14];
        f[0] = I(flags);
        f[8] = duration;
        f[9] = I(model);
        f[10] = 0.2f;       // fade in
        f[11] = 1f;         // fade out
        f[12] = 0f;         // lift
        f[13] = 0f;         // turn
        return f;
    }

    static MeshSim Make(float[] f = null) => new MeshSim(f ?? R61043());

    [Fact]
    public void FieldNinePicksOneOfFourTowerWrecks()
    {
        // 100e53fb: the four literals at 1016d288, 1016d2ac, 1016d2d4 and 1016d2f8.
        Assert.Equal("tower_destroyed_buff&debuff.abiff", Make(R61043(0)).ModelName);
        Assert.Equal("tower_destroyed_buff&debuff_LL.abiff", Make(R61043(1)).ModelName);
        Assert.Equal("tower_destroyed_controller.abiff", Make(R61043(2)).ModelName);
        Assert.Equal("tower_destroyed_guard.abiff", Make(R61043(3)).ModelName);
    }

    [Fact]
    public void AFieldNineOutsideTheFourLeavesItWithNothingToDraw()
    {
        // 100e540c falls out of the switch, so there is no visual and 100e5316 kills it.
        MeshSim none = Make(R61043(4));
        Assert.Null(none.ModelName);
        Assert.True(none.Dead);
    }

    [Fact]
    public void TheLoaderStoresTheReciprocalsOfBothFades()
    {
        // 100e5567.
        MeshSim m = Make();
        Assert.Equal(1f / 0.2f, m.InvFadeIn, 4);
        Assert.Equal(1f / 1f, m.InvFadeOut, 4);
        Assert.Equal(300f - 1f, m.FadeOutStart, 4);
    }

    [Fact]
    public void ItStartsInvisible()
    {
        // 100e5561.
        Assert.Equal(0f, Make().Alpha, 5);
    }

    [Fact]
    public void TheAlphaRunsUpThenHoldsThenDown()
    {
        // 100e5325: age * 1/fadeIn, then 1, then 1 - (age - start) * 1/fadeOut.
        MeshSim m = Make();
        m.Step(0.1f, true, 0f, 0f, 0f);
        Assert.Equal(0.5f, m.Alpha, 4);
        m.Step(50f, true, 0f, 0f, 0f);
        Assert.Equal(1f, m.Alpha, 4);
        m.Step(299.5f, true, 0f, 0f, 0f);
        Assert.Equal(0.5f, m.Alpha, 4);
        m.Step(300f, true, 0f, 0f, 0f);
        Assert.Equal(0f, m.Alpha, 4);
    }

    [Fact]
    public void ItTurnsAtFieldThirteenRadiansASecond()
    {
        float[] f = R61043();
        f[13] = 0.25f;
        MeshSim m = Make(f);
        m.Step(4f, true, 0f, 0f, 0f);
        Assert.Equal(1f, m.Angle, 4);
    }

    [Fact]
    public void ItStandsOnTheLocatorLiftedByFieldTwelve()
    {
        float[] f = R61043(flags: 0x003);   // neither the snap nor the outlive bit
        f[12] = 1.5f;
        MeshSim m = Make(f);
        m.Step(1f, true, 3f, 10f, -2f);
        Assert.Equal(3f, m.X, 4);
        Assert.Equal(11.5f, m.Y, 4);
        Assert.Equal(-2f, m.Z, 4);
    }

    [Fact]
    public void FlagOneHundredStandsItOnTheGroundInstead()
    {
        // 100e5382.
        float[] f = R61043(flags: 0x103);
        f[12] = 0.5f;
        MeshSim m = Make(f);
        m.GroundHeight = (x, z) => -4f;
        m.Step(1f, true, 0f, 100f, 0f);
        Assert.Equal(-3.5f, m.Y, 4);
    }

    [Fact]
    public void TheLiftIsNotAddedTwiceOverSuccessiveCalls()
    {
        // Stock reads the locator every call, so the height is worked out afresh rather than
        // accumulated onto what the last call left.
        float[] f = R61043();
        f[12] = 2f;
        MeshSim m = Make(f);
        m.Step(1f, true, 0f, 5f, 0f);
        Assert.Equal(7f, m.Y, 4);
        m.Step(2f, true, 0f, 5f, 0f);
        Assert.Equal(7f, m.Y, 4);
        m.Step(3f, true, 0f, 5f, 0f);
        Assert.Equal(7f, m.Y, 4);
    }

    [Fact]
    public void ADeadLocatorEndsItUnlessFlagTwoHundredIsSet()
    {
        // 100e530d. 60003's 0x103 has no 0x200; the 6104x records' 0x303 does.
        MeshSim plain = Make(R61043(flags: 0x103));
        plain.Step(1f, false, 0f, 0f, 0f);
        Assert.True(plain.Dead);

        MeshSim tough = Make();             // 0x303
        Assert.True(tough.OutlivesLocator);
        tough.Step(1f, false, 0f, 0f, 0f);
        Assert.False(tough.Dead);
    }

    [Fact]
    public void TheShippedRecordsCoverAllFourModels()
    {
        // 61041-61044 are Destruction 1-4 and take models 0, 1, 2 and 3 in turn.
        for (int i = 0; i < MeshSim.ModelNames.Length; i++)
            Assert.StartsWith("tower_destroyed_", MeshSim.ModelNames[i]);
        Assert.Equal(4, MeshSim.ModelNames.Length);
    }
}
