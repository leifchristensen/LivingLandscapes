using LivingLandscapes.Geology;
using Xunit;

namespace LivingLandscapes.Tests;

public class RiverEntryRulesTests
{
    [Theory]
    [InlineData(0, true)]            // corner
    [InlineData(5, true)]            // top row
    [InlineData(4 * 10 + 9, true)]   // right column
    [InlineData(4 * 10 + 5, false)]  // interior
    [InlineData(9 * 10 + 5, true)]   // bottom row
    public void EdgeCellIsOnTheBorder(int cell, bool edge) => Assert.Equal(edge, RiverEntryRules.IsEdgeCell(cell, 10, 10));

    [Fact]
    public void SpringInsideTheMapIsNotAnEntry() => Assert.False(RiverEntryRules.IsEdgeCell(3 * 10 + 4, 10, 8));

    // Channel 0 is the world river; 1 joins it; 2 joins 1; 3 leaves the map on its own; 4 joins 1 but ends outside the map.
    private static readonly int[] Parent = { -1, 0, 1, -1, 1 };
    private static readonly bool[] World = { true, false, false, false, false };
    private static readonly bool[] Inside = { true, true, true, true, false };

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    public void JoinsFollowsParentsToTheWorldRiver(int channel, bool joins) =>
        Assert.Equal(joins, RiverEntryRules.Joins(channel, Parent, World, Inside));

    [Fact]
    public void ParentLoopDoesNotJoinOrHang() =>
        Assert.False(RiverEntryRules.Joins(0, new[] { 1, 0 }, new[] { false, false }, new[] { true, true }));

    // The boundary condition of a world river: water enters at the map's edge, where the course crosses it.
    [Theory]
    [InlineData(114f, 250f, 114f, 248f, 250, 250, 114f, 249.99f)]  // north edge, model point one cell in from the edge
    [InlineData(-2f, 72f, 2f, 74f, 250, 250, 0f, 73f)]             // west edge
    [InlineData(250f, 148f, 248f, 146f, 250, 250, 249.99f, 147.99f)] // east edge
    public void WorldRiverEntersOnTheEdgeCell(float ox, float oz, float ix, float iz, int sx, int sz, float ex, float ez)
    {
        RiverEntryRules.EdgeCrossing(ox, oz, ix, iz, sx, sz, out float x, out float z);
        Assert.InRange(x, ex - 0.05f, ex + 0.05f);
        Assert.InRange(z, ez - 0.05f, ez + 0.05f);
        Assert.True(RiverEntryRules.IsEdgeCell((int)z * sx + (int)x, sx, sz));
    }

    // A chain of cells that all carry the same drainage area (a float cannot tell them apart) still comes donors first.
    [Fact]
    public void DonorsComeBeforeReceiversWhateverTheirAreas()
    {
        // 0 -> 1 -> 2 -> 3 (outlet), with 4 -> 2 as a tributary; cell 5 is not a member.
        int[] receiver = { 1, 2, 3, -1, 2, -1 };
        System.Collections.Generic.List<int> order = RiverEntryRules.DonorsFirst(receiver, c => c != 5);
        Assert.Equal(5, order.Count);
        foreach (int c in order)
        {
            if (receiver[c] >= 0 && receiver[c] != 5)
                Assert.True(order.IndexOf(c) < order.IndexOf(receiver[c]));
        }
    }

    // A 10x10 model grid with the map window on x 3..7, z 3..7.
    private static int[] Chain(params (int x, int z)[] cells)
    {
        var receiver = new int[100];
        for (int i = 0; i < receiver.Length; i++)
            receiver[i] = -1;
        for (int i = 0; i + 1 < cells.Length; i++)
            receiver[cells[i].z * 10 + cells[i].x] = cells[i + 1].z * 10 + cells[i + 1].x;
        return receiver;
    }

    [Fact]
    public void StraightChainCrossesFromWestToEast()
    {
        var r = Chain((0, 5), (1, 5), (2, 5), (3, 5), (4, 5), (5, 5), (6, 5), (7, 5), (8, 5), (9, 5));
        var c = RiverEntryRules.CrossWindow(r, 10, 3, 3, 4, 4, 5 * 10);
        Assert.Equal(5 * 10 + 3, c.FirstCell);
        Assert.Equal('W', c.FirstSide);
        Assert.Equal(5 * 10 + 7, c.LastCell);
        Assert.Equal('E', c.LastSide);
        Assert.False(c.EndsInside);
        Assert.Equal(5, c.InsideCells);
    }

    [Fact]
    public void ChainThatLeavesAndReEntersReportsFirstAndLastWindowCells()
    {
        var r = Chain((1, 5), (3, 5), (4, 5), (5, 5), (5, 8), (7, 8), (7, 5), (9, 5));
        var c = RiverEntryRules.CrossWindow(r, 10, 3, 3, 4, 4, 5 * 10 + 1);
        Assert.Equal(5 * 10 + 3, c.FirstCell);
        Assert.Equal('W', c.FirstSide);
        Assert.Equal(5 * 10 + 7, c.LastCell);
        Assert.Equal('E', c.LastSide);
        Assert.False(c.EndsInside);
        Assert.Equal(4, c.InsideCells);
    }

    [Fact]
    public void ChainEndingInsideTheWindowSaysSo()
    {
        var r = Chain((1, 5), (3, 5), (5, 5));
        var c = RiverEntryRules.CrossWindow(r, 10, 3, 3, 4, 4, 5 * 10 + 1);
        Assert.Equal(5 * 10 + 5, c.LastCell);
        Assert.Equal(5 * 10 + 5, c.EndCell);
        Assert.True(c.EndsInside);
    }

    [Fact]
    public void ChainOutsideTheWindowHasNoCrossing()
    {
        var c = RiverEntryRules.CrossWindow(Chain((0, 0), (1, 0), (2, 0)), 10, 3, 3, 4, 4, 0);
        Assert.Equal(-1, c.FirstCell);
        Assert.False(c.EndsInside);
    }

    [Fact]
    public void ChainsMeetAtTheFirstSharedCell()
    {
        var r = Chain((0, 5), (1, 5), (2, 5), (3, 5));
        r[0 * 10 + 2] = 5 * 10 + 2; // a second chain from (2,0) joins (2,5)
        Assert.Equal(5 * 10 + 2, RiverEntryRules.ChainsMeet(r, 5 * 10, 2));
        Assert.Equal(-1, RiverEntryRules.ChainsMeet(r, 5 * 10, 9 * 10 + 9));
    }
}
