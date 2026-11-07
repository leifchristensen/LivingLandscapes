using System;

namespace LivingLandscapes.Geology;

/// <summary>
/// The one definition of "water" for distance-to-water (#100) and for clearing roofs and buildings off a carved river
/// (#155). A cell is water where the surface water model puts at least <see cref="MinDepth"/> metres of it; the ground
/// rules turn exactly those cells into water cover, and the terrain placed from that cover is water terrain. The same
/// predicate is read at three stages, each from the data that exists then:
/// <list type="bullet">
/// <item>before the cover is planned, from the LL_SurfaceWater depth (<see cref="DistanceByDepth"/>);</item>
/// <item>once the ground is planned, from the planned cover (<see cref="DistanceByCover"/>);</item>
/// <item>once terrain is placed, from the terrain (<c>GroundCovers.IsWaterTerrain</c>, behind the LL_WaterDistance layer).</item>
/// </list>
/// Game-free so a unit test can hold every consumer to it.
/// </summary>
public static class WaterDefinition
{
    /// <summary>Metres of surface water at which a cell counts as water (the lowest water rule in LL_GroundRules).</summary>
    public const float MinDepth = 0.01f;

    /// <summary>Distance given to every cell on a map without water.</summary>
    public const float NoWater = 999f;

    public static bool IsDepth(float surfaceWaterDepth) => surfaceWaterDepth >= MinDepth;

    public static bool IsCover(GroundCover cover) =>
        cover == GroundCover.ShallowWater || cover == GroundCover.DeepWater || cover == GroundCover.ShallowMovingWater || cover == GroundCover.DeepMovingWater;

    /// <summary>Cells to the nearest water, from the surface water depth (the stage before the cover exists).</summary>
    public static float[] DistanceByDepth(float[] depth, int sizeX, int sizeZ) =>
        GridMath.DistanceTo(i => IsDepth(depth[i]), sizeX, sizeZ, NoWater);

    /// <summary>Cells to the nearest water, from the planned ground cover.</summary>
    public static float[] DistanceByCover(GroundCover[] cover, int sizeX, int sizeZ) =>
        GridMath.DistanceTo(i => IsCover(cover[i]), sizeX, sizeZ, NoWater);

    /// <summary>Cells to the nearest water, from a per-cell test of the placed terrain.</summary>
    public static float[] DistanceByTerrain(Func<int, bool> isWaterTerrain, int sizeX, int sizeZ) =>
        GridMath.DistanceTo(isWaterTerrain, sizeX, sizeZ, NoWater);
}
