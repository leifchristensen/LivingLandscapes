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
}
