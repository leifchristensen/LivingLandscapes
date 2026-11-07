using System;
using System.IO;
using LivingLandscapes.Geology;
using Xunit;

namespace LivingLandscapes.Tests;

/// <summary>
/// One definition of water (#100): the depth, cover and terrain stages give the same distance for the same water, and every
/// consumer reads <see cref="WaterDefinition"/> instead of keeping its own test.
/// </summary>
public class WaterDefinitionTests
{
    private const int SizeX = 8, SizeZ = 6;

    private static GroundCover CoverFor(float depth) =>
        WaterDefinition.IsDepth(depth) ? GroundCover.ShallowWater : GroundCover.None;

    [Fact]
    public void ThinFilmBelowTheWaterRuleIsNotWater()
    {
        Assert.False(WaterDefinition.IsDepth(0.004f));
        Assert.True(WaterDefinition.IsDepth(WaterDefinition.MinDepth));
    }

    [Fact]
    public void OnlyWaterCoversAreWater()
    {
        foreach (GroundCover cover in Enum.GetValues(typeof(GroundCover)))
        {
            bool expected = cover == GroundCover.ShallowWater || cover == GroundCover.DeepWater
                || cover == GroundCover.ShallowMovingWater || cover == GroundCover.DeepMovingWater;
            Assert.Equal(expected, WaterDefinition.IsCover(cover));
        }
    }

    [Fact]
    public void DepthCoverAndTerrainStagesAgreeOnDistance()
    {
        var depth = new float[SizeX * SizeZ];
        var cover = new GroundCover[depth.Length];
        depth[2 * SizeX + 3] = 0.5f;
        depth[2 * SizeX + 4] = 0.02f;
        depth[5 * SizeX + 0] = 0.004f;   // below the rule: dry at every stage
        for (int i = 0; i < depth.Length; i++)
            cover[i] = CoverFor(depth[i]);

        float[] byDepth = WaterDefinition.DistanceByDepth(depth, SizeX, SizeZ);
        float[] byCover = WaterDefinition.DistanceByCover(cover, SizeX, SizeZ);
        float[] byTerrain = WaterDefinition.DistanceByTerrain(i => cover[i] != GroundCover.None, SizeX, SizeZ);
        Assert.Equal(byDepth, byCover);
        Assert.Equal(byDepth, byTerrain);
        Assert.Equal(0f, byDepth[2 * SizeX + 3]);
        Assert.True(byDepth[5 * SizeX + 0] > 0f);
    }

    [Fact]
    public void MapWithoutWaterIsEverywhereNoWater()
    {
        float[] d = WaterDefinition.DistanceByDepth(new float[SizeX * SizeZ], SizeX, SizeZ);
        Assert.All(d, v => Assert.Equal(WaterDefinition.NoWater, v));
    }

    [Theory]
    [InlineData("Layers/WaterDistanceWorker.cs", "WaterDefinition.DistanceByTerrain")]
    [InlineData("Layers/SettlementWorker.cs", "WaterDefinition.DistanceByCover")]
    [InlineData("Geology/VanillaTerrainWriter.cs", "WaterDefinition.DistanceByDepth")]
    [InlineData("Geology/GroundRules.cs", "WaterDefinition.IsCover")]
    [InlineData("Patches/Patch_TileMutatorWorker_River_GetDepth.cs", "GroundCovers.IsWaterTerrain")]
    public void ConsumerReadsTheSharedDefinition(string file, string call)
    {
        string path = FindSource(file);
        Assert.Contains(call, File.ReadAllText(path));
    }

    [Theory]
    [InlineData("Layers/WaterDistanceWorker.cs")]
    [InlineData("Layers/SettlementWorker.cs")]
    [InlineData("Geology/VanillaTerrainWriter.cs")]
    public void ConsumerKeepsNoWaterTestOfItsOwn(string file)
    {
        string text = File.ReadAllText(FindSource(file));
        Assert.DoesNotContain("terrain.IsWater", text);
        Assert.DoesNotContain("water[i] > 0f", text);
        Assert.DoesNotContain("GridMath.DistanceTo(i => GroundCovers.IsWater", text);
    }

    private static string FindSource(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "Source", "LivingLandscapes", relative);
            if (File.Exists(candidate))
                return candidate;
        }
        throw new FileNotFoundException(relative);
    }
}
