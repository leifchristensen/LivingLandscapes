using LivingLandscapes.Geology;
using Xunit;

namespace LivingLandscapes.Tests;

/// <summary>
/// One standing-water test (#156): for a table of (filled, natural, depth) every caller (Confluences' exclusion, the budget's
/// base level, Paint's hand-over, the feedback passes) reads the same answer, and a hand-over implies base level.
/// </summary>
public class StandingTests
{
    private const float MinDepth = 0.15f, Tolerance = 0.05f, SpringDepth = 0.1f;

    // filled, natural, depth, expected standing
    public static TheoryData<float, float, float, bool> Table => new()
    {
        { 10.00f, 10.00f, 0.50f, false },  // no hollow
        { 10.30f, 10.00f, 0.50f, false },  // shallower than the channel
        { 10.54f, 10.00f, 0.50f, false },  // depth < filled - natural <= depth + tolerance: the old gap, now flowing for all callers
        { 10.56f, 10.00f, 0.50f, true },   // just past depth + tolerance
        { 10.18f, 10.00f, 0.02f, false },  // minDepth floors a thin channel: 0.18 < 0.15 + 0.05
        { 10.21f, 10.00f, 0.02f, true },
        { 12.00f, 10.00f, 1.00f, true },   // deep hollow
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void EveryCallerAgrees(float filled, float natural, float depth, bool expected)
    {
        var standing = new Standing(new[] { filled }, null, MinDepth, Tolerance, SpringDepth);
        bool budget = standing.Is(0, natural, depth);
        bool junctions = standing.Is(0, natural, depth);
        bool paint = standing.HandedToLake(0, natural, depth, holdsLake: true);
        Assert.Equal(expected, budget);
        Assert.Equal(budget, junctions);
        Assert.Equal(expected, paint);
        // A cell handed to the lake model is always standing (invariant 8).
        Assert.True(!paint || budget);
        // A hollow the lake model refuses is not handed over, whatever its depth.
        Assert.False(standing.HandedToLake(0, natural, depth, holdsLake: false));
    }

    [Fact]
    public void SeaIsAlwaysStandingButNeverHandedToTheLakeModel()
    {
        var standing = new Standing(new[] { 5f }, new[] { true }, MinDepth, Tolerance, SpringDepth);
        Assert.True(standing.Is(0, 5f, 1f));
        Assert.False(standing.HandedToLake(0, 5f, 1f, true));
    }

    [Fact]
    public void SpringHollowUsesItsOwnShallowerThreshold()
    {
        var standing = new Standing(new[] { 10.12f }, null, MinDepth, Tolerance, SpringDepth);
        Assert.True(standing.InSpringHollow(0, 10f));
        Assert.False(standing.Is(0, 10f, 0.5f));
    }

    [Fact]
    public void ScourDepthIsNotAnInput()
    {
        // The decision reads the depth it is given; callers pass the pre-scour depth (amendment 1). A deeper (scoured) depth would flip it.
        var standing = new Standing(new[] { 10.40f }, null, MinDepth, Tolerance, SpringDepth);
        Assert.True(standing.Is(0, 10f, 0.2f));
        Assert.False(standing.Is(0, 10f, 0.2f * 2f));
    }
}
