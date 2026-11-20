using LivingLandscapes.Geology;
using Xunit;

namespace LivingLandscapes.Tests;

/// <summary>Properties of the delta model's small formulas (<see cref="DeltaMath"/>), including their degenerate inputs.</summary>
public class DeltaMathTests
{
    private const float S = DeltaMath.MinSlope;

    [Fact]
    public void BackwaterLength_IsDepthOverSlope()
    {
        float l = DeltaMath.BackwaterLength(2f, 0.01f, 1000f, S, out bool capped);
        Assert.Equal(200f, l, 3);
        Assert.False(capped);
    }

    [Fact]
    public void BackwaterLength_IsCappedAndSaysSo()
    {
        float l = DeltaMath.BackwaterLength(2f, 0.01f, 54f, S, out bool capped);
        Assert.Equal(54f, l);
        Assert.True(capped);
    }

    [Fact]
    public void BackwaterLength_ExactlyAtCapIsNotCapped()
    {
        float l = DeltaMath.BackwaterLength(2f, 0.01f, 200f, S, out bool capped);
        Assert.Equal(200f, l, 3);
        Assert.False(capped);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-0.5f)]
    [InlineData(float.NaN)]
    public void BackwaterLength_BadSlopeUsesTheLeastSlope(float slope)
    {
        float l = DeltaMath.BackwaterLength(1f, slope, 1e6f, S, out bool capped);
        Assert.Equal(1f / S, l, 1);
        Assert.False(capped);
    }

    [Fact]
    public void BackwaterLength_SlopeBelowLeastIsRaisedToIt()
    {
        float l = DeltaMath.BackwaterLength(1f, S / 10f, 1e6f, S, out _);
        Assert.Equal(1f / S, l, 1);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    public void BackwaterLength_NeverNegative(float depth)
    {
        float l = DeltaMath.BackwaterLength(depth, 0.01f, 100f, S, out bool capped);
        Assert.Equal(0f, l);
        Assert.False(capped);
    }

    [Fact]
    public void BackwaterLength_NegativeCapGivesZero()
    {
        float l = DeltaMath.BackwaterLength(1f, 0.01f, -5f, S, out bool capped);
        Assert.Equal(0f, l);
        Assert.True(capped);
    }

    [Fact]
    public void Superelevation_IsWaterSurfaceAboveLateralGroundInDepths()
    {
        Assert.Equal(1f, DeltaMath.Superelevation(10f, 2f, 10f), 4);
        Assert.Equal(2f, DeltaMath.Superelevation(10f, 2f, 8f), 4);
    }

    [Fact]
    public void Superelevation_ZeroWhenSurfaceMeetsGroundAndNegativeBelowIt()
    {
        Assert.Equal(0f, DeltaMath.Superelevation(8f, 2f, 10f), 4);
        Assert.True(DeltaMath.Superelevation(5f, 2f, 10f) < 0f);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void Superelevation_NoDepthGivesZero(float depth)
    {
        Assert.Equal(0f, DeltaMath.Superelevation(10f, depth, 3f));
    }

    [Fact]
    public void BarForms_WhenBasinShallowerThanRhoChannelDepths()
    {
        Assert.True(DeltaMath.BarForms(1f, 2f, 1.25f));
        Assert.False(DeltaMath.BarForms(3f, 2f, 1.25f));
    }

    [Fact]
    public void BarForms_EqualIsNoBar()
    {
        Assert.False(DeltaMath.BarForms(2.5f, 2f, 1.25f));
    }

    [Fact]
    public void BarForms_ZeroBasinDepthFormsUnlessChannelDepthIsZero()
    {
        Assert.True(DeltaMath.BarForms(0f, 1f, 1f));
        Assert.False(DeltaMath.BarForms(0f, 0f, 1f));
    }

    [Fact]
    public void WaveRegime_ClassesByRatio()
    {
        Assert.Equal(DeltaMath.Regime.NoDelta, DeltaMath.WaveRegime(0.5, 1.0).Class);
        Assert.Equal(DeltaMath.Regime.WaveInfluenced, DeltaMath.WaveRegime(2.0, 1.0).Class);
        Assert.Equal(DeltaMath.Regime.RiverDominated, DeltaMath.WaveRegime(5.0, 1.0).Class);
        Assert.Equal(5.0, DeltaMath.WaveRegime(5.0, 1.0).Ratio, 6);
    }

    [Fact]
    public void WaveRegime_BoundariesBelongToTheHigherClass()
    {
        Assert.Equal(DeltaMath.Regime.WaveInfluenced, DeltaMath.WaveRegime(1.0, 1.0).Class);
        Assert.Equal(DeltaMath.Regime.RiverDominated, DeltaMath.WaveRegime(3.0, 1.0).Class);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-2.0)]
    public void WaveRegime_NoWavesIsInfiniteAndRiverDominated(double waveFlux)
    {
        DeltaMath.WaveResult r = DeltaMath.WaveRegime(1.0, waveFlux);
        Assert.True(double.IsPositiveInfinity(r.Ratio));
        Assert.Equal(DeltaMath.Regime.RiverDominated, r.Class);
    }

    [Fact]
    public void WaveRegime_NoRiverWithWavesIsNoDelta()
    {
        DeltaMath.WaveResult r = DeltaMath.WaveRegime(0.0, 1.0);
        Assert.Equal(0.0, r.Ratio);
        Assert.Equal(DeltaMath.Regime.NoDelta, r.Class);
    }

    [Fact]
    public void Room_IsLevelMinusBed()
    {
        Assert.Equal(3f, DeltaMath.Room(0f, -3f), 4);
    }

    [Fact]
    public void Room_ZeroAtOrAboveTheLevel()
    {
        Assert.Equal(0f, DeltaMath.Room(0f, 0f));
        Assert.Equal(0f, DeltaMath.Room(0f, 2f));
    }
}
