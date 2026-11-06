using System;

namespace LivingLandscapes.Geology;

/// <summary>
/// Which way a map's region slopes, from its world neighbours (game-free, unit-tested).
/// A world tile is tens of kilometres across and its elevation is a coarse mean, so the fall to a neighbour of the same
/// ruggedness says little about the ground a map shows: across a plain the ground is shaped by its own valleys and rises,
/// not by the tilt between two tile means. Where the ruggedness changes (plain to hills, hills to mountains) the fall is a
/// landform, the foot slope of the rougher ground, and the map inherits it. Each neighbour's fall therefore counts by the
/// relief contrast between the two tiles, |r1 - r2| / max(r1, r2) with r the relief each tile's hilliness gives
/// (GeologyGenerationExtension.ReliefFor): 0 between tiles of the same hilliness, 0.8 from flat (4 m) to small hills (20 m).
/// The sea and a world river keep their own pull (WorldContext.For).
/// </summary>
public static class RegionalSlopeMath
{
    /// <summary>
    /// How much one neighbour's fall counts towards the regional slope: the relief contrast between this tile and it, at
    /// least <paramref name="uniformShare"/> (the share kept between tiles of the same ruggedness; 0 drops it).
    /// </summary>
    public static float NeighbourWeight(float reliefHere, float reliefThere, float uniformShare)
    {
        float top = Math.Max(reliefHere, reliefThere);
        float contrast = top > 0f ? Math.Abs(reliefHere - reliefThere) / top : 0f;
        return Math.Max(Math.Max(0f, uniformShare), Math.Min(1f, contrast));
    }
}
