using LivingLandscapes.Overlay;
using Xunit;

namespace LivingLandscapes.Tests;

/// <summary>#190: topology colour bands are a fixed 15 m on absolute multiples of 15, with the 7-colour palette stretched for taller maps.</summary>
public class TopologyBandMathTests
{
    private const float Step = 15f;

    [Theory]
    [InlineData(0f, 1)]
    [InlineData(10f, 1)]
    [InlineData(15f, 1)]
    [InlineData(16f, 2)]
    [InlineData(36f, 3)]
    [InlineData(90f, 6)]
    [InlineData(102f, 7)]
    [InlineData(105f, 7)]
    [InlineData(156f, 11)]
    public void BandCountFromAlignedMinimum(float relief, int expected)
    {
        // Minimum on a band edge, so relief maps straight to ceil(relief / 15).
        Assert.Equal(expected, TopologyBandMath.BandCount(300f, 300f + relief, Step));
    }

    [Fact]
    public void BandsSitOnAbsoluteMultiples()
    {
        Assert.Equal(90f, TopologyBandMath.Origin(100f, Step));
        Assert.Equal(100f, TopologyBandMath.Origin(100f, 5f));
        // 100..112 straddles the 105 edge of a 15 m grid? No: 90..105 and 105..120, so two bands.
        Assert.Equal(2, TopologyBandMath.BandCount(100f, 112f, Step));
        // 106..112 sits inside one band.
        Assert.Equal(1, TopologyBandMath.BandCount(106f, 112f, Step));
    }

    [Fact]
    public void BandIndexCountsFromOriginAndTopEdgeStaysInTopBand()
    {
        float origin = TopologyBandMath.Origin(100f, Step);
        Assert.Equal(0, TopologyBandMath.BandIndex(100f, origin, Step, 2));
        Assert.Equal(0, TopologyBandMath.BandIndex(104.9f, origin, Step, 2));
        Assert.Equal(1, TopologyBandMath.BandIndex(105f, origin, Step, 2));
        Assert.Equal(1, TopologyBandMath.BandIndex(120f, origin, Step, 2));
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
        int count = TopologyBandMath.BandCount(0f, 102f, Step);
        Assert.Equal(7, count);
        Assert.Equal(0f, TopologyBandMath.PalettePosition(0, count));
        Assert.Equal(6f, TopologyBandMath.PalettePosition(count - 1, count));
    }

    [Fact]
    public void ElevenBandsSpanPaletteWithInterpolatedIntermediates()
    {
        int count = TopologyBandMath.BandCount(0f, 156f, Step);
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
