using LivingLandscapes.Geology;
using Xunit;

namespace LivingLandscapes.Tests;

/// <summary>
/// Throughflow enrichment (#187): T = C(A) x M(slope) x R(h). Properties of the equation, not snapshots: more upslope water
/// raises it, level ground (stagnant saturation) gives none, height above the drainage line lowers it.
/// </summary>
public class ThroughflowTests
{
    private static float T(float area, float gradient, float h) => Throughflow.Of(area, gradient, h, 1.5f, 3f, 0.05f, 1.5f);

    [Fact]
    public void RidgeCarriesNoThroughflow() => Assert.Equal(0f, T(0f, 0.2f, 0f));

    [Fact]
    public void StrongLineOnAGradeIsFull() => Assert.Equal(1f, T(3.5f, 0.2f, 0f), 4);

    [Fact]
    public void LevelSaturatedGroundIsStagnantNotFlowing() => Assert.Equal(0f, T(3.5f, 0f, 0f));

    [Fact]
    public void RisesWithDrainageArea() => Assert.True(T(2.5f, 0.1f, 0f) > T(2f, 0.1f, 0f));

    [Fact]
    public void FallsWithHeightAboveDrainage()
    {
        Assert.True(T(2f, 0.1f, 0f) > T(2f, 0.1f, 2f));
        Assert.Equal(T(3f, 0.1f, 0f), T(3f, 0.1f, 2f), 4);  // a cell carrying the full flow is itself on a line
    }

    [Fact]
    public void StaysInUnitRange()
    {
        foreach (float a in new[] { -1f, 0f, 2f, 5f })
            foreach (float g in new[] { -0.1f, 0f, 0.03f, 2f })
                foreach (float h in new[] { -1f, 0f, 5f })
                    Assert.InRange(T(a, g, h), 0f, 1f);
    }

    [Fact]
    public void StagnationIsWetnessOnLevelGroundAndNoneOnAGrade()
    {
        Assert.Equal(0.9f, Throughflow.Stagnation(0.9f, 0f, 0.03f), 4);
        Assert.Equal(0f, Throughflow.Stagnation(0.9f, 0.1f, 0.03f), 4);
        Assert.InRange(Throughflow.Stagnation(0.9f, 0.015f, 0.03f), 0.44f, 0.46f);
    }

    [Fact]
    public void ProfilePeaksOnTheAxisAndFadesOnTheFlanks()
    {
        Assert.Equal(1f, Throughflow.Profile(3f, 2.5f, 0.3f, 1f), 4);     // axis: well above its surroundings
        Assert.Equal(0f, Throughflow.Profile(2.5f, 2.5f, 0.3f, 1f), 4);   // flank: level with them
        Assert.Equal(1f, Throughflow.Profile(2.5f, 2.5f, 0.3f, 0f), 4);   // thinning 0 ignores the profile
        Assert.Equal(0.15f, Throughflow.Profile(2.5f, 2.5f, 0.3f, 0.85f), 4);
    }
}
