using LivingLandscapes.Overlay;
using Xunit;

namespace LivingLandscapes.Tests;

/// <summary>#190: topology bands are a fixed 5 m high, counted up from the map minimum, with the 7-colour palette stretched for taller maps.</summary>
public class TopologyBandMathTests
{
    private const float Step = 5f;

    [Theory]
    [InlineData(0f, 1)]
    [InlineData(3f, 1)]
    [InlineData(5f, 1)]
    [InlineData(12f, 3)]
    [InlineData(30f, 6)]
    [InlineData(34f, 7)]
    [InlineData(35f, 7)]
    [InlineData(52f, 11)]
    public void BandCountIsCeilOfReliefOverInterval(float relief, int expected)
    {
        Assert.Equal(expected, TopologyBandMath.BandCount(100f, 100f + relief, Step));
    }

    [Fact]
    public void BandIndexCountsUpFromMinimumAndTopEdgeStaysInTopBand()
    {
        Assert.Equal(0, TopologyBandMath.BandIndex(100f, 100f, Step, 3));
        Assert.Equal(0, TopologyBandMath.BandIndex(104.9f, 100f, Step, 3));
        Assert.Equal(1, TopologyBandMath.BandIndex(105f, 100f, Step, 3));
        Assert.Equal(2, TopologyBandMath.BandIndex(112f, 100f, Step, 3));
        Assert.Equal(1, TopologyBandMath.BandIndex(110f, 100f, Step, 2));
    }

    [Fact]
    public void UpToSevenBandsUseThePaletteInOrder()
    {
        for (int count = 1; count <= 7; count++)
            for (int b = 0; b < count; b++)
                Assert.Equal(b, TopologyBandMath.PalettePosition(b, count));
    }

    [Fact]
    public void FullPaletteAtThirtyFourMetres()
    {
        int count = TopologyBandMath.BandCount(0f, 34f, Step);
        Assert.Equal(7, count);
        Assert.Equal(0f, TopologyBandMath.PalettePosition(0, count));
        Assert.Equal(6f, TopologyBandMath.PalettePosition(count - 1, count));
    }

    [Fact]
    public void FiftyTwoMetresSpansPaletteWithInterpolatedIntermediates()
    {
        int count = TopologyBandMath.BandCount(0f, 52f, Step);
        Assert.Equal(11, count);
        Assert.Equal(0f, TopologyBandMath.PalettePosition(0, count));
        Assert.Equal(6f, TopologyBandMath.PalettePosition(count - 1, count), 4);
        float previous = -1f;
        for (int b = 0; b < count; b++)
        {
            float pos = TopologyBandMath.PalettePosition(b, count);
            Assert.True(pos > previous);
            previous = pos;
        }
        Assert.Equal(3f, TopologyBandMath.PalettePosition(5, count), 4);
    }

    [Fact]
    public void FlatMapIsOneLowestBand()
    {
        Assert.Equal(1, TopologyBandMath.BandCount(42f, 42f, Step));
        Assert.Equal(0, TopologyBandMath.BandIndex(42f, 42f, Step, 1));
        Assert.Equal(0f, TopologyBandMath.PalettePosition(0, 1));
    }
}
