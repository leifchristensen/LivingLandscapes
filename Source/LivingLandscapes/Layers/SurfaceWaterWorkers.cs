using System.Collections.Generic;
using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Layers;

/// <summary>Tuning for the surface water model, set on the LL_SurfaceWater layer Def in XML.</summary>
public class SurfaceWaterExtension : DefModExtension
{
    /// <summary>
    /// Yearly evaporation from open water (mm) at 0 C, and how much it rises per degree of the tile's mean
    /// temperature. A closed hollow fills to its spill level when the rain its catchment collects beats evaporation
    /// from its surface; otherwise it holds a smaller lake (W6).
    /// </summary>
    public float evaporationAtZero = 400f;
    public float evaporationPerDegree = 60f;
    public float minEvaporation = 150f;

    /// <summary>Share of the rain on the catchment that runs off into the hollow (the rest soaks in or evaporates on land).</summary>
    public float runoffShare = 0.3f;

    /// <summary>Hollows shallower (metres) or smaller (cells) than this hold no lake; they stay damp ground.</summary>
    public float minLakeDepth = 0.25f;
    public int minLakeCells = 12;

    /// <summary>
    /// Streams (W2, W3): water flows in a channel where the drainage area times the climate wetness reaches this
    /// (log10 cells). Channel width grows with the square root of that area (hydraulic geometry).
    /// </summary>
    public float streamArea = 2.8f;
    public float widthPerSqrtArea = 0.04f;
    public float maxStreamWidth = 6f;

    /// <summary>Water depth (metres) in a stream: a base plus a share of its width.</summary>
    public float streamDepth = 0.3f;
    public float streamDepthPerWidth = 0.1f;

    /// <summary>Flow speed 0..1 of the smallest and of a stream this much larger (log10 cells).</summary>
    public float minFlow = 0.4f;
    public float fullFlowAreaAbove = 1.5f;

    /// <summary>
    /// The drainage area (cells) a channel of this width drains under the stream width rule, read backwards: how much
    /// land a world river entering the map already drains, given the width vanilla gives it.
    /// </summary>
    public float AreaForWidth(float width, float climate) =>
        Mathf.Pow(width / widthPerSqrtArea, 2f) / Mathf.Max(climate, 1e-3f);
}

/// <summary>
/// The surface water the landform holds: lakes in closed hollows, filled from a water balance (W6), and streams along
/// drainage lines, sized by the area they drain (W2, W3). Computed from the natural landform before any terrain is
/// placed, so terrain, cliffs and plants all follow one water plan.
/// </summary>
public static class SurfaceWaterModel
{
    public struct Result
    {
        /// <summary>Water depth in metres; 0 on dry ground.</summary>
        public float[] Depth;

        /// <summary>Flow speed 0..1 in stream channels; 0 on dry ground and in still water.</summary>
        public float[] Flow;
    }

    public static Result Compute(Map map, float[] landform, float[] drainage, SurfaceWaterExtension p, bool log = false)
    {
        int sizeX = map.Size.x, sizeZ = map.Size.z, n = landform.Length;
        float climate = (EnvironmentLayerDefOf.LL_Wetness.GetModExtension<WetnessExtension>() ?? new WetnessExtension()).ClimateOf(map);
        var result = new Result { Depth = new float[n], Flow = new float[n] };

        // A world river's channels hold water up to the surface the carving gave them, flowing at full speed. Streams
        // and lakes join them there rather than overlaying them.
        float[] river = map.GetComponent<MapComponent_Environment>()?.PlannedRiver?.WaterLevel;
        bool InRiver(int i) => river != null && !float.IsNaN(river[i]);
        for (int i = 0; river != null && i < n; i++)
        {
            if (!InRiver(i))
                continue;
            result.Depth[i] = Mathf.Max(0f, river[i] - landform[i]);
            result.Flow[i] = 1f;
        }

        Streams(landform, drainage, sizeX, sizeZ, climate, p, result, InRiver);
        int lakes = Lakes(map, landform, drainage, sizeX, sizeZ, p, result, InRiver);

        if (!log)
            return result;
        int water = 0, flowing = 0;
        for (int i = 0; i < n; i++)
        {
            if (result.Depth[i] > 0f) water++;
            if (result.Flow[i] > 0f) flowing++;
        }
        Log.Message($"[LivingLandscapes] Surface water on {map}: {lakes} lakes, {water} water cells ({flowing} flowing), climate {climate:F2}.");
        return result;
    }

    /// <summary>
    /// Stream channels: every cell whose drainage area times the climate wetness reaches <see cref="SurfaceWaterExtension.streamArea"/>
    /// is on a channel; the channel is widened around it to its width.
    /// </summary>
    private static void Streams(float[] landform, float[] drainage, int sizeX, int sizeZ, float climate, SurfaceWaterExtension p,
        Result result, System.Func<int, bool> inRiver)
    {
        float climateLog = Mathf.Log10(Mathf.Max(climate, 1e-3f));
        for (int i = 0; i < landform.Length; i++)
        {
            float runoff = drainage[i] + climateLog;
            if (runoff < p.streamArea || inRiver(i))
                continue;
            float width = Mathf.Clamp(p.widthPerSqrtArea * Mathf.Sqrt(Mathf.Pow(10f, runoff)), 1f, p.maxStreamWidth);
            float depth = p.streamDepth + p.streamDepthPerWidth * width;
            float flow = Mathf.Lerp(p.minFlow, 1f, Mathf.InverseLerp(p.streamArea, p.streamArea + p.fullFlowAreaAbove, runoff));
            float reach = (width - 1f) * 0.5f;
            int r = Mathf.CeilToInt(reach);
            int cx = i % sizeX, cz = i / sizeX;
            for (int dz = -r; dz <= r; dz++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || z < 0 || x >= sizeX || z >= sizeZ || dx * dx + dz * dz > reach * reach + 0.25f)
                        continue;
                    int c = z * sizeX + x;
                    if (inRiver(c))
                        continue;
                    result.Depth[c] = Mathf.Max(result.Depth[c], depth);
                    result.Flow[c] = Mathf.Max(result.Flow[c], flow);
                }
            }
        }
    }

    /// <summary>
    /// Lakes: every closed hollow of the landform (found by filling depressions to their spill level) fills to the
    /// level where evaporation from its surface balances the runoff from its catchment, up to the spill level. A
    /// stream passing through a lake is still water there. Returns the number of lakes.
    /// </summary>
    private static int Lakes(Map map, float[] landform, float[] drainage, int sizeX, int sizeZ, SurfaceWaterExtension p, Result result,
        System.Func<int, bool> inRiver)
    {
        var filled = (float[])landform.Clone();
        GridMath.FillDepressions(filled, sizeX, sizeZ, _ => false, 0f);
        // One hollow per connected flooded area and spill level (a hollow spilling into a lower one touches it).
        GridMath.LabelRegions(i => filled[i] - landform[i] > 1e-4f, sizeX, sizeZ, out int[] labels);
        var hollows = new Dictionary<(int, float), List<int>>();
        for (int i = 0; i < labels.Length; i++)
        {
            if (labels[i] < 0)
                continue;
            var key = (labels[i], filled[i]);
            if (!hollows.TryGetValue(key, out List<int> list))
                hollows[key] = list = new List<int>();
            list.Add(i);
        }

        float rainfall = map.TileInfo?.rainfall ?? 0f;
        float temperature = map.TileInfo?.temperature ?? 10f;
        float evaporation = Mathf.Max(p.minEvaporation, p.evaporationAtZero + p.evaporationPerDegree * temperature);

        int lakes = 0;
        foreach (List<int> hollow in hollows.Values)
        {
            float spill = filled[hollow[0]];
            float catchment = 0f, deepest = 0f;
            foreach (int c in hollow)
            {
                catchment = Mathf.Max(catchment, Mathf.Pow(10f, drainage[c]));
                deepest = Mathf.Max(deepest, spill - landform[c]);
            }
            if (deepest < p.minLakeDepth || hollow.Count < p.minLakeCells)
                continue;

            // Lake area at balance: runoff in = net evaporation out. Wetter than evaporation: fills to the spill level.
            float netEvaporation = evaporation - rainfall;
            float balanceArea = netEvaporation <= 0f ? float.MaxValue : p.runoffShare * rainfall * catchment / netEvaporation;
            hollow.Sort((a, b) => landform[a].CompareTo(landform[b]));
            float level = balanceArea >= hollow.Count ? spill : landform[hollow[Mathf.Clamp((int)balanceArea, 0, hollow.Count - 1)]];
            if (level - landform[hollow[0]] < p.minLakeDepth)
                continue;
            lakes++;
            foreach (int c in hollow)
            {
                if (landform[c] >= level)
                    break;
                // Pools in a river's bed are part of the flowing river.
                if (inRiver(c))
                    continue;
                result.Depth[c] = level - landform[c];
                result.Flow[c] = 0f;
            }
        }
        return lakes;
    }
}

/// <summary>Depth of surface water (metres): lakes in closed hollows and streams along drainage lines (<see cref="SurfaceWaterModel"/>).</summary>
public class SurfaceWaterWorker : EnvironmentLayerWorker
{
    public override GenerationStage StageFor(Map map) => EnvironmentLayerDefOf.LL_Landform.Worker.StageFor(map);

    public override float[] Compute(EnvironmentContext context)
    {
        float[] landform = context.Get(EnvironmentLayerDefOf.LL_Landform);
        float[] drainage = context.Get(EnvironmentLayerDefOf.LL_DrainageArea);
        if (landform == null || drainage == null || !GeologicalTerrain.AppliesTo(context.Map))
            return null;
        var p = def.GetModExtension<SurfaceWaterExtension>() ?? new SurfaceWaterExtension();
        return SurfaceWaterModel.Compute(context.Map, landform, drainage, p, log: true).Depth;
    }
}

/// <summary>Flow speed 0..1 of surface water in stream channels; 0 in still water and on land (<see cref="SurfaceWaterModel"/>).</summary>
public class WaterFlowWorker : EnvironmentLayerWorker
{
    public override GenerationStage StageFor(Map map) => EnvironmentLayerDefOf.LL_Landform.Worker.StageFor(map);

    public override float[] Compute(EnvironmentContext context)
    {
        float[] landform = context.Get(EnvironmentLayerDefOf.LL_Landform);
        float[] drainage = context.Get(EnvironmentLayerDefOf.LL_DrainageArea);
        if (landform == null || drainage == null || !GeologicalTerrain.AppliesTo(context.Map))
            return null;
        var p = EnvironmentLayerDefOf.LL_SurfaceWater.GetModExtension<SurfaceWaterExtension>() ?? new SurfaceWaterExtension();
        return SurfaceWaterModel.Compute(context.Map, landform, drainage, p).Flow;
    }
}
