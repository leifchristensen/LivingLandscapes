using System.Linq;
using LivingLandscapes.Geology;
using Xunit;

namespace LivingLandscapes.Tests;

/// <summary>Rank mapping with a larger upper class (#187): rich soil covers topScale times its share, order is kept.</summary>
public class RankMathTests
{
    private static float[] Scores(int n) => Enumerable.Range(0, n).Select(i => (float)((i * 37) % n)).ToArray();
    private static float[] Reference(int n) => Enumerable.Range(0, n).Select(i => i / (float)n).ToArray();

    [Fact]
    public void ScaleOneIsPlainRankMapping()
    {
        float[] score = Scores(100), reference = Reference(100);
        Assert.Equal(RankMath.Map(score, reference), RankMath.Map(score, reference, 1f, 0.8f));
    }

    [Fact]
    public void UpperClassCoversScaleTimesItsShare()
    {
        float[] score = Scores(1000), reference = Reference(1000);
        int before = RankMath.Map(score, reference, 1f, 0.8f).Count(v => v >= 0.8f);
        int after = RankMath.Map(score, reference, 1.3f, 0.8f).Count(v => v >= 0.8f);
        Assert.Equal(200, before);
        Assert.Equal(260, after);
    }

    [Fact]
    public void KeepsOrderOfScores()
    {
        float[] score = Scores(500), reference = Reference(500);
        float[] mapped = RankMath.Map(score, reference, 1.3f, 0.8f);
        int[] byScore = Enumerable.Range(0, 500).OrderBy(i => score[i]).ToArray();
        for (int k = 1; k < byScore.Length; k++)
            Assert.True(mapped[byScore[k]] >= mapped[byScore[k - 1]]);
    }

    [Fact]
    public void CappedAtAllCells()
    {
        float[] mapped = RankMath.Map(Scores(100), Reference(100), 10f, 0.8f);
        Assert.Equal(100, mapped.Count(v => v >= 0.8f));
    }
}
