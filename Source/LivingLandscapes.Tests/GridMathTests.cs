using System;
using System.Linq;
using LivingLandscapes.Geology;
using Xunit;

namespace LivingLandscapes.Tests;

/// <summary>
/// Properties of the raster operations in <see cref="GridMath"/>, not snapshots of today's floats: the constants behind
/// them get tuned often, the contracts in their summaries do not. Grids are row-major, index = z * sizeX + x.
/// </summary>
public class GridMathTests
{
    private const float Tolerance = 1e-4f;

    private static float[] Plane(int sizeX, int sizeZ, Func<int, int, float> height)
    {
        var h = new float[sizeX * sizeZ];
        for (int z = 0; z < sizeZ; z++)
            for (int x = 0; x < sizeX; x++)
                h[z * sizeX + x] = height(x, z);
        return h;
    }

    /// <summary>Reproducible rough terrain: noise with a fixed seed, so a failure replays exactly.</summary>
    private static float[] RoughTerrain(int sizeX, int sizeZ, int seed)
    {
        var random = new Random(seed);
        return Plane(sizeX, sizeZ, (x, z) => 10f + (float)random.NextDouble() * 5f);
    }

    private static bool OnEdge(int i, int sizeX, int sizeZ) =>
        i % sizeX == 0 || i / sizeX == 0 || i % sizeX == sizeX - 1 || i / sizeX == sizeZ - 1;

    // ---- Blur ----

    [Fact]
    public void Blur_KeepsAConstantFieldConstant_EvenAtTheEdges()
    {
        var blurred = GridMath.Blur(Plane(20, 15, (x, z) => 7f), 20, 15, 2.5f);
        Assert.All(blurred, v => Assert.Equal(7f, v, 3));
    }

    [Fact]
    public void Blur_NeverLeavesTheRangeOfTheInput()
    {
        var input = RoughTerrain(32, 32, seed: 1);
        var blurred = GridMath.Blur(input, 32, 32, 3f);
        Assert.True(blurred.Min() >= input.Min() - Tolerance);
        Assert.True(blurred.Max() <= input.Max() + Tolerance);
    }

    [Fact]
    public void Blur_SpreadsAnImpulseSymmetricallyAroundItsPeak()
    {
        var input = new float[21 * 21];
        input[10 * 21 + 10] = 1f;
        var blurred = GridMath.Blur(input, 21, 21, 2f);
        Assert.Equal(blurred.Max(), blurred[10 * 21 + 10], 6);
        Assert.Equal(blurred[10 * 21 + 12], blurred[10 * 21 + 8], 5);
        Assert.Equal(blurred[12 * 21 + 10], blurred[8 * 21 + 10], 5);
        Assert.Equal(blurred[10 * 21 + 12], blurred[12 * 21 + 10], 5);
    }

    // ---- MaskedBlur ----

    [Fact]
    public void MaskedBlur_WithEveryCellWeighted_EqualsPlainBlur()
    {
        var input = RoughTerrain(24, 24, seed: 2);
        var ones = Plane(24, 24, (x, z) => 1f);
        var masked = GridMath.MaskedBlur(input, ones, 24, 24, 2f);
        var plain = GridMath.Blur(input, 24, 24, 2f);
        for (int i = 0; i < input.Length; i++)
            Assert.Equal(plain[i], masked[i], 3);
    }

    [Fact]
    public void MaskedBlur_FillsMaskedCellsFromTheirSurroundings_NotFromTheirOwnValue()
    {
        var input = Plane(21, 21, (x, z) => 10f);
        var weights = Plane(21, 21, (x, z) => 1f);
        input[10 * 21 + 10] = 999f;
        weights[10 * 21 + 10] = 0f;
        var result = GridMath.MaskedBlur(input, weights, 21, 21, 2f);
        Assert.Equal(10f, result[10 * 21 + 10], 3);
    }

    [Fact]
    public void MaskedBlur_ReachesAcrossAMaskedAreaWiderThanTheBlur()
    {
        var input = Plane(40, 40, (x, z) => 5f);
        var weights = Plane(40, 40, (x, z) => x >= 8 && x < 32 ? 0f : 1f);
        var result = GridMath.MaskedBlur(input, weights, 40, 40, 1.5f);
        Assert.All(result, v => Assert.Equal(5f, v, 2));
    }

    // ---- DistanceTo ----

    [Fact]
    public void DistanceTo_IsZeroOnSourcesOneNextToThemAndRootTwoOnDiagonals()
    {
        int source = 5 * 11 + 5;
        var d = GridMath.DistanceTo(i => i == source, 11, 11);
        Assert.Equal(0f, d[source]);
        Assert.Equal(1f, d[5 * 11 + 6], 5);
        Assert.Equal(1f, d[4 * 11 + 5], 5);
        Assert.Equal(MathF.Sqrt(2f), d[6 * 11 + 6], 5);
        Assert.Equal(3f, d[5 * 11 + 8], 5);
    }

    [Fact]
    public void DistanceTo_ReportsFarWhereThereIsNoSource()
    {
        var d = GridMath.DistanceTo(i => false, 6, 6, far: 123f);
        Assert.All(d, v => Assert.Equal(123f, v));
    }

    [Fact]
    public void DistanceTo_NeverExceedsTheStraightLineByMoreThanTheChamferError()
    {
        // Chamfer 1/sqrt2 overestimates Euclidean distance by at most about 8%.
        int sizeX = 41, sizeZ = 41, source = 20 * sizeX + 20;
        var d = GridMath.DistanceTo(i => i == source, sizeX, sizeZ);
        for (int i = 0; i < d.Length; i++)
        {
            float euclid = MathF.Sqrt(MathF.Pow(i % sizeX - 20, 2) + MathF.Pow(i / sizeX - 20, 2));
            Assert.True(d[i] >= euclid - Tolerance, $"cell {i}: {d[i]} < {euclid}");
            Assert.True(d[i] <= euclid * 1.09f + Tolerance, $"cell {i}: {d[i]} > {euclid} * 1.09");
        }
    }

    // ---- Depression filling and breaching ----

    [Fact]
    public void FillDepressions_RaisesAPitToItsRim_AndLowersNothing()
    {
        var h = Plane(9, 9, (x, z) => 10f);
        h[4 * 9 + 4] = 1f;
        var before = (float[])h.Clone();
        GridMath.FillDepressions(h, 9, 9, i => false, epsilon: 1e-3f);
        Assert.True(h[4 * 9 + 4] >= 10f);
        for (int i = 0; i < h.Length; i++)
            Assert.True(h[i] >= before[i] - Tolerance, $"cell {i} was lowered");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FillDepressions_GivesEveryCellAStrictlyDownhillStepToAnOutlet(int seed)
    {
        const int size = 30;
        const float epsilon = 1e-3f;
        var h = RoughTerrain(size, size, seed);
        GridMath.FillDepressions(h, size, size, i => false, epsilon);
        for (int i = 0; i < h.Length; i++)
        {
            if (OnEdge(i, size, size))
                continue;
            bool hasLower = false;
            GridMath.ForEachNeighbour(i, size, size, n => hasLower |= h[n] < h[i]);
            Assert.True(hasLower, $"cell {i} (seed {seed}) is a pit after filling");
        }
    }

    [Fact]
    public void FillDepressions_TreatsDesignatedOutletsLikeTheMapEdge()
    {
        // A bowl in the middle with a designated outlet cell inside it: it drains there instead of filling to the rim.
        int size = 15, centre = 7 * size + 7;
        var h = Plane(size, size, (x, z) => 10f);
        h[centre] = 2f;
        GridMath.FillDepressions(h, size, size, i => i == centre, epsilon: 1e-3f);
        Assert.Equal(2f, h[centre], 5);
    }

    [Fact]
    public void FillDepressions_WithClosedEdgesAndNoOutlets_LeavesTheMapAlone_OrWallsItIn()
    {
        // includeEdges:false makes edges walls; with no outlet at all nothing can drain, so nothing may be lowered.
        var h = RoughTerrain(12, 12, seed: 4);
        var before = (float[])h.Clone();
        GridMath.FillDepressions(h, 12, 12, i => false, 1e-3f, includeEdges: false);
        for (int i = 0; i < h.Length; i++)
            Assert.True(h[i] >= before[i] - Tolerance);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void BreachDepressions_LeavesNoPitsAndNeverRaisesAboveTheFilledSurface(int seed)
    {
        const int size = 30;
        const float epsilon = 1e-3f;
        var h = RoughTerrain(size, size, seed);
        var filled = (float[])h.Clone();
        GridMath.FillDepressions(filled, size, size, i => false, epsilon);
        GridMath.BreachDepressions(h, size, size, i => false, epsilon, maxBreachDepth: 0.5f);
        for (int i = 0; i < h.Length; i++)
        {
            Assert.True(h[i] <= filled[i] + Tolerance, $"cell {i} (seed {seed}) above the filled surface");
            if (OnEdge(i, size, size))
                continue;
            bool hasLower = false;
            GridMath.ForEachNeighbour(i, size, size, n => hasLower |= h[n] < h[i]);
            Assert.True(hasLower, $"cell {i} (seed {seed}) is a pit after breaching");
        }
    }

    // ---- Flow ----

    [Fact]
    public void FlowAccumulationMfd_DeliversEveryCellsWaterToSomeSink()
    {
        // Conservation: with no inflow, the area reaching cells that have no lower neighbour is every cell once.
        const int size = 30;
        var h = RoughTerrain(size, size, seed: 5);
        GridMath.FillDepressions(h, size, size, i => false, 1e-3f);
        var area = GridMath.FlowAccumulationMfd(h, size, size);
        float atSinks = 0f;
        for (int i = 0; i < h.Length; i++)
        {
            bool sink = true;
            GridMath.ForEachNeighbour(i, size, size, n => sink &= !(h[n] < h[i]));
            if (sink)
                atSinks += area[i];
        }
        Assert.Equal(size * size, atSinks, 1);
    }

    [Fact]
    public void FlowAccumulationMfd_OnATiltedPlane_GrowsDownslopeAndNeverBelowOneCell()
    {
        const int size = 20;
        var h = Plane(size, size, (x, z) => x);
        var area = GridMath.FlowAccumulationMfd(h, size, size);
        Assert.All(area, v => Assert.True(v >= 1f - Tolerance));
        float upslopeColumn = Enumerable.Range(0, size).Sum(z => area[z * size + size - 1]);
        float downslopeColumn = Enumerable.Range(0, size).Sum(z => area[z * size + 0]);
        Assert.Equal(size, upslopeColumn, 3);
        Assert.True(downslopeColumn > upslopeColumn * 5f);
    }

    [Fact]
    public void FlowAccumulationMfd_AddsInflowWhereItEnters()
    {
        const int size = 10;
        var h = Plane(size, size, (x, z) => x);
        var inflow = new float[size * size];
        inflow[5 * size + 9] = 100f;
        var without = GridMath.FlowAccumulationMfd(h, size, size);
        var with = GridMath.FlowAccumulationMfd(h, size, size, inflow: inflow);
        Assert.Equal(100f, with[5 * size + 9] - without[5 * size + 9], 3);
        Assert.True(with[5 * size + 0] > without[5 * size + 0]);
    }

    [Fact]
    public void Receivers_PointDownhill_AndAreMinusOneOnlyWhereNothingIsLower()
    {
        const int size = 12;
        var h = RoughTerrain(size, size, seed: 6);
        var receivers = GridMath.Receivers(h, size, size);
        for (int i = 0; i < h.Length; i++)
        {
            if (receivers[i] == -1)
            {
                GridMath.ForEachNeighbour(i, size, size, n => Assert.True(h[n] >= h[i]));
            }
            else
            {
                Assert.True(h[receivers[i]] < h[i]);
                // It is the steepest of the lower neighbours.
                int r = receivers[i];
                float rx = r % size - i % size, rz = r / size - i / size;
                float best = (h[i] - h[r]) / MathF.Sqrt(rx * rx + rz * rz);
                GridMath.ForEachNeighbour(i, size, size, n =>
                {
                    float dx = n % size - i % size, dz = n / size - i / size;
                    Assert.True((h[i] - h[n]) / MathF.Sqrt(dx * dx + dz * dz) <= best + Tolerance);
                });
            }
        }
    }

    // ---- Slope and aspect ----

    [Fact]
    public void Slope_OfAPlane_IsItsGradientEverywhere_IncludingTheEdges()
    {
        var h = Plane(16, 16, (x, z) => 0.5f * x + 1.2f * z);
        var slope = GridMath.Slope(h, 16, 16);
        float expected = MathF.Sqrt(0.5f * 0.5f + 1.2f * 1.2f);
        Assert.All(slope, v => Assert.Equal(expected, v, 4));
    }

    [Theory]
    [InlineData(-1f, 0f, 0f)]                       // falls toward +x: east
    [InlineData(1f, 0f, MathF.PI)]                  // falls toward -x: west
    [InlineData(0f, -1f, MathF.PI / 2f)]            // falls toward +z: north
    [InlineData(0f, 1f, 3f * MathF.PI / 2f)]        // falls toward -z: south
    public void Aspect_IsTheDownhillDirection_CounterClockwiseFromEast(float gradX, float gradZ, float expected)
    {
        var h = Plane(10, 10, (x, z) => gradX * x + gradZ * z);
        var aspect = GridMath.Aspect(h, 10, 10);
        Assert.All(aspect, a => Assert.Equal(expected, a, 4));
    }

    [Fact]
    public void Aspect_StaysInZeroToTwoPi()
    {
        var rough = GridMath.Aspect(RoughTerrain(20, 20, seed: 7), 20, 20);
        Assert.All(rough, a => Assert.InRange(a, 0f, 2f * MathF.PI + Tolerance));
    }

    // Found by this test: Aspect's summary says "Flat cells get 0", but Atan2(-0, -0) is -pi in IEEE arithmetic, so a
    // flat cell gets pi (west). Harmless for SlopeToward (slope is 0 there), wrong for anything reading aspect alone.
    // Filed as #79; remove the Skip once GridMath returns 0 for flat cells.
    [Fact(Skip = "#79: GridMath.Aspect returns pi, not 0, on flat ground (documented contract)")]
    public void Aspect_IsZeroOnFlatGround()
    {
        Assert.All(GridMath.Aspect(Plane(5, 5, (x, z) => 3f), 5, 5), a => Assert.Equal(0f, a));
    }

    [Fact]
    public void SlopeToward_IsFullFacingIt_ZeroAcrossIt_NegativeAgainstIt()
    {
        Assert.Equal(2f, GridMath.SlopeToward(2f, 0f, 0f), 5);
        Assert.Equal(0f, GridMath.SlopeToward(2f, 0f, MathF.PI / 2f), 5);
        Assert.Equal(-2f, GridMath.SlopeToward(2f, 0f, MathF.PI), 5);
    }

    // ---- Regions and neighbourhoods ----

    [Fact]
    public void LabelRegions_UsesFourConnectivity_SoDiagonalCellsAreSeparateRegions()
    {
        // Two blobs, plus one cell touching a blob only diagonally.
        const int size = 8;
        var member = new bool[size * size];
        foreach (var (x, z) in new[] { (1, 1), (2, 1), (1, 2), (6, 6), (6, 5), (2, 2) })
            member[z * size + x] = true;
        member[3 * size + 3] = true; // diagonal to (2,2)
        int count = GridMath.LabelRegions(i => member[i], size, size, out int[] labels);
        Assert.Equal(3, count);
        Assert.Equal(labels[1 * size + 1], labels[2 * size + 2]);
        Assert.NotEqual(labels[2 * size + 2], labels[3 * size + 3]);
        Assert.Equal(-1, labels[0]);
    }

    [Theory]
    [InlineData(0, 0, 3)]
    [InlineData(5, 0, 5)]
    [InlineData(5, 5, 8)]
    [InlineData(9, 9, 3)]
    public void ForEachNeighbour_VisitsEveryInBoundsNeighbourOnce(int x, int z, int expected)
    {
        var seen = new System.Collections.Generic.List<int>();
        GridMath.ForEachNeighbour(z * 10 + x, 10, 10, seen.Add);
        Assert.Equal(expected, seen.Count);
        Assert.Equal(seen.Count, seen.Distinct().Count());
        Assert.DoesNotContain(z * 10 + x, seen);
    }

    [Fact]
    public void Strips_CountsCellsWithAtMostTwoMarkedNeighbours()
    {
        // A one-cell-wide line of 5: every cell has at most 2 neighbours.
        var line = new bool[10 * 10];
        for (int x = 2; x < 7; x++)
            line[5 * 10 + x] = true;
        Assert.Equal(5, GridMath.Strips(line, 10, 10));

        // A 3x3 block: corners have 3 neighbours, edges 5, the centre 8: none is a strip.
        var block = new bool[10 * 10];
        for (int z = 4; z < 7; z++)
            for (int x = 4; x < 7; x++)
                block[z * 10 + x] = true;
        Assert.Equal(0, GridMath.Strips(block, 10, 10));
    }
}
