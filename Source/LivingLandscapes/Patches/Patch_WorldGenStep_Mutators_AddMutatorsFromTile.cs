using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// River deltas from the river's own tributaries, not from the mouth tile's link count alone. Vanilla gives the RiverDelta
/// tile feature only to a coastal tile with more than two river links, which needs two rivers to meet exactly on the coast tile.
/// Realistic Planets 2 draws about three times as many mouths as vanilla, nearly all a single river with two links, so deltas
/// become rare. Here a coastal tile whose river flows into the water is also a delta when a confluence (a tile with three or more
/// links) lies within <see cref="Reach"/> river tiles upstream: a river that gathers its tributaries just before the sea is the
/// river that builds a delta. Needs no random numbers, so it changes nothing else in the world; every tile that vanilla already
/// made a delta stays one. The tile's other river feature (River, Confluence, Island) gives way to the delta, as vanilla does.
/// </summary>
[HarmonyPatch(typeof(WorldGenStep_Mutators), nameof(WorldGenStep_Mutators.AddMutatorsFromTile))]
public static class Patch_WorldGenStep_Mutators_AddMutatorsFromTile
{
    /// <summary>How many river tiles upstream of the mouth a confluence may lie and still make the mouth a delta.</summary>
    public const int Reach = 2;

    public static void Postfix(PlanetLayer layer)
    {
        try
        {
            AddDeltas(layer);
        }
        catch (Exception e)
        {
            Log.Warning($"[LivingLandscapes] Tributary deltas skipped: {e}");
        }
    }

    private static void AddDeltas(PlanetLayer layer)
    {
        TileMutatorDef delta = DefDatabase<TileMutatorDef>.GetNamedSilentFail("RiverDelta");
        if (delta == null || !ModsConfig.OdysseyActive || layer != Find.WorldGrid.Surface)
            return;
        int added = 0;
        foreach (Tile tile in layer.Tiles)
        {
            if (tile is not SurfaceTile surface || surface.WaterCovered || surface.Rivers.NullOrEmpty() || tile.Mutators.Contains(delta))
                continue;
            if (!Find.World.CoastDirectionAt(tile.tile).IsValid || !surface.Rivers.Any(l => l.neighbor.Tile.WaterCovered))
                continue;
            if (!ConfluenceUpstream(surface))
                continue;
            // Vanilla's River-category feature blocks an equal-priority one: take it off, put it back if the delta is refused.
            var removed = tile.Mutators.Where(m => m.categories.Contains(delta.categories[0])).ToList();
            foreach (TileMutatorDef m in removed)
                tile.mutatorsNullable.Remove(m);
            if (delta.IsValidTile(tile.tile, layer))
            {
                tile.AddMutator(delta);
                added++;
            }
            else
            {
                foreach (TileMutatorDef m in removed)
                    tile.AddMutator(m);
            }
        }
        Log.Message($"[LivingLandscapes] Tributary deltas: {added} coastal river mouths became deltas (a confluence within {Reach} river tiles upstream).");
    }

    /// <summary>True if a land tile with three or more river links lies within <see cref="Reach"/> river tiles of the mouth tile (the mouth itself is not counted).</summary>
    private static bool ConfluenceUpstream(SurfaceTile mouth)
    {
        var seen = new HashSet<int> { mouth.tile.tileId };
        var frontier = new List<SurfaceTile> { mouth };
        for (int depth = 1; depth <= Reach; depth++)
        {
            var next = new List<SurfaceTile>();
            foreach (SurfaceTile t in frontier)
            {
                foreach (SurfaceTile.RiverLink link in t.Rivers)
                {
                    Tile n = link.neighbor.Tile;
                    if (n.WaterCovered || n is not SurfaceTile s || !seen.Add(n.tile.tileId))
                        continue;
                    if (s.Rivers.Count > 2)
                        return true;
                    next.Add(s);
                }
            }
            frontier = next;
        }
        return false;
    }
}
