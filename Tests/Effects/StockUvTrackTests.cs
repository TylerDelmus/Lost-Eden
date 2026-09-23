using Xunit;

/// <summary>
/// Locks <see cref="StockUvTrack"/> to randy31's FAFAnim_t evaluate (<c>10028fde</c>) and
/// RRefFrame_t::SetAnimationTime (<c>1004506b</c>), with the hoverbike circles' track (271013): offset u
/// 0 → 1 over 0.8 s, looping, both keys lerping.
/// </summary>
public class StockUvTrackTests
{
    static StockUvTrack Circle(bool lerp = true) => new StockUvTrack(
        new[] { 0f, 0.8f }, new[] { 1f, 1f }, new[] { 1f, 1f }, new[] { 0f, 1f }, new[] { 0f, 0f },
        new[] { lerp, lerp }, loop: true, totalTime: 0.8f);

    [Fact]
    public void TheOffset_ScrollsLinearly()
    {
        var uv = Circle();
        int k = 0;
        uv.Evaluate(0.2f, ref k, out float tu, out float tv, out float ou, out float ov);
        Assert.Equal(0.25f, ou, 5);
        Assert.Equal(0f, ov);
        Assert.Equal(1f, tu);
        Assert.Equal(1f, tv);
        uv.Evaluate(0.8f, ref k, out _, out _, out ou, out _);
        Assert.Equal(1f, ou, 5);
    }

    [Fact]
    public void WithoutTheFlag_TheOffsetHolds()
    {
        var uv = Circle(lerp: false);
        int k = 0;
        uv.Evaluate(0.4f, ref k, out _, out _, out float ou, out _);
        Assert.Equal(0f, ou);
    }

    [Fact]
    public void TheCachedKey_StepsBackForAnEarlierTime()
    {
        var uv = new StockUvTrack(
            new[] { 0f, 1f, 2f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 0f, 1f, 3f }, new[] { 0f, 0f, 0f },
            new[] { true, true, true }, loop: true, totalTime: 2f);
        int k = 0;
        uv.Evaluate(1.5f, ref k, out _, out _, out float ou, out _);
        Assert.Equal(1, k);
        Assert.Equal(2f, ou, 5);
        uv.Evaluate(0.5f, ref k, out _, out _, out ou, out _);
        Assert.Equal(0, k);
        Assert.Equal(0.5f, ou, 5);
    }

    [Fact]
    public void NodeTime_WrapsButAWholeLoopIsTheTotal()
    {
        Assert.Equal(0.4f, StockUvTrack.NodeTime(1.2f, 0.8f), 5);
        Assert.Equal(0.8f, StockUvTrack.NodeTime(1.6f, 0.8f));
        Assert.Equal(0f, StockUvTrack.NodeTime(0f, 0.8f));
    }

    [Fact]
    public void OneKey_SetsNothing()
    {
        var uv = new StockUvTrack(new[] { 0f }, new[] { 2f }, new[] { 2f }, new[] { 0.5f }, new[] { 0.5f },
            new[] { true }, loop: true, totalTime: 1f);
        int k = 0;
        uv.Evaluate(0.5f, ref k, out float tu, out _, out float ou, out _);
        Assert.Equal(1f, tu);
        Assert.Equal(0f, ou);
    }
}
