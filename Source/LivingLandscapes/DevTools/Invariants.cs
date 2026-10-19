using System.Text;
using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.DevTools;

/// <summary>
/// Checks the water invariants of Docs/GEOLOGY-RULES.md section 9 on a generated map and reports them for the export
/// summary, on the natural landform that water flows over: rivers never climb (1), split channels rejoin (4), lakes lie
/// in closed depressions (2).
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
        if (environment == null || !environment.TryGetGrid(EnvironmentLayerDefOf.LL_Height, out EnvironmentGrid grid)
            || !environment.TryGetGrid(EnvironmentLayerDefOf.LL_Landform, out EnvironmentGrid landform))
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
        Lakes(map, landform.ToArray(), sizeX, sizeZ, summary);
    }

    /// <summary>
    /// Invariant 1: walks every river segment's centre line downstream and measures how far the water surface climbs
    /// above the lowest point already passed.
    /// </summary>
    private static void RiverDownhill(Map map, float[] h, StringBuilder summary)
    {
        var graph = map.waterInfo?.riverGraph;
        if (graph == null || graph.Count == 0)
            return;
        // Each segment is walked downstream on its own: channels branch and rejoin, so there is no single course.
        int steps = 0, climbing = 0;
        float maxRise = 0f;
        IntVec3 worst = IntVec3.Invalid;
        foreach (RiverNode node in graph)
        {
            float length = (node.end - node.start).magnitude, lowest = float.MaxValue;
            for (float t = 0f; t <= length; t += 1f)
            {
                IntVec3 cell = Vector3.Lerp(node.start, node.end, length < 1e-3f ? 0f : t / length).ToIntVec3();
                if (!cell.InBounds(map) || !cell.GetTerrain(map).IsRiver)
                    continue;
                float v = h[map.cellIndices.CellToIndex(cell)];
                steps++;
                if (v > lowest + Tolerance)
                {
                    climbing++;
                    if (v - lowest > maxRise)
                    {
                        maxRise = v - lowest;
                        worst = cell;
                    }
                }
                lowest = Mathf.Min(lowest, v);
            }
        }
        summary.AppendLine($"invariant 1 (river downhill): {(climbing == 0 ? "ok" : "FAIL")} | {steps} centre-line cells in {graph.Count} segments, " +
                           $"{climbing} above an upstream low point, max rise {maxRise:F2} m{(worst.IsValid ? $" at {worst}" : "")}");

        RiverPlan plan = map.GetComponent<MapComponent_Environment>()?.PlannedRiver;
        if (plan == null)
            return;
        float length2 = 0f;
        Vector2? first = null, last = null;
        for (int i = 1; i < plan.Main.Count; i++)
        {
            Vector2 a = plan.Main[i - 1], b = plan.Main[i];
            if (!Inside(map, a) || !Inside(map, b))
                continue;
            first ??= a;
            last = b;
            length2 += (b - a).magnitude;
        }
        float straight = first.HasValue ? (last.Value - first.Value).magnitude : 0f;
        int rejoin = 0;
        foreach (RiverPlan.SideBranch side in plan.Sides)
        {
            if ((side.Points[0] - plan.Main[side.From]).magnitude <= plan.NominalWidth
                && (side.Points[side.Points.Count - 1] - plan.Main[side.To]).magnitude <= plan.NominalWidth)
                rejoin++;
        }
        summary.AppendLine($"invariant 4 (split channels rejoin): {(rejoin == plan.Sides.Count ? "ok" : "FAIL")} | {rejoin} of {plan.Sides.Count} side channels rejoin; " +
                           $"sinuosity {(straight > 1f ? length2 / straight : 1f):F2}, {plan.Oxbows.Count} cut-off loops, width {plan.NominalWidth:F0}");
    }

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
        bool StillWater(int i) => terrain[i] != null && terrain[i].IsWater && !terrain[i].IsRiver && !terrain[i].IsOcean;

        int lakeCount = GridMath.LabelRegions(StillWater, sizeX, sizeZ, out int[] lakeLabels);
        var lakeDepth = new float[lakeCount];
        var lakeCells = new int[lakeCount];
        var lakeTerrain = new TerrainDef[lakeCount];
        for (int i = 0; i < h.Length; i++)
        {
            int l = lakeLabels[i];
            if (l < 0)
                continue;
            lakeCells[l]++;
            lakeTerrain[l] ??= terrain[i];
            lakeDepth[l] = Mathf.Max(lakeDepth[l], filled[i] - h[i]);
        }
        int lakes = 0, inDepression = 0;
        var failing = new StringBuilder();
        for (int l = 0; l < lakeCount; l++)
        {
            if (lakeCells[l] < DepressionCells)
                continue;
            lakes++;
            if (lakeDepth[l] > Tolerance)
                inDepression++;
            else
                failing.Append($" {lakeTerrain[l].defName} x{lakeCells[l]}");
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
                           $"{inDepression} of {lakes} still-water bodies (>= {DepressionCells} cells) lie in a closed depression; " +
                           $"{hollows} closed depressions >= {DepressionDepth} m deep ({hollowArea} cells), {dryHollows} hold no water" +
                           (failing.Length > 0 ? $"; not in a depression:{failing}" : ""));
    }
}
