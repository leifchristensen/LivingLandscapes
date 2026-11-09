using LivingLandscapes.Geology;
using Xunit;

namespace LivingLandscapes.Tests;

/// <summary>
/// Loose ground (issue #186, G4): the exported erodibility blends the rock K toward the loose K by the share of
/// looseDepth the river deposits fill (LooseGround.Erodibility). The model's own K never goes through this.
/// </summary>
public class LooseGroundTests
{
    [Fact]
    public void NoDeposit_GivesRockK()
    {
        Assert.Equal(2.5f, LooseGround.Erodibility(2.5f, 0f, 8f, 1f), 6);
    }

    [Fact]
    public void DepositAtOrOverLooseDepth_GivesLooseK()
    {
        Assert.Equal(8f, LooseGround.Erodibility(2.5f, 1f, 8f, 1f), 6);
        Assert.Equal(8f, LooseGround.Erodibility(2.5f, 3.7f, 8f, 1f), 6);
    }

    [Fact]
    public void Between_IsLinearAndMonotone()
    {
        // Half the depth is half the way from rock K to loose K: 2.5 + (8 - 2.5) * 0.5 = 5.25.
        Assert.Equal(5.25f, LooseGround.Erodibility(2.5f, 0.5f, 8f, 1f), 6);
        float prev = LooseGround.Erodibility(2.5f, 0f, 8f, 1f);
        for (int i = 1; i <= 10; i++)
        {
            float v = LooseGround.Erodibility(2.5f, i * 0.1f, 8f, 1f);
            Assert.True(v > prev, $"deposit {i * 0.1f}: {v} should exceed {prev}");
            prev = v;
        }
        Assert.Equal(8f, prev, 5); // the last step (depth 1.0) is fully loose
    }

    [Fact]
    public void LooseKEqualsRockK_GivesRockK()
    {
        Assert.Equal(3f, LooseGround.Erodibility(3f, 0.5f, 3f, 1f), 6);
        Assert.Equal(3f, LooseGround.Erodibility(3f, 5f, 3f, 1f), 6);
    }
}
