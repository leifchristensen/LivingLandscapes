using System;
using System.Collections.Generic;
using LivingLandscapes.Geology;
using UnityEngine;
using Xunit;

namespace LivingLandscapes.Tests;

/// <summary>
/// Feature regions, phase 1 (FEATURE-REGIONS.md): the conditions and their combination rules, requirements that log and
/// skip, time windows, region shapes, and "combine every contribution first, then apply the limits last".
/// </summary>
public class FeatureRegionsTests
{
    // ---- Conditions: each has one fixed combination rule ----

    [Fact]
    public void EachConditionHasItsFixedCombinationRule()
    {
        Assert.Equal(CombineRule.Add, FeatureConditions.RuleOf(Condition.Uplift));
        Assert.Equal(CombineRule.TimeOrder, FeatureConditions.RuleOf(Condition.RockArrangement));
        Assert.Equal(CombineRule.TimeOrder, FeatureConditions.RuleOf(Condition.InheritedSurface));
        Assert.Equal(CombineRule.PerDrainage, FeatureConditions.RuleOf(Condition.BaseLevel));
        Assert.Equal(CombineRule.PerDrainage, FeatureConditions.RuleOf(Condition.GroundWater));
        foreach (Condition c in new[] { Condition.WaterSupply, Condition.SedimentSupply, Condition.HeatFlow, Condition.IceSupply })
            Assert.Equal(CombineRule.Add, FeatureConditions.RuleOf(c));
    }

    [Fact]
    public void OnlyUpliftAndRockArrangementAreReadInPhaseOne()
    {
        foreach (Condition c in Enum.GetValues(typeof(Condition)))
            Assert.Equal(c == Condition.Uplift || c == Condition.RockArrangement, FeatureConditions.IsRead(c));
    }

    // ---- Requirements: a missing world fact means the feature is not formed, and the reason is given ----

    private static WorldFacts Plain() => new WorldFacts
    {
        hasRiver = false, hasCoast = false, hasSlope = false, hilliness = "Flat",
        rocks = new List<(string, float)> { ("Granite", 0f), ("Sandstone", 0.1f) },
    };

    [Fact]
    public void MetRequirementsGiveNoReasons()
    {
        var reqs = new List<FeatureRequirement>
        {
            new FeatureRequirement { fact = WorldFact.Hilliness, anyOf = { "Flat", "SmallHills" } },
            new FeatureRequirement { fact = WorldFact.RockType, anyOf = { "Sandstone", "Limestone" } },
        };
        Assert.Empty(FeatureRequirements.Unmet(reqs, Plain()));
        Assert.Empty(FeatureRequirements.Unmet(null, Plain()));
    }

    [Fact]
    public void EachUnmetRequirementSaysWhy()
    {
        var reqs = new List<FeatureRequirement>
        {
            new FeatureRequirement { fact = WorldFact.WorldRiver },
            new FeatureRequirement { fact = WorldFact.Coast },
            new FeatureRequirement { fact = WorldFact.Slope },
            new FeatureRequirement { fact = WorldFact.Hilliness, anyOf = { "Mountainous" } },
            new FeatureRequirement { fact = WorldFact.RockType, anyOf = { "Marble" } },
            new FeatureRequirement { fact = WorldFact.SolubleRock, atLeast = 0.5f },
        };
        List<string> reasons = FeatureRequirements.Unmet(reqs, Plain());
        Assert.Equal(6, reasons.Count);
        Assert.Contains("no world river", reasons[0]);
        Assert.Contains("no coast", reasons[1]);
        Assert.Contains("no slope", reasons[2]);
        Assert.Contains("Flat, not Mountainous", reasons[3]);
        Assert.Contains("Marble", reasons[4]);
        Assert.Contains("Granite, Sandstone", reasons[4]);
        Assert.Contains("no soluble rock", reasons[5]);
    }

    [Fact]
    public void AnEmptyListAcceptsAnyHillinessOrRock()
    {
        var reqs = new List<FeatureRequirement>
        {
            new FeatureRequirement { fact = WorldFact.Hilliness },
            new FeatureRequirement { fact = WorldFact.RockType },
        };
        Assert.Empty(FeatureRequirements.Unmet(reqs, Plain()));
    }

    // ---- Time windows: whole run by default, smooth start and end ----

    [Fact]
    public void TheWholeRunIsExactlyOneThroughout()
    {
        for (int i = 0; i <= 100; i++)
            Assert.Equal(1f, TimeWindow.WholeRun.Weight(i / 100f));
        Assert.True(new TimeWindow().IsWholeRun);
        Assert.Empty(new TimeWindow().ConfigErrors());
    }

    [Fact]
    public void APartialWindowRampsSmoothlyInAndOut()
    {
        var w = new TimeWindow { start = 0.2f, end = 0.7f, ramp = 0.1f };
        Assert.Equal(0f, w.Weight(0.1f));
        Assert.Equal(0f, w.Weight(0.2f));
        Assert.Equal(1f, w.Weight(0.3f), 5);
        Assert.Equal(1f, w.Weight(0.5f), 5);
        Assert.Equal(0f, w.Weight(0.7f), 5);
        Assert.Equal(0f, w.Weight(0.9f));
        // Continuous (no step larger than the ramp's slope allows), rising monotonically, then falling monotonically.
        float previous = w.Weight(0f);
        for (int i = 1; i <= 1000; i++)
        {
            float t = i / 1000f, now = w.Weight(t);
            Assert.True(Math.Abs(now - previous) < 0.02f, $"jump at t={t}");
            if (t <= 0.45f)
                Assert.True(now >= previous - 1e-6f, $"falls while rising at t={t}");
            else
                Assert.True(now <= previous + 1e-6f, $"rises while falling at t={t}");
            Assert.InRange(now, 0f, 1f);
            previous = now;
        }
    }

    [Fact]
    public void AWindowFromTheStartOrToTheEndHasNoRampThere()
    {
        Assert.Equal(1f, new TimeWindow { start = 0f, end = 0.5f }.Weight(0f));
        Assert.Equal(1f, new TimeWindow { start = 0.5f, end = 1f }.Weight(1f));
    }

    [Fact]
    public void BadWindowsAreConfigErrors()
    {
        Assert.NotEmpty(new TimeWindow { start = 0.6f, end = 0.4f }.ConfigErrors());
        Assert.NotEmpty(new TimeWindow { start = 0.2f, end = 0.6f, ramp = 0f }.ConfigErrors());
        Assert.Empty(new TimeWindow { start = 0.2f, end = 0.6f, ramp = 0.05f }.ConfigErrors());
    }

    // ---- Shapes ----

    private static readonly Vector2 East = new Vector2(1f, 0f);

    [Fact]
    public void ARingPeaksOnItsRimAndIsEmptyInsideAndFarAway()
    {
        float W(float x) => FeatureShapeMath.Weight(ShapeKind.Ring, BandSide.Both, Vector2.zero, new Vector2(x, 0f), 0f, 0.02f, 0.01f, radius: 0.2f);
        Assert.Equal(1f, W(0.2f), 5);
        Assert.Equal(0f, W(0f), 5);
        Assert.Equal(0f, W(0.5f), 5);
        Assert.True(W(0.215f) > 0f && W(0.215f) < 1f);
    }

    [Fact]
    public void AScatterTakesItsStrongestPointAndNeverExceedsOne()
    {
        var points = new List<Vector2> { new Vector2(0.1f, 0f), new Vector2(0.11f, 0f), new Vector2(-0.2f, 0.1f) };
        float W(Vector2 q) => FeatureShapeMath.Weight(ShapeKind.Scatter, BandSide.Both, Vector2.zero, q, 0f, 0.05f, 0.01f, 0.3f, points);
        Assert.Equal(1f, W(points[0]), 5);
        Assert.Equal(1f, W(points[2]), 5);
        Assert.Equal(0f, W(new Vector2(0.5f, 0.5f)), 5);
        for (int i = 0; i <= 50; i++)
            Assert.InRange(W(new Vector2(0.05f + i * 0.002f, 0f)), 0f, 1f); // overlapping points do not add up
        Assert.Equal(0f, FeatureShapeMath.Weight(ShapeKind.Scatter, BandSide.Both, Vector2.zero, Vector2.zero, 0f, 0.05f, 0.01f));
    }

    [Fact]
    public void APointHasNoFlatCore()
    {
        float W(float x) => FeatureShapeMath.Weight(ShapeKind.Point, BandSide.Both, Vector2.zero, new Vector2(x, 0f), 0f, 0.1f, 0.01f);
        Assert.Equal(1f, W(0f), 5);
        Assert.True(W(0.03f) < 1f && W(0.03f) > W(0.06f));
        Assert.Equal(0f, W(0.1f), 5);
    }

    // Today's landform shapes, as Landforms.Inside computed them before the port: the ported shapes must give the same
    // weight bit for bit, so the maps stay identical.
    private static float OldAxis(Vector2 dir, Vector2 q, float offset, float size, float edge)
    {
        edge = Mathf.Max(edge, 1e-4f);
        var normal = new Vector2(-dir.y, dir.x);
        float distance = Mathf.Abs(Vector2.Dot(q, normal) - offset);
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(size - edge, size + edge, distance));
    }

    private static float OldRay(Vector2 dir, Vector2 q, float size, float edge)
    {
        edge = Mathf.Max(edge, 1e-4f);
        float along = Vector2.Dot(q, dir);
        float distance = along < 0f ? q.magnitude : Mathf.Abs(Vector2.Dot(q, new Vector2(-dir.y, dir.x)));
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(size - edge, size + edge, distance));
    }

    private static float OldRamp(Vector2 dir, Vector2 q, float offset, float size, float edge)
    {
        edge = Mathf.Max(edge, 1e-4f);
        float along = Vector2.Dot(q, dir) - offset;
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-size - edge, size + edge, along));
    }

    private static float OldDisk(Vector2 dir, Vector2 q, float offset, float size, float edge)
    {
        edge = Mathf.Max(edge, 1e-4f);
        float distance = (q - dir * offset).magnitude;
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(size - edge, size + edge, distance));
    }

    [Fact]
    public void TheShapesTodaysLandformsUseGiveTheOldWeightsBitForBit()
    {
        var rng = new System.Random(7);
        for (int i = 0; i < 5000; i++)
        {
            float a = (float)(rng.NextDouble() * 2 * Math.PI);
            var dir = new Vector2((float)Math.Cos(a), (float)Math.Sin(a));
            var q = new Vector2((float)(rng.NextDouble() - 0.5), (float)(rng.NextDouble() - 0.5));
            float offset = (float)(rng.NextDouble() * 0.4 - 0.2), size = (float)(rng.NextDouble() * 0.3), edge = (float)(rng.NextDouble() * 0.1);
            if (i % 50 == 0)
                edge = 0f; // the old clamp
            Assert.Equal(OldAxis(dir, q, offset, size, edge), FeatureShapeMath.Weight(ShapeKind.Band, BandSide.Both, dir, q, offset, size, edge));
            Assert.Equal(OldRamp(dir, q, offset, size, edge), FeatureShapeMath.Weight(ShapeKind.Band, BandSide.Inside, dir, q, offset, size, edge));
            Assert.Equal(OldRay(dir, q, size, edge), FeatureShapeMath.Weight(ShapeKind.Path, BandSide.Both, dir, q, 0f, size, edge));
            Assert.Equal(OldDisk(dir, q, offset, size, edge), FeatureShapeMath.Weight(ShapeKind.Area, BandSide.Both, dir, q, offset, size, edge));
        }
    }

    // ---- Order of operations: combine every contribution, then the limits last ----

    private static float[] Fill(int n, float v)
    {
        var a = new float[n];
        for (int i = 0; i < n; i++)
            a[i] = v;
        return a;
    }

    [Fact]
    public void UpliftAddsEveryContributionThenTheFloorThenTheGradeThenTheCaps()
    {
        float[] background = { 0.5f, 0.5f, 0.5f };
        var contributions = new List<UpliftContribution>
        {
            new UpliftContribution(new[] { 0.3f, -1f, 2f }, null),
            new UpliftContribution(new[] { 0.1f, 0f, 1f }, null),
        };
        float[] grade = { 1f, 1f, 0.5f };
        var caps = new List<float[]> { new[] { 10f, 10f, 0.4f } };
        var into = new float[3];
        ConditionCombine.Uplift(background, contributions, 0f, 0.02f, grade, caps, into);
        Assert.Equal(0.5f + 0.3f + 0.1f, into[0], 6);
        Assert.Equal(0.02f, into[1], 6); // the floor after the sum: a deep valley never sinks the ground
        Assert.Equal(0.4f, into[2], 6); // (0.5 + 2 + 1) * 0.5 = 1.75, capped last
    }

    [Fact]
    public void ACapHoldsHoweverManyContributionsComeBeforeIt()
    {
        var rng = new System.Random(3);
        for (int trial = 0; trial < 50; trial++)
        {
            var contributions = new List<UpliftContribution>();
            int count = rng.Next(1, 12);
            for (int c = 0; c < count; c++)
                contributions.Add(new UpliftContribution(Fill(4, (float)(rng.NextDouble() * 3 - 1)), null));
            var cap = new[] { 0.3f, 0.6f, 0.9f, 1.2f };
            var into = new float[4];
            ConditionCombine.Uplift(Fill(4, 0.5f), contributions, 0f, 0.02f, null, new List<float[]> { cap }, into);
            for (int i = 0; i < 4; i++)
                Assert.True(into[i] <= cap[i], $"trial {trial} cell {i}: {into[i]} above cap {cap[i]}");
        }
    }

    [Fact]
    public void AddingUpliftDoesNotDependOnOrder()
    {
        var a = new UpliftContribution(new[] { 0.25f }, null);
        var b = new UpliftContribution(new[] { -0.5f }, null);
        float[] x = new float[1], y = new float[1];
        ConditionCombine.Uplift(new[] { 0.75f }, new List<UpliftContribution> { a, b }, 0f, 0.02f, null, null, x);
        ConditionCombine.Uplift(new[] { 0.75f }, new List<UpliftContribution> { b, a }, 0f, 0.02f, null, null, y);
        Assert.Equal(x[0], y[0]);
    }

    [Fact]
    public void AlteringRockDoesNotDependOnOrderButAnIntrusionDoes()
    {
        var harden = new RockContribution(new[] { 1f }, RockOperation.Alter, 0.5f, null);
        var soften = new RockContribution(new[] { 1f }, RockOperation.Alter, 2f, null);
        float[] x = new float[1], y = new float[1];
        ConditionCombine.Rock(new List<RockContribution> { harden, soften }, 0f, x);
        ConditionCombine.Rock(new List<RockContribution> { soften, harden }, 0f, y);
        Assert.Equal(x[0], y[0], 6);

        // An intrusion replaces the rock it finds: the younger body wins, so time order decides, not list order.
        var olderDike = new RockContribution(new[] { 1f }, RockOperation.Intrude, 0.2f, new TimeWindow { start = 0.1f, end = 1f });
        var youngerPlug = new RockContribution(new[] { 1f }, RockOperation.Intrude, 0.6f, new TimeWindow { start = 0.5f, end = 1f });
        ConditionCombine.Rock(new List<RockContribution> { youngerPlug, olderDike }, 0.9f, x);
        Assert.Equal(0.6f, x[0], 5);
    }

    [Fact]
    public void EquallyOldRockContributionsKeepTheirListOrder()
    {
        var first = new RockContribution(new[] { 1f }, RockOperation.Intrude, 0.2f, null);
        var second = new RockContribution(new[] { 1f }, RockOperation.Intrude, 0.7f, null);
        List<RockContribution> ordered = ConditionCombine.InTimeOrder(new List<RockContribution> { first, second });
        Assert.Equal(0.2f, ordered[0].factor);
        Assert.Equal(0.7f, ordered[1].factor);
    }

    [Fact]
    public void TodaysLandformsCombineExactlyAsBefore()
    {
        // The old code: u = background; per landform u += inside*w + outside*(1-w); k *= Lerp(1, erodibility, w); then Max(0.02, u).
        float background = 0.6f, w1 = 0.37f, w2 = 0.81f;
        float u = background;
        u += -0.5f * w1 + 0.8f * (1f - w1);
        u += 1.2f * w2 + 0f * (1f - w2);
        u = Mathf.Max(0.02f, u);
        float k = 1f;
        k *= Mathf.Lerp(1f, 1f, w1);
        k *= Mathf.Lerp(1f, 0.4f, w2);

        var uplift = new List<UpliftContribution>
        {
            new UpliftContribution(new[] { -0.5f * w1 + 0.8f * (1f - w1) }, null),
            new UpliftContribution(new[] { 1.2f * w2 + 0f * (1f - w2) }, null),
        };
        float[] newU = new float[1], newK = new float[1];
        ConditionCombine.Uplift(new[] { background }, uplift, 0f, 0.02f, null, null, newU);
        ConditionCombine.Rock(new List<RockContribution> { new RockContribution(new[] { w2 }, RockOperation.Alter, 0.4f, null) }, 0f, newK);
        Assert.Equal(u, newU[0]);
        Assert.Equal(k, newK[0]);
    }

    [Fact]
    public void AContributionWithAWindowFadesWithIt()
    {
        var window = new TimeWindow { start = 0.4f, end = 0.8f, ramp = 0.1f };
        var contributions = new List<UpliftContribution> { new UpliftContribution(new[] { 1f }, window) };
        var into = new float[1];
        ConditionCombine.Uplift(new[] { 0.5f }, contributions, 0.2f, 0.02f, null, null, into);
        Assert.Equal(0.5f, into[0], 6);
        ConditionCombine.Uplift(new[] { 0.5f }, contributions, 0.6f, 0.02f, null, null, into);
        Assert.Equal(1.5f, into[0], 6);
        Assert.True(ConditionCombine.ChangesDuringRun(contributions, new List<RockContribution>()));
        Assert.False(ConditionCombine.ChangesDuringRun(new List<UpliftContribution> { new UpliftContribution(new[] { 1f }, null) }, new List<RockContribution>()));
    }

    // ---- The overlay's weights (map position, warp, peak): the same pure functions the generator lays regions with ----

    [Fact]
    public void MapFractionDividesBothAxesByTheWidth()
    {
        Assert.Equal(new Vector2(0f, 0f), FeatureShapeMath.MapFraction(125f, 100f, 250, 200));
        Vector2 corner = FeatureShapeMath.MapFraction(250f, 200f, 250, 200);
        Assert.Equal(0.5f, corner.x, 6);
        Assert.Equal(0.4f, corner.y, 6);
        // Positions between cells are not rounded.
        Assert.Equal(0.001f, FeatureShapeMath.MapFraction(125.25f, 100f, 250, 200).x, 6);
    }

    [Fact]
    public void WarpedWeightMovesThePositionByTheRegionsShareOfTheWarp()
    {
        var area = new RegionShape { kind = ShapeKind.Area, origin = new Vector2(0.1f, 0f), size = 0.2f, edge = 0.05f, warp = 0.1f };
        // This warp brings the map centre onto the region's origin: its core.
        Assert.Equal(1f, FeatureShapeMath.WarpedWeight(area, Vector2.zero, new Vector2(1f, 0f)));
        // This one moves it far outside.
        Assert.Equal(0f, FeatureShapeMath.WarpedWeight(area, Vector2.zero, new Vector2(-3f, 0f)));
        var p = new Vector2(0.2f, 0.15f);
        var warp = new Vector2(0.5f, -0.25f);
        Assert.Equal(area.WeightAt(new Vector2(0.15f, 0.125f)), FeatureShapeMath.WarpedWeight(area, p, warp), 5);
        area.warp = 0f;
        Assert.Equal(area.WeightAt(p - area.origin), FeatureShapeMath.WarpedWeight(area, p, warp));
    }

    [Fact]
    public void TheWorldRiverValleyPeaksBelowOne()
    {
        // Its edge (0.1) is wider than its half-width (0.05), so even its middle is inside the falloff.
        Assert.Equal(0.84375f, FeatureShapeMath.PeakWeight(ShapeKind.Path, BandSide.Both, 0.05f, 0.1f), 5);
    }

    [Theory]
    [InlineData(ShapeKind.Path, BandSide.Both)]
    [InlineData(ShapeKind.Area, BandSide.Both)]
    [InlineData(ShapeKind.Band, BandSide.Both)]
    [InlineData(ShapeKind.Band, BandSide.Inside)]
    [InlineData(ShapeKind.Ring, BandSide.Both)]
    [InlineData(ShapeKind.Point, BandSide.Both)]
    [InlineData(ShapeKind.Scatter, BandSide.Both)]
    public void PeakWeightIsTheMostTheShapeReaches(ShapeKind kind, BandSide side)
    {
        foreach ((float size, float edge) in new[] { (0.05f, 0.1f), (0.2f, 0.05f), (0.1f, 0f) })
        {
            var shape = new RegionShape
            {
                kind = kind, side = side, direction = new Vector2(1f, 0f), size = size, edge = edge, radius = 0.3f,
                points = new List<Vector2> { new Vector2(0.2f, 0f) },
            };
            float most = 0f;
            for (int x = -100; x <= 100; x++)
            {
                for (int y = -100; y <= 100; y++)
                    most = Math.Max(most, shape.WeightAt(new Vector2(x * 0.01f, y * 0.01f)));
            }
            Assert.Equal(FeatureShapeMath.PeakWeight(kind, side, size, edge), most, 5);
        }
    }

    [Fact]
    public void RelativeWeightIsAShareOfThePeak()
    {
        Assert.Equal(0.5f, FeatureShapeMath.RelativeWeight(0.421875f, 0.84375f), 6);
        Assert.Equal(1f, FeatureShapeMath.RelativeWeight(0.9f, 0.84375f));
        Assert.Equal(0f, FeatureShapeMath.RelativeWeight(0.5f, 0f));
    }
}
