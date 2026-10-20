using System.Collections.Generic;
using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace LivingLandscapes.Gameplay;

/// <summary>
/// Where a colony starts (Docs/GAMEPLAY-PLAN.md, G1). Vanilla picks a random cell in the middle of the map that merely
/// has room; here the geology scores every cell as settlement ground (<see cref="Layers.SettlementWorker"/>), an anchor is
/// drawn where the score is high, and the start spot is the best valid cell near it. The anchor exists before rocks, roads and
/// resources are placed, so they can use it too.
/// </summary>
public static class SettlementSiting
{
    /// <summary>Scores the ground and draws the anchor. Called once the ground is classified (after planned cover and fertility).</summary>
    public static void Plan(Map map, MapComponent_Environment environment)
    {
        if (!GameplaySiting.Enabled)
            return;
        environment.ComputeLayers(GenerationStage.Siting);
        if (!environment.TryGetGrid(EnvironmentLayerDefOf.LL_Settlement, out EnvironmentGrid grid))
            return;
        var p = EnvironmentLayerDefOf.LL_Settlement.GetModExtension<SettlementExtension>() ?? new SettlementExtension();
        int sizeX = map.Size.x, sizeZ = map.Size.z;
        float[] suitability = grid.ToArray();
        float[] smooth = GridMath.Blur(suitability, sizeX, sizeZ, p.anchorSigma);
        environment.SettlementSmoothed = smooth;

        // Draw the anchor among the central cells, favouring high smoothed suitability.
        int half = Mathf.RoundToInt(p.centralFraction * Mathf.Min(sizeX, sizeZ) / 2f);
        var cumulative = new List<(int cell, float total)>();
        float sum = 0f, best = 0f;
        for (int z = map.Center.z - half; z <= map.Center.z + half; z++)
        {
            for (int x = map.Center.x - half; x <= map.Center.x + half; x++)
            {
                if (x < 0 || z < 0 || x >= sizeX || z >= sizeZ)
                    continue;
                int i = z * sizeX + x;
                sum += Mathf.Pow(smooth[i], p.anchorSharpness);
                best = Mathf.Max(best, smooth[i]);
                cumulative.Add((i, sum));
            }
        }
        if (sum <= 0f)
            return;
        float draw = Rand.Value;
        float pick = draw * sum;
        int chosen = cumulative.Find(entry => entry.total >= pick).cell;
        environment.SettlementAnchor = map.cellIndices.IndexToCell(chosen);
        Log.Message($"[LivingLandscapes] Settlement anchor on {map}: {environment.SettlementAnchor} (suitability {smooth[chosen]:0.00}, best of the central part {best:0.00}; draw {draw:0.000}).");
    }

    /// <summary>
    /// After vanilla chose a start spot: moves it to the best valid cell near the anchor, reachable on foot from it. Valid means
    /// what vanilla requires (standable, unroofed, heavy-building ground, outside ruin rectangles). Leaves vanilla's spot when nothing qualifies.
    /// </summary>
    public static void MoveStartSpot(Map map)
    {
        var environment = map.GetComponent<MapComponent_Environment>();
        if (environment == null || !MapGenerator.PlayerStartSpotValid)
            return;
        environment.StartSpot = MapGenerator.PlayerStartSpot;
        if (!environment.SettlementAnchor.IsValid || environment.SettlementSmoothed == null)
            return;
        var p = EnvironmentLayerDefOf.LL_Settlement.GetModExtension<SettlementExtension>() ?? new SettlementExtension();
        IntVec3 vanilla = MapGenerator.PlayerStartSpot;
        float[] smooth = environment.SettlementSmoothed;
        // Cliffs are edifices, so vanilla's open-area test cuts the map at every cliff line; reachability on foot is the real test.
        TraverseParms walk = TraverseParms.For(TraverseMode.NoPassClosedDoors);
        var usedRects = MapGenerator.GetOrGenerateVar<List<CellRect>>("UsedRects");

        IntVec3 best = IntVec3.Invalid;
        float bestScore = -1f;
        int radius = Mathf.CeilToInt(p.searchRadius);
        int notOpen = 0, notStandable = 0, notHeavy = 0, used = 0;
        foreach (IntVec3 c in GenRadial.RadialCellsAround(environment.SettlementAnchor, radius, true))
        {
            if (!c.InBounds(map))
                continue;
            if (!c.Standable(map) || c.Roofed(map)) { notStandable++; continue; }
            if (!c.GetAffordances(map).Contains(TerrainAffordanceDefOf.Heavy)) { notHeavy++; continue; }
            if (usedRects.Exists(r => r.Contains(c))) { used++; continue; }
            if (!map.reachability.CanReach(vanilla, c, PathEndMode.OnCell, walk)) { notOpen++; continue; }
            float score = smooth[map.cellIndices.CellToIndex(c)];
            if (score > bestScore)
            {
                bestScore = score;
                best = c;
            }
        }
        float vanillaScore = smooth[map.cellIndices.CellToIndex(vanilla)];
        if (!best.IsValid)
        {
            // The anchor lies in ground cut off from vanilla's open area (mountains, cliffs): take the best valid cell the colony can walk to.
            var central = new List<IntVec3>();
            foreach (IntVec3 c in CellRect.CenteredOn(map.Center, Mathf.RoundToInt(p.centralFraction * map.Size.x / 2f)).ClipInsideMap(map))
                central.Add(c);
            central.Sort((a, b) => smooth[map.cellIndices.CellToIndex(b)].CompareTo(smooth[map.cellIndices.CellToIndex(a)]));
            foreach (IntVec3 c in central)
            {
                float score = smooth[map.cellIndices.CellToIndex(c)];
                if (score <= vanillaScore)
                    break;
                if (c.Standable(map) && !c.Roofed(map) && c.GetAffordances(map).Contains(TerrainAffordanceDefOf.Heavy)
                    && !usedRects.Exists(r => r.Contains(c)) && map.reachability.CanReach(vanilla, c, PathEndMode.OnCell, walk))
                {
                    best = c;
                    bestScore = score;
                    break;
                }
            }
        }
        if (!best.IsValid)
        {
            Log.Message($"[LivingLandscapes] Start spot on {map}: no valid cell near the anchor, keeping vanilla's {vanilla} (suitability {vanillaScore:0.00}); rejected {notOpen} unreachable from vanilla's spot, {notStandable} not standable or roofed, {notHeavy} without heavy ground, {used} in ruins.");
            return;
        }
        MapGenerator.PlayerStartSpot = best;
        environment.StartSpot = best;
        Log.Message($"[LivingLandscapes] Start spot on {map}: {best} (suitability {bestScore:0.00}); vanilla's pick was {vanilla} ({vanillaScore:0.00}).");
    }
}
