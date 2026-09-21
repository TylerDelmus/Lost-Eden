using System;
using System.Linq;
using Xunit;

/// <summary>
/// Locks the stock GfxControlScatter_t schedule using the real templates:
/// 71044 (mode 1, 16 slots over 10s), 71055 and 71056 (mode 0, 8 slots over 2.5s / 2s).
/// </summary>
public class ScatterScheduleTests
{
    [Fact]
    public void Record71044_EvenModeSpacesSixteenSlotsOverTenSeconds()
    {
        float[] times = ScatterSchedule.BuildFireTimes(
            count: 16, duration: 10f, mode: ScatterSchedule.ModeEven, unit01: null);

        Assert.Equal(16, times.Length);
        Assert.Equal(0f, times[0], 5);
        Assert.Equal(0.625f, times[1], 5);
        Assert.Equal(9.375f, times[15], 5);
        // Evenly spaced and strictly increasing.
        for (int i = 1; i < times.Length; i++)
            Assert.Equal(0.625f, times[i] - times[i - 1], 4);
    }

    [Fact]
    public void EvenModeNeverSchedulesAtOrBeyondTheDuration()
    {
        // duration * i / count keeps the last slot strictly inside the window.
        float[] times = ScatterSchedule.BuildFireTimes(8, 2f, ScatterSchedule.ModeEven, null);
        Assert.All(times, t => Assert.True(t < 2f));
    }

    [Fact]
    public void Record71055_RandomModeKeepsEverySlotInsideTheWindow()
    {
        var rng = new Random(1234);
        float[] times = ScatterSchedule.BuildFireTimes(
            count: 8, duration: 2.5f, mode: ScatterSchedule.ModeRandom, unit01: () => (float)rng.NextDouble());

        Assert.Equal(8, times.Length);
        Assert.All(times, t => Assert.InRange(t, 0f, 2.5f));
        // Random mode must not come out evenly spaced.
        Assert.True(times.Distinct().Count() > 1);
    }

    [Fact]
    public void RandomModeScalesTheUnitStreamByDuration()
    {
        float[] times = ScatterSchedule.BuildFireTimes(3, 4f, ScatterSchedule.ModeRandom, () => 0.25f);
        Assert.All(times, t => Assert.Equal(1f, t, 5));
    }

    [Fact]
    public void UnknownModeLeavesSlotsAtZeroLikeTheZeroedAllocation()
    {
        float[] times = ScatterSchedule.BuildFireTimes(4, 5f, mode: 7, unit01: () => 0.5f);
        Assert.Equal(4, times.Length);
        Assert.All(times, t => Assert.Equal(0f, t));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void NonPositiveCountProducesNoSlots(int count)
    {
        Assert.Empty(ScatterSchedule.BuildFireTimes(count, 5f, ScatterSchedule.ModeEven, null));
    }
}
