using System.Linq;
using LivingLandscapes.Geology;
using Xunit;

namespace LivingLandscapes.Tests;

public class RiverBedTests
{
    [Theory]
    [InlineData(new float[] { 10, 9, 8, 7, 6 })]        // falling ground
    [InlineData(new float[] { 5, 6, 7, 8, 9 })]         // rising ground
    [InlineData(new float[] { 8, 3, 9, 2, 10, 1, 7 })]  // bumpy ground
    public void BedNeverClimbsNeverLiesAboveTheGroundAndEndsAtTheBaseLevel(float[] ground)
    {
        float[] bed = RiverBed.Grade(ground, 0.5f, 0f);
        for (int k = 1; k < bed.Length; k++)
            Assert.True(bed[k] <= bed[k - 1] + 1e-6f, $"bed climbs at {k}");
        for (int k = 0; k < ground.Length; k++)
        {
            Assert.True(bed[k] <= ground[k] - 0.5f + 1e-5f || bed[k] <= 1e-5f);
            Assert.True(bed[k] >= -1e-6f, "bed below its outlet's base level");
        }
        Assert.Equal(0f, bed[bed.Length - 1], 5);
    }

    [Fact]
    public void BedNeverSinksBelowTheOutletOnALongFlatCourse()
    {
        // 200 cells over ground 1 m above the outlet: an even fall of a fixed amount per cell would end far below base level.
        float[] bed = RiverBed.Grade(Enumerable.Repeat(1f, 200).ToArray(), 0.1f, 0f);
        Assert.True(bed.Min() >= 0f);
        Assert.Equal(0f, bed[199], 5);
        Assert.True(bed[0] > bed[100] && bed[100] > bed[198]);
    }

    [Fact]
    public void GroundLowerThanTheOutletGivesAFlatBedAtTheBaseLevel()
    {
        float[] bed = RiverBed.Grade(new float[] { 0.2f, 0.1f, 0f, 0f }, 0.3f, 1f);
        Assert.All(bed, b => Assert.Equal(1f, b, 5));
    }

    [Fact]
    public void AlsoWorksWithABaseLevelAboveZeroAndOneCell()
    {
        float[] bed = RiverBed.Grade(new float[] { 12, 11, 10 }, 1f, 4f);
        Assert.Equal(4f, bed[2], 5);
        Assert.True(bed[0] <= 11f && bed[0] > bed[1]);
        Assert.Equal(4f, RiverBed.Grade(new float[] { 9 }, 1f, 4f)[0], 5);
    }
}

public class PrescribedCourseTests
{
    private static int[] Receivers(int n, params int[][] legs) => PrescribedCourse.PrescribedReceivers(n, legs);

    [Fact]
    public void CutDownhillWithAFallLeavesEveryCellStrictlyBelowTheOneBefore()
    {
        float[] g = { 5f, 5f, 6f, 1f };
        PrescribedCourse.CutDownhill(g, Receivers(4, new[] { 0, 1, 2, 3 }), 0.5f);
        Assert.Equal(new[] { 5f, 4.5f, 4f, 1f }, g);
    }

    [Fact]
    public void CutDownhillLowersAClimbingCellToItsUpstreamNeighbour()
    {
        float[] g = { 5f, 3f, 4f, 2f };
        int lowered = PrescribedCourse.CutDownhill(g, Receivers(4, new[] { 0, 1, 2, 3 }));
        Assert.Equal(new[] { 5f, 3f, 3f, 2f }, g);
        Assert.Equal(1, lowered);
    }

    [Fact]
    public void CutDownhillSettlesWhereLegsShareACell()
    {
        // Leg B joins at cell 2 from a lower cell 4; leg A's next cell 3 must then not exceed the lowered cell 2.
        float[] g = { 5f, 4f, 4f, 4f, 1f };
        PrescribedCourse.CutDownhill(g, Receivers(5, new[] { 0, 1, 2, 3 }, new[] { 4, 2 }));
        Assert.Equal(new[] { 5f, 4f, 1f, 1f, 1f }, g);
    }

    [Fact]
    public void CutDownhillFollowsTheBoundTreeWhereTwoLegsCrossCellsInOppositeOrder()
    {
        // Leg B passes cells 1 and 2 the other way round; the first leg decides (1 -> 2), so cell 2 ends below cell 1 and
        // every bound step falls.
        float[] g = { 9f, 5f, 6f, 8f, 1f };
        int[] r = Receivers(5, new[] { 0, 1, 2, 4 }, new[] { 3, 2, 1, 4 });
        PrescribedCourse.CutDownhill(g, r, 0.1f);
        for (int c = 0; c < 5; c++)
        {
            if (r[c] >= 0)
                Assert.True(g[r[c]] < g[c], $"cell {c} -> {r[c]}: {g[c]} then {g[r[c]]}");
        }
    }

    [Fact]
    public void LegsAreBoundCellToNextCell()
    {
        int[] r = Receivers(10, new[] { 1, 2, 3 });
        Assert.Equal(new[] { -1, 2, 3, -1 }, new[] { r[0], r[1], r[2], r[3] });
    }

    [Fact]
    public void TributariesJoinTheExitLegAtTheSharedMeetingCell()
    {
        // exit leg 5 -> 6 -> 7; tributaries 1 -> 2 -> 5 and 3 -> 4 -> 5
        int[] r = Receivers(10, new[] { 5, 6, 7 }, new[] { 1, 2, 5 }, new[] { 3, 4, 5 });
        Assert.Equal(6, r[5]);
        Assert.Equal(2, r[1]);
        Assert.Equal(5, r[2]);
        Assert.Equal(5, r[4]);
        Assert.Equal(-1, r[7]);
        foreach (int start in new[] { 1, 3 })
        {
            int c = start;
            while (r[c] >= 0) c = r[c];
            Assert.Equal(7, c);
        }
    }

    [Fact]
    public void WhereLegsShareACellTheFirstLegDecides()
    {
        // the tributary also passes 6 but would go on to 8; the exit leg's 6 -> 7 stands
        int[] r = Receivers(10, new[] { 5, 6, 7 }, new[] { 1, 6, 8 });
        Assert.Equal(7, r[6]);
        Assert.Equal(6, r[1]);
    }

    [Fact]
    public void AStepThatWouldCloseALoopIsSkipped()
    {
        int[] r = Receivers(10, new[] { 1, 2, 3 }, new[] { 3, 1 });
        Assert.Equal(-1, r[3]);
    }

    [Fact]
    public void DedupeDropsRepeatedNeighbours() =>
        Assert.Equal(new[] { 1, 2, 3 }, PrescribedCourse.Dedupe(new[] { 1, 1, 2, 2, 3 }).ToArray());
}
