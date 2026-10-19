using System.Collections.Generic;
using System.Text;
using LivingLandscapes.Environment;
using UnityEngine;
using Verse;

namespace LivingLandscapes.DevTools;

/// <summary>
/// Test summary lines for the gameplay siting (Docs/GAMEPLAY-PLAN.md): where the colony starts, how good that ground scores,
/// how many ways in lead to it, and where each ore lies relative to it. For A/B runs with <c>-ll-siting=off</c>.
/// </summary>
public static class GameplayReport
{
    private const float ApproachRadius = 30f;

    public static void Append(Map map, StringBuilder summary)
    {
        var environment = map.GetComponent<MapComponent_Environment>();
        if (environment == null || !environment.StartSpot.IsValid)
            return;
        IntVec3 start = environment.StartSpot;
        float[] smooth = environment.SettlementSmoothed;
        string score = smooth != null ? $", suitability {smooth[map.cellIndices.CellToIndex(start)]:0.00}" : "";
        summary.AppendLine($"gameplay: start spot {start} ({Vector3.Distance(start.ToVector3(), map.Center.ToVector3()):0} cells from the centre){score}; " +
                           $"{Approaches(map, start)} ways in at {ApproachRadius:0} cells");

        var lumps = new Dictionary<ThingDef, List<float>>();
        foreach (Thing thing in map.listerThings.AllThings)
        {
            if (thing.def.building?.isResourceRock != true)
                continue;
            if (!lumps.TryGetValue(thing.def, out List<float> distances))
                lumps[thing.def] = distances = new List<float>();
            distances.Add((thing.Position - start).LengthHorizontal);
        }
        foreach (KeyValuePair<ThingDef, List<float>> pair in lumps)
        {
            pair.Value.Sort();
            summary.AppendLine($"gameplay: ore {pair.Key.defName}: {pair.Value.Count} cells, nearest {pair.Value[0]:0}, median {pair.Value[pair.Value.Count / 2]:0} cells from the start");
        }
    }

    /// <summary>Separate stretches of walkable ground where a ring round the start crosses the map: the ways in for a raid.</summary>
    private static int Approaches(Map map, IntVec3 start)
    {
        const int samples = 360;
        var walkable = new bool[samples];
        int open = 0;
        for (int s = 0; s < samples; s++)
        {
            float angle = s * Mathf.PI * 2f / samples;
            var cell = new IntVec3(Mathf.RoundToInt(start.x + ApproachRadius * Mathf.Cos(angle)), 0, Mathf.RoundToInt(start.z + ApproachRadius * Mathf.Sin(angle)));
            walkable[s] = cell.InBounds(map) && cell.Walkable(map);
            if (walkable[s])
                open++;
        }
        if (open == samples)
            return 0;
        int arcs = 0;
        for (int s = 0; s < samples; s++)
        {
            if (walkable[s] && !walkable[(s + samples - 1) % samples])
                arcs++;
        }
        return arcs;
    }
}
