using System.Collections.Generic;
using System.Linq;
using LivingLandscapes.Flora;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.DevTools;

/// <summary>
/// Gameplay measures of a map's vegetation (Docs/FLORA-GAMEPLAY.md step 1), logged by the plant census as one line per
/// group, "[LivingLandscapes] Flora metrics: ...", with key=value pairs for the test scripts:
///   cover    land cells holding a canopy, shrub or ground plant, and bare land;
///   clumps   connected groups of one layer's plants (plants within 2 cells link): median, 90th percentile, largest;
///   edges    shrubs and ground plants per land cell by distance from the edge of deep shade (shade above 0.6);
///   travel   the time to walk between random pairs of cells, with plants against the same ground without them
///            (vanilla's rule: move ticks plus the highest path cost in the cell, capped at 450), and path length over
///            the straight line;
///   fuel     the largest 8-connected patch of ground-layer plants (grass and herbs: fine fuel) and of all plants, as
///            a share of land.
/// </summary>
public static class FloraMetrics
{
    /// <summary>A walker's ticks per cell at vanilla's base move speed 4.6 cells per second.</summary>
    private const float TicksCardinal = 60f / 4.6f;
    private const float TicksDiagonal = TicksCardinal * 1.41421356f;
    private const float MaxMoveTicks = 450f;
    private const float DeepShade = 0.6f;
    private const int TravelPairs = 40;

    public static void Report(Map map, MapComponent_Ecology ecology)
    {
        int area = map.Area;
        var land = new bool[area];
        var layer = new int[area];
        var shrubDef = new ThingDef[area];
        int landCells = 0;
        for (int i = 0; i < area; i++)
        {
            layer[i] = -1;
            land[i] = !map.terrainGrid.TerrainAt(i).IsWater && map.pathing.Normal.pathGrid.WalkableFast(i);
            if (land[i])
                landCells++;
        }
        foreach (Plant plant in map.listerThings.ThingsInGroup(ThingRequestGroup.NonStumpPlant).OfType<Plant>())
        {
            if (plant.def.IsDeadPlant)
                continue;
            int cell = map.cellIndices.CellToIndex(plant.Position);
            int stratum = (int)PlantHabitat.For(plant.def).stratum;
            // Several plants in one cell: the tallest layer (canopy 0) stands for it.
            if (layer[cell] < 0 || stratum < layer[cell])
                layer[cell] = stratum;
            if (stratum == (int)PlantStratum.Shrub)
                shrubDef[cell] = plant.def;
        }
        string prefix = $"[LivingLandscapes] Flora metrics: biome={map.Biome.defName}";

        // Cover.
        var cover = new int[3];
        int bare = 0;
        for (int i = 0; i < area; i++)
        {
            if (!land[i])
                continue;
            if (layer[i] < 0)
                bare++;
            else
                cover[layer[i]]++;
        }
        float n = Mathf.Max(landCells, 1);
        Log.Message($"{prefix} cover canopy={cover[0] / n:F3} shrub={cover[1] / n:F3} ground={cover[2] / n:F3} bare={bare / n:F3}");

        // Clumps per layer.
        foreach (PlantStratum s in new[] { PlantStratum.Canopy, PlantStratum.Shrub, PlantStratum.Ground })
        {
            List<int> sizes = Components(map, i => layer[i] == (int)s, 2);
            if (sizes.Count == 0)
            {
                Log.Message($"{prefix} clumps layer={s} count=0");
                continue;
            }
            sizes.Sort();
            Log.Message($"{prefix} clumps layer={s} count={sizes.Count} median={sizes[sizes.Count / 2]} p90={sizes[(int)(sizes.Count * 0.9f)]} largest={sizes[sizes.Count - 1]}");
        }

        // Edges: signed distance in cells from the edge of deep shade (negative inside the forest).
        if (ecology != null && ecology.Ready)
        {
            var forest = new bool[area];
            int forestCells = 0;
            for (int i = 0; i < area; i++)
            {
                forest[i] = ecology.ShadeAt(i) > DeepShade;
                if (forest[i])
                    forestCells++;
            }
            if (forestCells > 0 && forestCells < area)
            {
                int[] inside = StepsTo(map, i => !forest[i]), outside = StepsTo(map, i => forest[i]);
                string[] bins = { "forest>4", "forest1-4", "open1-4", "open>4" };
                var cells = new int[4];
                var shrubs = new int[4];
                var ground = new int[4];
                for (int i = 0; i < area; i++)
                {
                    if (!land[i])
                        continue;
                    int bin = forest[i] ? (inside[i] > 4 ? 0 : 1) : (outside[i] > 4 ? 3 : 2);
                    cells[bin]++;
                    if (layer[i] == (int)PlantStratum.Shrub)
                        shrubs[bin]++;
                    else if (layer[i] == (int)PlantStratum.Ground)
                        ground[bin]++;
                }
                string text = string.Join(" ", Enumerable.Range(0, 4).Select(b =>
                    $"{bins[b]}:cells={cells[b]},shrub={shrubs[b] / (float)Mathf.Max(cells[b], 1):F3},ground={ground[b] / (float)Mathf.Max(cells[b], 1):F3}"));
                Log.Message($"{prefix} edges {text}");

                // Diagnostic: the light reaching the ground in each band (quartiles), and the fitness the shrubs standing
                // there have for their own species now.
                var light = new List<float>[4];
                var shrubFitness = new List<float>[4];
                for (int b = 0; b < 4; b++)
                {
                    light[b] = new List<float>();
                    shrubFitness[b] = new List<float>();
                }
                for (int i = 0; i < area; i++)
                {
                    if (!land[i])
                        continue;
                    int bin = forest[i] ? (inside[i] > 4 ? 0 : 1) : (outside[i] > 4 ? 3 : 2);
                    light[bin].Add(ecology.LightAt(i));
                    if (shrubDef[i] != null)
                        shrubFitness[bin].Add(ecology.Fitness(shrubDef[i], i));
                }
                string Quartiles(List<float> values)
                {
                    if (values.Count == 0)
                        return "-";
                    values.Sort();
                    return $"{values[values.Count / 4]:F2}/{values[values.Count / 2]:F2}/{values[values.Count * 3 / 4]:F2}";
                }
                Log.Message($"{prefix} light " + string.Join(" ", Enumerable.Range(0, 4).Select(b =>
                    $"{bins[b]}:light={Quartiles(light[b])},shrubFitness={Quartiles(shrubFitness[b])},shrubs={shrubFitness[b].Count}")));
            }
            else
            {
                Log.Message($"{prefix} edges none (deep shade on {forestCells} cells)");
            }
        }

        // Travel.
        float[] withPlants = MoveCost(map, includePlants: true), withoutPlants = MoveCost(map, includePlants: false);
        double plantCost = 0;
        int thicketCells = 0, gridBehind = 0;
        for (int i = 0; i < area; i++)
        {
            if (!land[i])
                continue;
            plantCost += withPlants[i] - withoutPlants[i];
            if (withPlants[i] >= 100f)
                thicketCells++;
            // The pathfinder reads the path grid: it must hold the thicket cost too.
            if (map.pathing.Normal.pathGrid.Cost(map.cellIndices.IndexToCell(i)) < withPlants[i])
                gridBehind++;
        }
        Rand.PushState(map.Tile.tileId ^ 0x5eed);
        double timeRatio = 0, lengthRatio = 0;
        int pairs = 0;
        try
        {
            for (int attempt = 0; attempt < TravelPairs * 20 && pairs < TravelPairs; attempt++)
            {
                int a = Rand.Range(0, area), b = Rand.Range(0, area);
                if (!land[a] || !land[b])
                    continue;
                IntVec3 ca = map.cellIndices.IndexToCell(a), cb = map.cellIndices.IndexToCell(b);
                float straight = (ca - cb).LengthHorizontal;
                if (straight < 60f || straight > 120f)
                    continue;
                if (!Shortest(map, withPlants, a, b, out float time, out float length) || !Shortest(map, withoutPlants, a, b, out float bareTime, out _))
                    continue;
                timeRatio += time / bareTime;
                lengthRatio += length / straight;
                pairs++;
            }
        }
        finally
        {
            Rand.PopState();
        }
        Log.Message($"{prefix} travel pairs={pairs} plantCostPerLandCell={plantCost / n:F1} timeWithPlants/without={(pairs > 0 ? timeRatio / pairs : double.NaN):F3} pathLength/straight={(pairs > 0 ? lengthRatio / pairs : double.NaN):F3} thicketCells(cost>=100)={thicketCells / n:F3} pathGridBehind={gridBehind}");

        // Fuel continuity.
        List<int> fine = Components(map, i => layer[i] == (int)PlantStratum.Ground, 1);
        List<int> any = Components(map, i => layer[i] >= 0, 1);
        Log.Message($"{prefix} fuel fineLargest={(fine.Count > 0 ? fine.Max() : 0) / n:F3} anyLargest={(any.Count > 0 ? any.Max() : 0) / n:F3}");
    }

    /// <summary>Sizes of the groups of cells that pass <paramref name="member"/>, linking cells within <paramref name="reach"/> (Chebyshev).</summary>
    private static List<int> Components(Map map, System.Func<int, bool> member, int reach)
    {
        int sizeX = map.Size.x, sizeZ = map.Size.z, area = map.Area;
        var seen = new bool[area];
        var sizes = new List<int>();
        var queue = new Queue<int>();
        for (int start = 0; start < area; start++)
        {
            if (seen[start] || !member(start))
                continue;
            int size = 0;
            seen[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                size++;
                int cx = c % sizeX, cz = c / sizeX;
                for (int dz = -reach; dz <= reach; dz++)
                {
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        int x = cx + dx, z = cz + dz;
                        if (x < 0 || z < 0 || x >= sizeX || z >= sizeZ)
                            continue;
                        int o = z * sizeX + x;
                        if (!seen[o] && member(o))
                        {
                            seen[o] = true;
                            queue.Enqueue(o);
                        }
                    }
                }
            }
            sizes.Add(size);
        }
        return sizes;
    }

    /// <summary>8-neighbour steps from each cell to the nearest cell passing <paramref name="target"/> (0 on it).</summary>
    private static int[] StepsTo(Map map, System.Func<int, bool> target)
    {
        int sizeX = map.Size.x, sizeZ = map.Size.z, area = map.Area;
        var steps = new int[area];
        var queue = new Queue<int>();
        for (int i = 0; i < area; i++)
        {
            steps[i] = int.MaxValue;
            if (target(i))
            {
                steps[i] = 0;
                queue.Enqueue(i);
            }
        }
        while (queue.Count > 0)
        {
            int c = queue.Dequeue();
            int cx = c % sizeX, cz = c / sizeX;
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || z < 0 || x >= sizeX || z >= sizeZ)
                        continue;
                    int o = z * sizeX + x;
                    if (steps[o] == int.MaxValue)
                    {
                        steps[o] = steps[c] + 1;
                        queue.Enqueue(o);
                    }
                }
            }
        }
        return steps;
    }

    /// <summary>
    /// Extra ticks to enter each cell as vanilla counts them: the highest path cost of the terrain and the things in it
    /// (plants only if <paramref name="includePlants"/>); negative where the cell cannot be walked.
    /// </summary>
    private static float[] MoveCost(Map map, bool includePlants)
    {
        var cost = new float[map.Area];
        for (int i = 0; i < map.Area; i++)
        {
            if (!map.pathing.Normal.pathGrid.WalkableFast(i))
            {
                cost[i] = -1f;
                continue;
            }
            int highest = map.terrainGrid.TerrainAt(i).pathCost;
            foreach (Thing thing in map.thingGrid.ThingsListAtFast(i))
            {
                if (thing is Plant && !includePlants)
                    continue;
                if (thing.def.pathCost > highest)
                    highest = thing.def.pathCost;
            }
            // With plants, the thicket drag (P18) as the game applies it.
            if (includePlants)
                highest = Mathf.Max(highest, map.GetComponent<MapComponent_Ecology>()?.ThicketCost(map.cellIndices.IndexToCell(i)) ?? 0);
            cost[i] = highest;
        }
        return cost;
    }

    /// <summary>Dijkstra on 8 neighbours: walking time from a to b in ticks, and the path's length in cells (diagonal steps 1.41).</summary>
    private static bool Shortest(Map map, float[] cost, int a, int b, out float time, out float length)
    {
        int sizeX = map.Size.x, sizeZ = map.Size.z, area = map.Area;
        var best = new float[area];
        var from = new int[area];
        for (int i = 0; i < area; i++)
        {
            best[i] = float.MaxValue;
            from[i] = -1;
        }
        var open = new PriorityQueue<int, float>();
        best[a] = 0f;
        open.Enqueue(a, 0f);
        while (open.TryDequeue(out int c, out float t))
        {
            if (t > best[c])
                continue;
            if (c == b)
                break;
            int cx = c % sizeX, cz = c / sizeX;
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0)
                        continue;
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || z < 0 || x >= sizeX || z >= sizeZ)
                        continue;
                    int o = z * sizeX + x;
                    if (cost[o] < 0f)
                        continue;
                    float move = Mathf.Min((dx != 0 && dz != 0 ? TicksDiagonal : TicksCardinal) + cost[o], MaxMoveTicks);
                    if (t + move < best[o])
                    {
                        best[o] = t + move;
                        from[o] = c;
                        open.Enqueue(o, best[o]);
                    }
                }
            }
        }
        time = best[b];
        length = 0f;
        if (time == float.MaxValue)
            return false;
        for (int c = b; c != a; c = from[c])
            length += c % sizeX != from[c] % sizeX && c / sizeX != from[c] / sizeX ? 1.41421356f : 1f;
        return true;
    }
}
