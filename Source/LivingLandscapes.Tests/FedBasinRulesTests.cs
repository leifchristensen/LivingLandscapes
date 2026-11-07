using LivingLandscapes.Geology;
using Xunit;

namespace LivingLandscapes.Tests;

/// <summary>A hollow a world river runs into holds water from beyond the map: it is no closed basin (#83, P3, P23).</summary>
public class FedBasinRulesTests
{
    // A 6x1 strip: sink A = cells 0-1, sink B = cells 4-5.
    private static (bool[] sink, float[] baseLevel) Strip() =>
        (new[] { true, true, false, false, true, true }, new[] { 5f, 5f, float.NaN, float.NaN, 7f, 7f });

    [Fact]
    public void ASinkBiggerThanTheRegionIsLiftedWhole()
    {
        (bool[] sink, float[] b) = Strip();
        var area = new[] { 10f, 500f, 1f, 1f, 10f, 10f };
        int lifted = FedBasinRules.LiftFedSinks(sink, b, area, 100, 6, 1);
        Assert.Equal(1, lifted);
        Assert.False(sink[0] || sink[1]);
        Assert.True(float.IsNaN(b[0]) && float.IsNaN(b[1]));
    }

    [Fact]
    public void ASinkOfLocalCatchmentStays()
    {
        (bool[] sink, float[] b) = Strip();
        var area = new[] { 10f, 50f, 1f, 1f, 10f, 100f };
        Assert.Equal(0, new[] { FedBasinRules.LiftFedSinks(sink, b, area, 100, 6, 1) }[0]);
        Assert.True(sink[0] && sink[1] && sink[4] && sink[5]);
        Assert.Equal(5f, b[0]);
    }

    [Fact]
    public void OnlyTheFedSinkIsLifted()
    {
        (bool[] sink, float[] b) = Strip();
        var area = new[] { 10f, 10f, 1f, 1f, 10f, 101f };
        Assert.Equal(1, FedBasinRules.LiftFedSinks(sink, b, area, 100, 6, 1));
        Assert.True(sink[0] && sink[1]);
        Assert.False(sink[4] || sink[5]);
        Assert.Equal(5f, b[0]);
    }

    [Fact]
    public void DiagonalNeighboursAreOneSink()
    {
        var sink = new[] { true, false, false, true };   // 2x2: cells 0 and 3 touch diagonally
        var b = new[] { 1f, float.NaN, float.NaN, 1f };
        var area = new[] { 500f, 1f, 1f, 1f };
        Assert.Equal(1, FedBasinRules.LiftFedSinks(sink, b, area, 100, 2, 2));
        Assert.False(sink[3]);
    }
}
