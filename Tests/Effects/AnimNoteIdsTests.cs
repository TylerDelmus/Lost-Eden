using Xunit;

/// <summary>Locks <see cref="AnimNoteIds"/> to DisplaySystem FUN_10075c84.</summary>
public class AnimNoteIdsTests
{
    [Theory]
    [InlineData("effect1start", 0x42)]
    [InlineData("effect1start_left", 0x42)]     // strncmp over the key's length: a prefix is enough
    [InlineData("Effect1Start", 0)]             // strncmp is case-sensitive
    [InlineData("effect1stop", 0x43)]
    [InlineData("itemshow_b", 0x41)]
    [InlineData("attack_effect_1", 0x8e)]
    [InlineData("attack", 0xb)]
    [InlineData("stepfast", 0x26)]              // "step" comes first and shadows it
    [InlineData("idle_combat", 0xf)]            // likewise "idle"
    [InlineData("useitemonitem", 0x3)]          // likewise "use"
    [InlineData("wear", 0x6)]
    [InlineData("loopstart", 0)]
    [InlineData("SM_Sand", 0)]
    [InlineData("", 0)]
    public void NameMapsToTheStockEventId(string name, int id)
    {
        Assert.Equal(id, AnimNoteIds.FromName(name));
    }

    [Fact]
    public void ControlBytesEndTheName()
    {
        Assert.Equal(0x42, AnimNoteIds.FromName("effect1start\u0001xx"));
        Assert.Equal(0, AnimNoteIds.FromName("effect\u00011start"));
    }

    [Theory]
    [InlineData(0.5f, 0.49f, 0.51f, false, true)]
    [InlineData(0.5f, 0.4f, 0.5f, false, true)]   // up to and including the new time
    [InlineData(0.5f, 0.5f, 0.6f, false, false)]  // already passed on the step before
    [InlineData(0.5f, 0.5f, 0.6f, true, true)]    // a clip's first step includes its start
    [InlineData(0f, 0f, 0.033f, true, true)]
    [InlineData(0.7f, 0.4f, 0.6f, false, false)]
    public void Crossed_IsAfterFromUpToTo(float t, float from, float to, bool includeFrom, bool expected)
    {
        Assert.Equal(expected, AnimNoteIds.Crossed(t, from, to, includeFrom));
    }
}
