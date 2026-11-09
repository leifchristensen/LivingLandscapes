using System.Collections.Generic;
using LivingLandscapes.Geology;
using Xunit;

namespace LivingLandscapes.Tests;

/// <summary>
/// #217: the feedback passes' deposits must not raise a reach above the bed upstream of it (a dam turns the valley behind it into
/// a lake and the world river stops flowing). <see cref="Hydraulics.GradeRises"/> caps each receiver's rise at its donor's.
/// </summary>
public class GradedRisesTests
{
    // A straight river of five cells, highest first: 0 -> 1 -> 2 -> 3 -> 4 (4 is the exit).
    private static readonly List<int> Cells = new List<int> { 0, 1, 2, 3, 4 };
    private static readonly int[] Receiver = { 1, 2, 3, 4, -1 };
    private static readonly float[] Surface = { 5f, 4f, 3f, 2f, 1f };

    [Fact]
    public void ADepositAtTheExit_CannotStandAboveTheBedUpstream()
    {
        // The exit would rise 3 m, to 4 m: above cell 3 (2 m). It is capped at the donor's surface.
        float[] rises = { 0f, 0f, 0f, 0f, 3f };
        Hydraulics.GradeRises(Cells, Receiver, null, Surface, rises);
        Assert.Equal(1f, rises[4]);
        for (int k = 0; k + 1 < Cells.Count; k++)
            Assert.True(Surface[k] + rises[k] >= Surface[k + 1] + rises[k + 1]);
    }

    [Fact]
    public void ARiseThatKeepsTheGradeIsKept_AndTheUpperReachCarriesTheLowerOne()
    {
        // The whole reach rises together: nothing is capped.
        float[] rises = { 1f, 1f, 1f, 1f, 1f };
        Hydraulics.GradeRises(Cells, Receiver, null, Surface, rises);
        Assert.Equal(new[] { 1f, 1f, 1f, 1f, 1f }, rises);
    }

    [Fact]
    public void ACappedRise_PropagatesDownstream_AndNeverGoesNegative()
    {
        // Cell 2 rises 4 m (to 7, above cell 1's 4): capped to 1 m (to 4). Cell 3 (rise 3, to 5) is then capped at cell 2's 4 m.
        float[] rises = { 0f, 0f, 4f, 3f, 0f };
        Hydraulics.GradeRises(Cells, Receiver, null, Surface, rises);
        Assert.Equal(1f, rises[2]);
        Assert.Equal(2f, rises[3]);
        Assert.All(rises, r => Assert.True(r >= 0f));
    }

    [Fact]
    public void TheSecondaryWayIsCappedToo()
    {
        // Cell 0 divides: its second way is cell 3 (2 m), which only that way feeds and would rise 6 m above the 5 m donor.
        int[] receiver = { 1, 2, -1, 4, -1 };
        int[] secondary = { 3, -1, -1, -1, -1 };
        float[] rises = { 0f, 0f, 0f, 6f, 0f };
        Hydraulics.GradeRises(Cells, receiver, secondary, Surface, rises);
        Assert.Equal(3f, rises[3]);
    }

    [Fact]
    public void GradeRises_after_a_room_cap_still_never_rises_downstream()
    {
        // D5 (#186): a lake (cells 2-3, a flat at 3 m) whose bed is capped at its room by the accommodation rule, between a reach
        // that rises 1 m and a lower cell (4, at 2 m) that would rise 2 m. The cap only lowers a rise, and GradeRises only lowers,
        // so the graded profile still never rises downstream and the capped cells keep exactly their room.
        float[] lake = { 5f, 4f, 3f, 3f, 2f };
        float[] capped = { 1f, 1f, 0.25f, 0.125f, 2f };
        float[] rises = (float[])capped.Clone();
        Hydraulics.GradeRises(Cells, Receiver, null, lake, rises);

        Assert.Equal(0.25f, rises[2]); // the room cap is not undone by the grading
        Assert.Equal(0.125f, rises[3]);
        Assert.Equal(1.125f, rises[4]); // cell 4 may only rise to the raised lake surface, 3.125 m
        for (int k = 0; k + 1 < Cells.Count; k++)
            Assert.True(lake[k] + rises[k] >= lake[k + 1] + rises[k + 1], $"bed rises downstream at {k} -> {k + 1}");
        for (int k = 0; k < rises.Length; k++)
            Assert.True(rises[k] <= capped[k], $"cell {k} was raised above what the room cap allowed");
    }
}
