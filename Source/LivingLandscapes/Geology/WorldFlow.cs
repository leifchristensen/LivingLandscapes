using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LivingLandscapes.Compatibility;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>
/// Vanilla's world river flow per tile: rainfall gathered downhill toward the coast, less evaporation. World generation
/// lays rivers where this flow passes the river defs' thresholds, then throws it away, so it is worked out again here
/// with vanilla's own methods (<see cref="WorldGenStep_Rivers"/>), once per world. A tile the world gives no river
/// carries less than a creek, and this says how much less (Docs/WATER-AUDIT.md).
/// </summary>
public static class WorldFlow
{
    private static World cachedWorld;
    private static int cachedLayer = -1;
    private static float[] cachedFlow;

    /// <summary>Flow through this tile in vanilla's units (the river defs' thresholds); the tile's own rainfall if it cannot be worked out.</summary>
    public static float At(PlanetTile tile)
    {
        float[] flow = FlowFor(tile.Layer);
        float own = Find.WorldGrid[tile].rainfall;
        return flow != null && tile.tileId < flow.Length && flow[tile.tileId] > 0f ? flow[tile.tileId] : own;
    }

    /// <summary>
    /// A creek's flow and width in vanilla's data: the narrowest river def, which ends where the flow falls below its
    /// degrade threshold. Null if no river def has one.
    /// </summary>
    public static RiverDef Creek()
    {
        RiverDef creek = null;
        foreach (RiverDef river in DefDatabase<RiverDef>.AllDefsListForReading)
        {
            if (river.degradeThreshold > 0 && (creek == null || river.widthOnMap < creek.widthOnMap))
                creek = river;
        }
        return creek;
    }

    // Map Preview generates previews on its own worker thread while the main thread may read the same world, so the
    // first computation of a world's flow must not run twice at once (#204).
    private static readonly object computeLock = new object();

    private static float[] FlowFor(PlanetLayer layer)
    {
        lock (computeLock)
        {
            if (cachedFlow != null && cachedWorld == Find.World && cachedLayer == layer.LayerID)
                return cachedFlow;
            cachedWorld = Find.World;
            cachedLayer = layer.LayerID;
            cachedFlow = null;
            try
            {
                // Under Realistic Planets 2 the world's rivers come from its own flow field: use that one, so a tile's channel
                // is sized by the same discharge that drew the world river (#219).
                cachedFlow = (layer == Find.WorldGrid.Surface ? RealisticPlanets2Rivers.Flow() : null) ?? Compute(layer, out _);
            }
            catch (Exception e)
            {
                Log.Warning($"[LivingLandscapes] Could not work out the world's river flow ({e.Message}); tiles use their own rainfall.");
            }
            return cachedFlow;
        }
    }

    /// <summary>
    /// Vanilla's flow per tile and the tile each tile drains to (the parent in its downhill tree; -1 for sea tiles and tiles the
    /// coast flood does not reach). Worked out again on every call; for world generation (the watershed rule, #219).
    /// </summary>
    public static float[] Drainage(PlanetLayer layer, out int[] downstream) => Compute(layer, out downstream);

    private static float[] Compute(PlanetLayer layer, out int[] downstream)
    {
        var step = new WorldGenStep_Rivers();
        var coastal = (List<PlanetTile>)AccessTools.Method(typeof(WorldGenStep_Rivers), "GetCoastalWaterTiles").Invoke(step, new object[] { layer });
        downstream = null;
        if (coastal.NullOrEmpty())
            return null;
        var implied = AccessTools.MethodDelegate<Func<Tile, float>>(AccessTools.Method(typeof(WorldGenStep_Rivers), "GetImpliedElevation"));
        var cost = (SimpleCurve)AccessTools.Field(typeof(WorldGenStep_Rivers), "ElevationChangeCost").GetValue(null);
        MethodInfo accumulate = AccessTools.Method(typeof(WorldGenStep_Rivers), "AccumulateFlow");

        // The same downhill tree vanilla floods from the coast: a step costs by the fall between the tiles, double where
        // it is not the tile's steepest way down.
        var neighbours = new List<PlanetTile>();
        List<PlanetTile>[] tree = layer.Pather.FloodPathsWithCostForTree(coastal, (from, to) =>
        {
            Find.WorldGrid.GetTileNeighbors(to, neighbours);
            PlanetTile lowest = neighbours[0];
            foreach (PlanetTile n in neighbours)
            {
                if (implied(Find.WorldGrid[n]) < implied(Find.WorldGrid[lowest]))
                    lowest = n;
            }
            float steepest = lowest != from ? 2f : 1f;
            return Mathf.RoundToInt(steepest * cost.Evaluate(implied(Find.WorldGrid[from]) - implied(Find.WorldGrid[to])));
        }, t => Find.WorldGrid[t].WaterCovered);
        var flow = new float[tree.Length];
        foreach (PlanetTile root in coastal)
            accumulate.Invoke(step, new object[] { flow, tree, root });
        downstream = new int[tree.Length];
        Array.Fill(downstream, -1);
        for (int parent = 0; parent < tree.Length; parent++)
        {
            if (tree[parent] == null)
                continue;
            foreach (PlanetTile child in tree[parent])
                downstream[child.tileId] = parent;
        }
        return flow;
    }
}
