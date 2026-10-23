using System.Collections.Generic;
using System.Text;
using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.DevTools;

/// <summary>
/// Checks the water invariants of Docs/GEOLOGY-RULES.md section 9 on a generated map and reports them for the export
/// summary, on the natural landform that water flows over: rivers never climb (1), channels
/// end at a base level (5), lakes lie in closed depressions (2).
/// </summary>
public static class Invariants
{
    /// <summary>Height differences below this (metres) count as level.</summary>
    private const float Tolerance = 0.05f;

    /// <summary>Hollows at least this deep (metres) and this large (cells) count as closed depressions.</summary>
    private const float DepressionDepth = 0.3f;
    private const int DepressionCells = 20;

    public static void Report(Map map, StringBuilder summary)
    {
        var environment = map.GetComponent<MapComponent_Environment>();
        if (environment == null || !environment.TryGetGrid(EnvironmentLayerDefOf.LL_Landform, out EnvironmentGrid landform))
            return;
        int sizeX = map.Size.x, sizeZ = map.Size.z;
        // Water flows downhill at its surface: the bed has pools and riffles, the surface never climbs.
        float[] surface = landform.ToArray();
        if (environment.TryGetGrid(EnvironmentLayerDefOf.LL_SurfaceWater, out EnvironmentGrid water))
        {
            float[] depth = water.ToArray();
            for (int i = 0; i < surface.Length; i++)
                surface[i] += depth[i];
        }
        RiverDownhill(map, surface, summary);
        MovingWaterFlows(map, summary);
        Lakes(map, landform.ToArray(), sizeX, sizeZ, summary);
    }

    /// <summary>
    /// Invariants 1 and 5 over the planned channel network: walking each channel's centre line downstream inside
    /// the map, the water surface never climbs above the lowest point already passed (1); every channel ends at a base level: the channel it joins, the map
    /// edge, or standing water (5).
    /// </summary>
    private static void RiverDownhill(Map map, float[] h, StringBuilder summary)
    {
        RiverSystem rivers = map.GetComponent<MapComponent_Environment>()?.PlannedRivers;
        if (rivers == null || rivers.Channels.Count == 0)
            return;
        int steps = 0, climbing = 0, ended = 0;
        bool[] coast = Geology.Coastline.SeaCells(map);
        var stranded = new StringBuilder();
        float maxRise = 0f, length = 0f, straight = 0f;
        IntVec3 worst = IntVec3.Invalid, worstLow = IntVec3.Invalid;
        int worstChannel = -1;
        for (int k = 0; k < rivers.Channels.Count; k++)
        {
            RiverPlan plan = rivers.Channels[k];
            float lowest = float.MaxValue;
            int lowestIndex = -1;
            Vector2? first = null, last = null;
            for (int i = 0; i < plan.Main.Count - 1; i++)
            {
                Vector2 a = plan.Main[i], b = plan.Main[i + 1];
                float segment = (b - a).magnitude;
                for (float t = 0f; t < segment; t += 1f)
                {
                    Vector2 v = Vector2.Lerp(a, b, t / Mathf.Max(segment, 1e-3f));
                    if (!Inside(map, v))
                        continue;
                    int index = RiverGrid.Index(v, map.Size.x, map.Size.z);
                    if (rivers.ChannelOf[index] != k)
                        continue;
                    steps++;
                    if (h[index] > lowest + Tolerance)
                    {
                        climbing++;
                        if (h[index] - lowest > maxRise)
                        {
                            maxRise = h[index] - lowest;
                            worst = map.cellIndices.IndexToCell(index);
                            worstLow = map.cellIndices.IndexToCell(lowestIndex);
                            worstChannel = k;
                        }
                    }
                    if (h[index] < lowest)
                    {
                        lowest = h[index];
                        lowestIndex = index;
                    }
                }
                if (Inside(map, a) && Inside(map, b))
                {
                    first ??= a;
                    last = b;
                    length += segment;
                }
            }
            if (first.HasValue)
                straight += (last.Value - first.Value).magnitude;


            Vector2 mouth = plan.Main[plan.Main.Count - 1];
            int mouthIndex = RiverGrid.Index(mouth, map.Size.x, map.Size.z);
            // The coast counts with its beach: a stream ending on the sand runs on across it into the sea.
            bool atBase = plan.Parent >= 0 || !Inside(map, mouth) || NearEdge(map, mouth, plan.NominalWidth)
                          || map.terrainGrid.TerrainAt(mouthIndex).IsWater || (coast != null && coast[mouthIndex]);
            if (atBase)
                ended++;
            else
                stranded.Append($" ({(int)mouth.x}, {(int)mouth.y}) on {map.terrainGrid.TerrainAt(mouthIndex).defName}, width {plan.NominalWidth:F1};");
        }
        summary.AppendLine($"invariant 1 (water flows downhill): {(climbing == 0 ? "ok" : "FAIL")} | {steps} centre-line cells in {rivers.Channels.Count} channels, " +
                           $"{climbing} above an upstream low point, max rise {maxRise:F2} m{(worst.IsValid ? $" at {worst} (channel {worstChannel}, width {rivers.Channels[worstChannel].NominalWidth:F0}, low point {worstLow}; surface {h[map.cellIndices.CellToIndex(worstLow)]:F2} then {h[map.cellIndices.CellToIndex(worst)]:F2}, water level {rivers.WaterLevel[map.cellIndices.CellToIndex(worstLow)]:F2} then {rivers.WaterLevel[map.cellIndices.CellToIndex(worst)]:F2})" : "")}");
        summary.AppendLine($"channels: {rivers.Channels.Count}, sinuosity {(straight > 1f ? length / straight : 1f):F2}, widths " +
                           string.Join(" ", rivers.Channels.ConvertAll(c => c.NominalWidth.ToString("F0"))));
        summary.AppendLine($"invariant 5 (channels end at a base level): {(ended == rivers.Channels.Count ? "ok" : "FAIL")} | {ended} of {rivers.Channels.Count} channels " +
                           "end in another channel, at the map edge or in standing water" + (stranded.Length > 0 ? $"; ending on land:{stranded}" : ""));
    }

    /// <summary>
    /// Invariant 6: every cell of moving water has a flow direction in vanilla's flow map (so it animates, turns
    /// watermills and carries items). Also reports the river graph's size: vanilla's flow debug view draws nothing on
    /// a map whose river graph is empty, whatever the flow map holds.
    /// </summary>
    private static void MovingWaterFlows(Map map, StringBuilder summary)
    {
        List<float> flow = map.waterInfo.riverFlowMap;
        int moving = 0, flowing = 0;
        foreach (IntVec3 cell in map.AllCells)
        {
            if (!cell.GetTerrain(map).IsRiver)
                continue;
            moving++;
            int f = (cell.x * map.Size.z + cell.z) * 2;
            if (flow != null && f + 1 < flow.Count && !float.IsNaN(flow[f]) && (flow[f] != 0f || flow[f + 1] != 0f))
                flowing++;
        }
        summary.AppendLine($"invariant 6 (moving water flows): {(flowing == moving ? "ok" : "FAIL")} | {flowing} of {moving} moving water cells have a flow direction; " +
                           $"river graph {map.waterInfo.riverGraph?.Count ?? 0} segments");
    }

    private static bool NearEdge(Map map, Vector2 v, float distance) =>
        v.x < distance || v.y < distance || v.x > map.Size.x - distance || v.y > map.Size.z - distance;

    private static bool Inside(Map map, Vector2 v) => v.x >= 0f && v.y >= 0f && v.x < map.Size.x && v.y < map.Size.z;

    /// <summary>
    /// Invariant 2: every body of still water lies in a closed depression (the depression-filled surface stands above
    /// it). Also counts the closed depressions that hold no water, the candidates for lakes (W6).
    /// </summary>
    private static void Lakes(Map map, float[] h, int sizeX, int sizeZ, StringBuilder summary)
    {
        var filled = (float[])h.Clone();
        GridMath.FillDepressions(filled, sizeX, sizeZ, _ => false, 0f);
        TerrainDef[] terrain = map.terrainGrid.topGrid;
        bool[] coast = Geology.Coastline.SeaCells(map);
        // Open still water only: marsh is wet ground (floodplains, seeps), not a lake.
        bool StillWater(int i) => terrain[i] != null && terrain[i].IsWater && !terrain[i].IsRiver && !terrain[i].IsOcean
                                  && terrain[i] != TerrainDefOf.Marsh;

        int lakeCount = GridMath.LabelRegions(StillWater, sizeX, sizeZ, out int[] lakeLabels);
        var lakeDepth = new float[lakeCount];
        var lagoon = new bool[lakeCount];
        var lakeCells = new int[lakeCount];
        var lakeTerrain = new TerrainDef[lakeCount];
        var lakeAt = new IntVec3[lakeCount];
        for (int i = 0; i < h.Length; i++)
        {
            int l = lakeLabels[i];
            if (l < 0)
                continue;
            lakeCells[l]++;
            if (lakeTerrain[l] == null)
                lakeAt[l] = map.cellIndices.IndexToCell(i);
            lakeTerrain[l] ??= terrain[i];
            lakeDepth[l] = Mathf.Max(lakeDepth[l], filled[i] - h[i]);
            // Still water that touches the ocean lies at sea level, open to it: a lagoon, part of the sea. The coast's own
            // water counts too, ocean or not: on a Lakeshore tile the coast is a lake, painted as lake water, and its pieces
            // along the map edge belong to the lake beyond it.
            lagoon[l] |= coast != null && coast[i];
            if (!lagoon[l])
                GridMath.ForEachNeighbour(i, sizeX, sizeZ, nb => lagoon[l] |= terrain[nb] != null && terrain[nb].IsOcean);
        }
        int lakes = 0, inDepression = 0, lagoons = 0;
        var failing = new StringBuilder();
        for (int l = 0; l < lakeCount; l++)
        {
            if (lakeCells[l] < DepressionCells)
                continue;
            lakes++;
            if (lakeDepth[l] > Tolerance)
                inDepression++;
            else if (lagoon[l])
            {
                inDepression++;
                lagoons++;
            }
            else
                failing.Append($" {lakeTerrain[l].defName} x{lakeCells[l]} at {lakeAt[l].x},{lakeAt[l].z}");
        }

        int hollowCount = GridMath.LabelRegions(i => filled[i] - h[i] > Tolerance, sizeX, sizeZ, out int[] hollowLabels);
        var hollowDepth = new float[hollowCount];
        var hollowCells = new int[hollowCount];
        var hollowWet = new bool[hollowCount];
        for (int i = 0; i < h.Length; i++)
        {
            int l = hollowLabels[i];
            if (l < 0)
                continue;
            hollowCells[l]++;
            hollowDepth[l] = Mathf.Max(hollowDepth[l], filled[i] - h[i]);
            hollowWet[l] |= terrain[i] != null && terrain[i].IsWater;
        }
        int hollows = 0, dryHollows = 0, hollowArea = 0;
        for (int l = 0; l < hollowCount; l++)
        {
            if (hollowCells[l] < DepressionCells || hollowDepth[l] < DepressionDepth)
                continue;
            hollows++;
            hollowArea += hollowCells[l];
            if (!hollowWet[l])
                dryHollows++;
        }
        summary.AppendLine($"invariant 2 (lakes in depressions): {(inDepression == lakes ? "ok" : "FAIL")} | " +
                           $"{inDepression} of {lakes} still-water bodies (>= {DepressionCells} cells) lie in a closed depression or open to the sea ({lagoons} lagoons); " +
                           $"{hollows} closed depressions >= {DepressionDepth} m deep ({hollowArea} cells), {dryHollows} hold no water" +
                           (failing.Length > 0 ? $"; not in a depression:{failing}" : ""));
    }
}
