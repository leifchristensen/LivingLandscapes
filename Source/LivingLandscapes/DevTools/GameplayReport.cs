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
        int rock = 0, roofed = 0;
        foreach (IntVec3 c in map.AllCells)
        {
            if (c.GetEdifice(map)?.def.building?.isNaturalRock == true)
                rock++;
            if (c.GetRoof(map)?.isNatural == true)
                roofed++;
        }
        summary.AppendLine($"terrain: rock {rock * 100f / map.Area:0.0}% of cells, natural roof {roofed * 100f / map.Area:0.0}%, hilliness {map.TileInfo.hilliness}");
        var environment = map.GetComponent<MapComponent_Environment>();
        if (environment == null || !environment.StartSpot.IsValid)
            return;
        IntVec3 start = environment.StartSpot;
        float[] smooth = environment.SettlementSmoothed;
        string score = smooth != null ? $", suitability {smooth[map.cellIndices.CellToIndex(start)]:0.00}" : "";
        summary.AppendLine($"gameplay: start spot {start} ({Vector3.Distance(start.ToVector3(), map.Center.ToVector3()):0} cells from the centre){score}; " +
                           $"{Approaches(map, start)} ways in at {ApproachRadius:0} cells");

        var discoveries = new Dictionary<string, List<float>>();
        var lumps = new Dictionary<ThingDef, List<float>>();
        foreach (Thing thing in map.listerThings.AllThings)
        {
            if (thing.def.building?.isResourceRock != true)
            {
                if (thing.def.defName == "SteamGeyser" || thing.def.defName.StartsWith("Ancient"))
                    Discoveries(thing.def.defName, discoveries).Add((thing.Position - start).LengthHorizontal);
                continue;
            }
            if (!lumps.TryGetValue(thing.def, out List<float> distances))
                lumps[thing.def] = distances = new List<float>();
            distances.Add((thing.Position - start).LengthHorizontal);
        }
        foreach (KeyValuePair<string, List<float>> pair in discoveries)
        {
            pair.Value.Sort();
            summary.AppendLine($"gameplay: discovery {pair.Key}: {pair.Value.Count}, nearest {pair.Value[0]:0}, median {pair.Value[pair.Value.Count / 2]:0} cells from the start");
        }
        foreach (KeyValuePair<ThingDef, List<float>> pair in lumps)
        {
            pair.Value.Sort();
            summary.AppendLine($"gameplay: ore {pair.Key.defName}: {pair.Value.Count} cells, nearest {pair.Value[0]:0}, median {pair.Value[pair.Value.Count / 2]:0} cells from the start");
        }
    }

    private static List<float> Discoveries(string name, Dictionary<string, List<float>> all)
    {
        if (!all.TryGetValue(name, out List<float> list))
            all[name] = list = new List<float>();
        return list;
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
