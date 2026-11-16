using System.Linq;
using LivingLandscapes.Geology;
using Xunit;

namespace LivingLandscapes.Tests;

public class RiverBedTests
{
    private static void AssertNeverClimbs(float[] bed)
    {
        for (int k = 1; k < bed.Length; k++)
            Assert.True(bed[k] < bed[k - 1], $"bed climbs or stalls at {k}");
    }

    [Theory]
    [InlineData(new float[] { 10, 9, 8, 7, 6 })]        // falling ground
    [InlineData(new float[] { 5, 6, 7, 8, 9 })]         // rising ground
    [InlineData(new float[] { 8, 3, 9, 2, 10, 1, 7 })]  // bumpy ground
    public void BedNeverClimbsAndLiesBelowTheGround(float[] ground)
    {
        float[] bed = RiverBed.Grade(ground, 0.5f, 0.1f);
        AssertNeverClimbs(bed);
        for (int k = 0; k < ground.Length; k++)
            Assert.True(bed[k] <= ground[k] - 0.5f - 0.1f + 1e-5f);
    }

    [Fact]
    public void FlatGroundFallsByExactlyTheFall()
    {
        float[] bed = RiverBed.Grade(new float[] { 4, 4, 4, 4 }, 1f, 0.25f);
        for (int k = 1; k < bed.Length; k++)
            Assert.Equal(0.25f, bed[k - 1] - bed[k], 4);
    }

    [Fact]
    public void FallFollowsTheGradientAndFallsBackWhenItIsNotUsable()
    {
        Assert.Equal(0.02f, RiverBed.FallPerCell(0.002f, 10f), 5);
        Assert.Equal(RiverBed.MinFallPerCell, RiverBed.FallPerCell(0f, 10f));
        Assert.Equal(RiverBed.MinFallPerCell, RiverBed.FallPerCell(float.NaN, 10f));
        Assert.Equal(RiverBed.MinFallPerCell, RiverBed.FallPerCell(-1f, 10f));
    }

    [Fact]
    public void NanGradientStillGivesAFallingBed() =>
        AssertNeverClimbs(RiverBed.Grade(new float[] { 3, 6, 2, 8 }, 0.2f, RiverBed.FallPerCell(float.NaN, 5f)));
}

public class PrescribedCourseTests
{
    private static int[] Receivers(int n, params int[][] legs) => PrescribedCourse.PrescribedReceivers(n, legs);

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
