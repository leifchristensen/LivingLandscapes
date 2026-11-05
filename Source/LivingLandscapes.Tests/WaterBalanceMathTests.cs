using System;
using LivingLandscapes.Geology;
using Xunit;

namespace LivingLandscapes.Tests;

/// <summary>
/// The water balance of a closed hollow with seepage through its wetted floor (P23 extended, #149):
/// A_lake = runoff x rain x catchment / (evaporation - rain + S), so the share is the lake area per unit of catchment.
/// Tested as properties of the equation, not snapshots: the climate inputs and the S table get tuned, the balance does
/// not. Seepage is in mm/yr like rain and evaporation, so a wet climate needs a permeable floor to lose its water.
/// </summary>
public class WaterBalanceMathTests
{
    private const float Tolerance = 1e-4f;

    /// <summary>
    /// Asserts two shares are equal to a relative tolerance, treating +infinity (every hollow full to its rim) as a
    /// value. Assert.Equal(expected, actual, decimals) compares decimal places instead of relative error, which is the
    /// wrong measure for a share that spans 0.001 to infinity, and stumbles when only one side is infinite.
    /// </summary>
    private static void AssertShareEqual(float expected, float actual, string what)
    {
        if (float.IsPositiveInfinity(expected) || float.IsPositiveInfinity(actual))
        {
            Assert.Equal(expected, actual);  // infinities are only equal to each other
            return;
        }
        Assert.True(Math.Abs(expected - actual) <= Tolerance * Math.Max(1f, Math.Abs(expected)),
            $"{what}: expected {expected}, got {actual}");
    }

    /// <summary>The balance before seepage existed (WaterBalance.LakeShare at dc2c6f9), as the S = 0 reference.</summary>
    private static float ShareWithoutSeepage(float runoff, float rain, float evaporation)
    {
        float netEvaporation = evaporation - rain;
        return netEvaporation <= 0f ? float.PositiveInfinity : runoff * rain / netEvaporation;
    }

    /// <summary>mm/yr evaporation of a warm temperate tile, and a runoff share in the range the game uses.</summary>
    private const float Runoff = 0.4f, Rain = 900f, Evaporation = 1100f;

    [Theory]
    // dry (rain well under evaporation), near-balanced, and wet (rain over evaporation) climates, dry to wet floors
    [InlineData(0.4f, 300f, 1200f)]
    [InlineData(0.4f, 900f, 1100f)]
    [InlineData(0.5f, 1000f, 1000f)]
    [InlineData(0.4f, 2200f, 700f)]
    [InlineData(0.3f, 3200f, 500f)]
    public void LakeShare_WithNoSeepage_IsTheBalanceWithoutSeepage(float runoff, float rain, float evaporation)
    {
        float share = WaterBalanceMath.LakeShare(runoff, rain, evaporation, 0f);
        float today = ShareWithoutSeepage(runoff, rain, evaporation);

        AssertShareEqual(today, share, "S = 0 must reproduce today's share");
    }

    [Theory]
    [InlineData(900f, 900f)]   // rain equals evaporation: nothing left to hold the hollow below its rim
    [InlineData(2200f, 700f)]  // wet temperate forest
    [InlineData(3200f, 500f)]  // tropical rainforest
    public void LakeShare_RainAtOrAboveEvaporation_WithoutSeepage_FillsEveryHollow(float rain, float evaporation)
    {
        Assert.True(float.IsPositiveInfinity(WaterBalanceMath.LakeShare(Runoff, rain, evaporation, 0f)),
            "a hollow that only loses water by evaporation must fill to its rim when the rain beats it");
    }

    [Theory]
    // floors that seep more than the rain's surplus over evaporation close the balance below the rim; the wet
    // temperate climate here has a surplus of 1500 mm/yr (rain 2200, evaporation 700).
    [InlineData(1600f)]  // fine sand, just over the surplus
    [InlineData(2000f)]  // sand
    [InlineData(8000f)]  // gravel
    [InlineData(20000f)] // karst
    public void LakeShare_SeepageAboveTheRainSurplus_StandsBelowItsRim(float seepage)
    {
        float share = WaterBalanceMath.LakeShare(Runoff, 2200f, 700f, seepage);

        Assert.False(float.IsPositiveInfinity(share), "seepage is a loss too, so the balance can close below the rim");
        Assert.True(share > 0f, "a hollow with a positive balance still holds water: " + share);
        Assert.True(float.IsFinite(share));
    }

    [Theory]
    // floors that seep less than the rain's surplus cannot close the balance: the hollow fills to its rim and spills.
    // A clay or silt floor in a rainforest stays full - what C4 of #149 asks the wet maps to keep.
    [InlineData(1f)]     // clay
    [InlineData(20f)]    // silt
    [InlineData(1500f)]  // sand at exactly the surplus: the losses still do not beat the rain
    public void LakeShare_SeepageBelowTheRainSurplus_StillFillsToItsRim(float seepage)
    {
        Assert.True(float.IsPositiveInfinity(WaterBalanceMath.LakeShare(Runoff, 2200f, 700f, seepage)),
            "a floor that cannot pass the rain's surplus leaves the hollow filling to its rim");
    }

    [Fact]
    public void LakeShare_IsNeverLargerForAMorePermeableFloor()
    {
        // the same hollow in climates from dry to wet, its floor swept from clay to karst
        float[][] climates =
        {
            new[] { 300f, 1200f }, new[] { 900f, 1100f }, new[] { 1000f, 1000f },
            new[] { 2200f, 700f }, new[] { 3200f, 500f },
        };
        float[] floors = { 0f, 20f, 600f, 1500f, 8000f, 20000f };

        foreach (var climate in climates)
        {
            float rain = climate[0], evaporation = climate[1];
            float previous = WaterBalanceMath.LakeShare(Runoff, rain, evaporation, floors[0]);
            for (int i = 1; i < floors.Length; i++)
            {
                float share = WaterBalanceMath.LakeShare(Runoff, rain, evaporation, floors[i]);
                Assert.True(share <= previous || Math.Abs(share - previous) < Tolerance,
                    $"floor S={floors[i]} gives a larger lake than S={floors[i - 1]} at rain {rain}, evap {evaporation}");
                previous = share;
            }
        }
    }

    [Fact]
    public void LakeShare_WithSeepage_ShrinksLikeTheBalanceSays()
    {
        // A_lake/catchment = runoff x rain / (evap - rain + S): the loss term carries the seepage, one for one.
        AssertShareEqual(Runoff * Rain / (Evaporation - Rain), WaterBalanceMath.LakeShare(Runoff, Rain, Evaporation, 0f), "S = 0");
        AssertShareEqual(Runoff * Rain / (Evaporation - Rain + 400f), WaterBalanceMath.LakeShare(Runoff, Rain, Evaporation, 400f), "S = 400");
        // wet climate whose rain surplus (2200 - 700 = 1500) the floor outseeps: net loss 2000 - 1500 = 500
        AssertShareEqual(Runoff * 2200f / (700f - 2200f + 2000f), WaterBalanceMath.LakeShare(Runoff, 2200f, 700f, 2000f), "wet climate, S = 2000");
    }

    [Fact]
    public void LakeShare_AFloorSeepingMoreThanTheNetRain_IsNegligibleButStillHoldsWater()
    {
        // a hollow on karst in a rainforest: the balance closes on a pond, but never on nothing
        float share = WaterBalanceMath.LakeShare(Runoff, 3200f, 500f, 20000f);

        Assert.True(share > 0f);
        Assert.True(share < 0.1f, "a floor that drains far faster than the hollow fills leaves a pond, not a lake: " + share);
    }

    [Fact]
    public void LakeShare_NeverGoesNegative_HoweverTheInputsAreOrdered()
    {
        foreach (float rain in new[] { 0f, 60f, 900f, 3200f })
            foreach (float evaporation in new[] { 0f, 150f, 700f, 1200f })
                foreach (float seepage in new[] { 0f, 20f, 20000f })
                {
                    float share = WaterBalanceMath.LakeShare(Runoff, rain, evaporation, seepage);
                    Assert.True(float.IsPositiveInfinity(share) || share >= 0f,
                        $"negative share at rain {rain}, evap {evaporation}, S {seepage}: {share}");
                }
    }
}
