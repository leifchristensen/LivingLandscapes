using LivingLandscapes.Overlay;
using Xunit;

namespace LivingLandscapes.Tests;

/// <summary>#190: topology colour bands are a fixed 25 m on absolute multiples of 25, with the 7-colour palette stretched for taller maps.</summary>
public class TopologyBandMathTests
{
    private const float Step = 25f;

    [Theory]
    [InlineData(0f, 1)]
    [InlineData(10f, 1)]
    [InlineData(25f, 1)]
    [InlineData(26f, 2)]
    [InlineData(60f, 3)]
    [InlineData(150f, 6)]
    [InlineData(160f, 7)]
    [InlineData(175f, 7)]
    [InlineData(260f, 11)]
    public void BandCountFromAlignedMinimum(float relief, int expected)
    {
        // Minimum on a band edge, so relief maps straight to ceil(relief / 25).
        Assert.Equal(expected, TopologyBandMath.BandCount(300f, 300f + relief, Step));
    }

    [Fact]
    public void BandsSitOnAbsoluteMultiples()
    {
        Assert.Equal(100f, TopologyBandMath.Origin(110f, Step));
        Assert.Equal(110f, TopologyBandMath.Origin(110f, 5f));
        // 110..130 crosses the 125 edge: two bands. 110..120 stays inside one.
        Assert.Equal(2, TopologyBandMath.BandCount(110f, 130f, Step));
        Assert.Equal(1, TopologyBandMath.BandCount(110f, 120f, Step));
    }

    [Fact]
    public void BandIndexCountsFromOriginAndTopEdgeStaysInTopBand()
    {
        float origin = TopologyBandMath.Origin(110f, Step);
        Assert.Equal(0, TopologyBandMath.BandIndex(110f, origin, Step, 2));
        Assert.Equal(0, TopologyBandMath.BandIndex(124.9f, origin, Step, 2));
        Assert.Equal(1, TopologyBandMath.BandIndex(125f, origin, Step, 2));
        Assert.Equal(1, TopologyBandMath.BandIndex(150f, origin, Step, 2));
    }

    [Fact]
    public void UpToSevenBandsUseThePaletteInOrder()
    {
        for (int count = 1; count <= 7; count++)
            for (int b = 0; b < count; b++)
                Assert.Equal(b, TopologyBandMath.PalettePosition(b, count));
    }

    [Fact]
    public void SevenBandsUseFullPalette()
    {
        int count = TopologyBandMath.BandCount(0f, 160f, Step);
        Assert.Equal(7, count);
        Assert.Equal(0f, TopologyBandMath.PalettePosition(0, count));
        Assert.Equal(6f, TopologyBandMath.PalettePosition(count - 1, count));
    }

    [Fact]
    public void ElevenBandsSpanPaletteWithInterpolatedIntermediates()
    {
        int count = TopologyBandMath.BandCount(0f, 260f, Step);
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
        Assert.Equal(0, TopologyBandMath.BandIndex(42f, TopologyBandMath.Origin(42f, Step), Step, 1));
        Assert.Equal(0f, TopologyBandMath.PalettePosition(0, 1));
    }
}
