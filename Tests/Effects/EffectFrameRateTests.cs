using Xunit;

namespace LostEden.Effects.Tests;

public class EffectFrameRateTests
{
    const float StockDt = EffectFrameRate.StockFrameDt;
    const float HighFpsDt = StockDt / 8f;

    [Fact]
    public void AStockFrameCountsAsExactlyOneStep()
    {
        Assert.Equal(1f, EffectFrameRate.FrameSteps(StockDt), 5);
    }

    [Fact]
    public void EightTimesTheFrameRateMovesAnEighthAsFarPerFrame()
    {
        Assert.Equal(0.125f, EffectFrameRate.FrameSteps(HighFpsDt), 5);
    }

    /// <summary>
    /// The whole point: a stock per-frame constant scaled this way covers the same ground per second
    /// no matter how many frames that second is cut into.
    /// </summary>
    [Theory]
    [InlineData(1f / 30f)]
    [InlineData(1f / 60f)]
    [InlineData(1f / 144f)]
    [InlineData(1f / 240f)]
    public void ASecondOfDriftIsTheSameAtEveryFrameRate(float dt)
    {
        const float perStockFrame = 0.02f;

        float drift = 0f;
        for (float t = 0f; t < 1f; t += dt)
            drift += perStockFrame * EffectFrameRate.FrameSteps(dt);

        float expected = perStockFrame / StockDt;
        Assert.Equal(expected, drift, 1);
    }

    [Fact]
    public void AStallIsClampedRatherThanJumped()
    {
        Assert.Equal(EffectFrameRate.MaxFrameSteps, EffectFrameRate.FrameSteps(30f), 5);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    public void ANonAdvancingFrameStepsNowhere(float dt)
    {
        Assert.Equal(0f, EffectFrameRate.FrameSteps(dt));
    }

    /// <summary>
    /// Stock's spawn allowance of 2 per Process is a rate, not a per-frame constant. Truncating the
    /// scaled budget would round 0.25 to nothing and stall the emitter, so the carry has to keep it.
    /// </summary>
    [Fact]
    public void AFractionalBudgetStillOpensSlots()
    {
        float carry = 0f;
        int opened = 0;
        for (int i = 0; i < 8; i++)
            opened += EffectFrameRate.TakeBudget(ref carry, 2, HighFpsDt);

        Assert.Equal(2, opened);
    }

    [Theory]
    [InlineData(1f / 30f)]
    [InlineData(1f / 60f)]
    [InlineData(1f / 144f)]
    [InlineData(1f / 240f)]
    public void ASecondOpensStocksSlotCountAtEveryFrameRate(float dt)
    {
        float carry = 0f;
        int opened = 0;
        for (float t = 0f; t < 1f; t += dt)
            opened += EffectFrameRate.TakeBudget(ref carry, 2, dt);

        // Stock managed 2 per frame at ~30 FPS.
        Assert.InRange(opened, 58, 62);
    }

    [Fact]
    public void AStockRateFrameHandsBackTheStockAllowanceExactly()
    {
        float carry = 0f;
        Assert.Equal(15, EffectFrameRate.TakeBudget(ref carry, 15, StockDt));
    }

    [Fact]
    public void ABudgetOfNothingStaysNothing()
    {
        float carry = 0f;
        Assert.Equal(0, EffectFrameRate.TakeBudget(ref carry, 0, StockDt));
        Assert.Equal(0f, carry);
    }

    [Fact]
    public void TheCarryNeverGrowsPastOneWholeSlot()
    {
        float carry = 0f;
        for (int i = 0; i < 100; i++)
        {
            EffectFrameRate.TakeBudget(ref carry, 2, HighFpsDt);
            Assert.InRange(carry, 0f, 1f);
        }
    }
}
