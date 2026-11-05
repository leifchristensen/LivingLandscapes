using System;
using LivingLandscapes.Geology;
using Xunit;

namespace LivingLandscapes.Tests;

/// <summary>
/// Which floor under a closed hollow seeps how much (P23 extended, #149): the floor class of a tile from what lies
/// under it, the water-table cap on that class's seepage, and the seepage the balance then uses,
/// S_eff = S(class) x cap(HAND), fed to <see cref="WaterBalanceMath.LakeShare"/>.
/// Tested as properties of the rules, not snapshots: the values live in FloorSeepage.xml and get tuned, the ordering
/// they must keep (a more permeable floor never holds a bigger lake; a dry floor seeps nothing) does not.
/// </summary>
public class FloorSeepageRulesTests
{
    /// <summary>The table as FloorSeepageDef ships it, so the tests cover the shipped ordering and thresholds.</summary>
    private static FloorSeepageRules Shipped() => new FloorSeepageRules();

    /// <summary>Tiles from bare massive rock to alluvium, with the karst and joint flags the rock classes need.</summary>
    [Theory]
    [InlineData(0f, 0f, 0f, false, 0f, FloorSeepageRules.ClayHardpan)]   // bare, unfractured rock: an aquitard floor
    [InlineData(0f, 0f, 0f, false, 0.4f, FloorSeepageRules.ClayHardpan)] // closed joints are still not a drain
    [InlineData(0f, 0f, 0f, false, 0.5f, FloorSeepageRules.JointedRock)] // open joints under a thin soil
    [InlineData(0f, 0f, 0f, true, 0f, FloorSeepageRules.Karst)]          // carbonate ground, joints or not
    [InlineData(0f, 0f, 1.2f, false, 0f, FloorSeepageRules.Silt)]        // deep weathered soil, no alluvium
    [InlineData(0f, 0f, 1.2f, true, 1f, FloorSeepageRules.Silt)]         // soil seals the karst under it
    [InlineData(0.5f, 0.1f, 0f, true, 1f, FloorSeepageRules.Silt)]       // fine alluvium seals it too
    [InlineData(0.5f, 0.25f, 0f, false, 0f, FloorSeepageRules.Sand)]     // the grainSilt bound is sand
    [InlineData(0.5f, 0.5f, 9f, true, 1f, FloorSeepageRules.Sand)]       // alluvium wins over soil and rock
    [InlineData(0.5f, 0.7f, 0f, false, 0f, FloorSeepageRules.Sand)]      // the grainGravel bound is still sand
    [InlineData(0.5f, 0.9f, 0f, false, 0f, FloorSeepageRules.Gravel)]    // coarse alluvium: an open bed
    public void ClassOf_SortsTheFloorByWhatLiesUnderIt(
        float alluvium, float grainSize, float soilDepth, bool karst, float joint, int expected)
    {
        int floorClass = Shipped().ClassOf(alluvium, grainSize, soilDepth, karst, joint);

        Assert.Equal(expected, floorClass);
    }

    [Fact]
    public void ClassOf_NeverReturnsAClassTheTableDoesNotKnow()
    {
        FloorSeepageRules rules = Shipped();

        Assert.Equal(rules.Seepage.Length, rules.ClassNames.Length);
        foreach (float alluvium in new[] { 0f, 0.5f })
            foreach (float grainSize in new[] { 0f, 0.3f, 0.8f })
                foreach (float soilDepth in new[] { 0f, 2f })
                    foreach (bool karst in new[] { false, true })
                        foreach (float joint in new[] { 0f, 0.6f })
                        {
                            int floorClass = rules.ClassOf(alluvium, grainSize, soilDepth, karst, joint);
                            Assert.InRange(floorClass, 0, rules.Seepage.Length - 1);
                        }
    }

    [Fact]
    public void Cap_IsFullAtTheFloor_ZeroOnceTheFloorStandsDry_AndNeverIncreasesWithDepth()
    {
        FloorSeepageRules rules = Shipped();

        Assert.Equal(1f, rules.Cap(0f));
        Assert.Equal(1f, rules.Cap(-3f), 4);  // a water table above the floor is still a wetted floor
        Assert.Equal(0f, rules.Cap(rules.handDry));
        Assert.Equal(0f, rules.Cap(rules.handDry * 2f));

        float previous = 1f;
        for (float hand = 0f; hand <= rules.handDry * 1.5f; hand += 0.5f)
        {
            float cap = rules.Cap(hand);
            Assert.InRange(cap, 0f, 1f);
            Assert.True(cap <= previous + 1e-6f, $"cap grows with depth at HAND {hand}: {previous} -> {cap}");
            previous = cap;
        }
    }

    [Fact]
    public void SeepageEff_IsTheClassValueAtTheFloor_AndNothingOnADryFloor()
    {
        FloorSeepageRules rules = Shipped();

        for (int floorClass = 0; floorClass < rules.Seepage.Length; floorClass++)
        {
            Assert.Equal(rules.Seepage[floorClass], rules.SeepageEff(floorClass, 0f), 4);
            Assert.Equal(0f, rules.SeepageEff(floorClass, rules.handDry * 2f), 4);
        }
    }

    [Fact]
    public void SeepageEff_IsNeverLargerForALessPermeableFloor_NorDeeperWaterTable()
    {
        FloorSeepageRules rules = Shipped();

        // the shipped table is ordered dry to permeable, which is what the class indices and the XML assume
        for (int i = 1; i < rules.Seepage.Length; i++)
            Assert.True(rules.Seepage[i] >= rules.Seepage[i - 1],
                $"{rules.ClassNames[i]} seeps less than {rules.ClassNames[i - 1]}: {rules.Seepage[i]} < {rules.Seepage[i - 1]}");

        foreach (float hand in new[] { 0f, 5f, 12.5f, 24f, 40f })
            for (int i = 1; i < rules.Seepage.Length; i++)
                Assert.True(rules.SeepageEff(i, hand) >= rules.SeepageEff(i - 1, hand),
                    $"class {i} seeps less than class {i - 1} at HAND {hand}");
    }

    [Fact]
    public void Seepage_NeverGivesABiggerLakeThanNoSeepage()
    {
        // the rules and the balance are one chain: whatever S_eff they produce, a wetted floor can only take water away
        FloorSeepageRules rules = Shipped();
        float runoff = 0.4f;

        foreach (float[] climate in new[] { new[] { 150f, 1200f }, new[] { 900f, 1100f }, new[] { 2200f, 700f },
                                            new[] { 3200f, 500f } })
        {
            float withoutSeepage = WaterBalanceMath.LakeShare(runoff, climate[0], climate[1], 0f);
            for (int floorClass = 0; floorClass < rules.Seepage.Length; floorClass++)
                foreach (float hand in new[] { 0f, 4f, 12f, 30f })
                {
                    float share = WaterBalanceMath.LakeShare(runoff, climate[0], climate[1],
                        rules.SeepageEff(floorClass, hand));
                    Assert.True(share <= withoutSeepage || Math.Abs(share - withoutSeepage) < 1e-4f,
                        $"{rules.ClassNames[floorClass]} at HAND {hand} gives a bigger lake than a sealed floor " +
                        $"(rain {climate[0]}, evap {climate[1]}): {share} > {withoutSeepage}");
                }
        }
    }

    [Fact]
    public void APermeableFloor_BetsAWetClimateDown_WhileAClayFloor_DoesNot()
    {
        // the ordering #149 asks for, at the wet temperate case of the loop (rain 2200, evaporation ~700):
        // gravel and karst stand their hollows below the rim, clay and silt keep them full to it.
        FloorSeepageRules rules = Shipped();
        float rain = 2200f, evaporation = 700f;

        foreach (int floorClass in new[] { FloorSeepageRules.Gravel, FloorSeepageRules.Karst })
            Assert.False(float.IsPositiveInfinity(
                    WaterBalanceMath.LakeShare(0.4f, rain, evaporation, rules.SeepageEff(floorClass, 0f))),
                $"{rules.ClassNames[floorClass]} on a wet floor should drain the hollow below its rim");

        foreach (int floorClass in new[] { FloorSeepageRules.ClayHardpan, FloorSeepageRules.Silt })
            Assert.True(float.IsPositiveInfinity(
                    WaterBalanceMath.LakeShare(0.4f, rain, evaporation, rules.SeepageEff(floorClass, 0f))),
                $"{rules.ClassNames[floorClass]} should still hold a hollow full to its rim");

        // the same gravel floor under a deep water table (a desert hollow) is unsaturated and holds as well as clay
        Assert.True(float.IsPositiveInfinity(
                WaterBalanceMath.LakeShare(0.4f, rain, evaporation,
                    rules.SeepageEff(FloorSeepageRules.Gravel, rules.handDry))),
            "a floor above the water table must not drain anything - this is what keeps deserts unchanged");
    }
}
