using System.Collections.Generic;
using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Layers;

/// <summary>Tuning for <see cref="WetnessWorker"/>, set on the layer Def in XML.</summary>
public class WetnessExtension : DefModExtension
{
    /// <summary>Drainage area (log10 cells) from which a cell counts as a drainage line (gully, wash, stream).</summary>
    public float channelArea = 2.5f;

    /// <summary>Height above the nearest drainage line (metres) over which groundwater wetness falls to 1/e.</summary>
    public float heightAboveDrainageScale = 2f;

    /// <summary>
    /// Floodplains: larger channels flood higher and keep the water table up over a wider band. The height scale above
    /// grows by this share for every tenfold of drainage area beyond <see cref="channelArea"/>, so a great river gets a
    /// broad wet floodplain and a gully a narrow damp strip.
    /// </summary>
    public float floodGrowthPerDecade = 0.4f;

    /// <summary>Smoothing (cells) of the surface whose slope enters the wetness index, so boulders and hummocks don't speckle it.</summary>
    public float slopeSmoothing = 2f;

    /// <summary>Wetness index range mapped to 0..1: ln(upslope area / slope).</summary>
    public FloatRange wetnessIndexRange = new FloatRange(4f, 13f);

    /// <summary>Share of the wetness index (versus height above drainage) in topographic wetness.</summary>
    public float wetnessIndexWeight = 0.5f;

    /// <summary>
    /// Tile rainfall (mm) treated as fully wet. Drier tiles sharpen wetness: only the strongest drainage lines stay
    /// wet, so moisture in deserts follows washes closely.
    /// </summary>
    public float wetRainfallMm = 1500f;

    /// <summary>Sharpening exponent on the driest tiles (1 = none, on fully wet tiles).</summary>
    public float aridSharpening = 3f;

    /// <summary>Rainfall share assumed when the tile reports none (pocket maps, mod-made tiles).</summary>
    public float unknownRainfall = 0.4f;

    /// <summary>Wetness of the ground beside a seep, as a share of the seep's own.</summary>
    public float seepFringe = 0.85f;

    /// <summary>
    /// Wetness of a seep by how close it comes to running water (0..1, its width as a share of moving water's): just
    /// below a stream it is saturated (marsh, mud), where little water gathers a damp or dry bed (the wash rules).
    /// </summary>
    public SimpleCurve seepWetness = new SimpleCurve
    {
        new CurvePoint(0f, 0.5f),
        new CurvePoint(0.4f, 0.8f),
        new CurvePoint(0.7f, 0.9f),
        new CurvePoint(1f, 1f),
    };

    /// <summary>
    /// 0..1 climate wetness of a map's tile from its rainfall, shifted by the map's water regime (a wetland is wet
    /// whatever its rainfall).
    /// </summary>
    public float ClimateOf(Map map)
    {
        float rainfall = map.TileInfo?.rainfall ?? 0f;
        float climate = rainfall > 0f ? rainfall / wetRainfallMm : unknownRainfall;
        return Mathf.Clamp01(climate + (map.GetComponent<MapComponent_Environment>()?.PlannedRivers?.Regime?.climateOffset ?? 0f));
    }
}

/// <summary>
/// 0..1 topographic wetness: where water collects and the water table lies close to the surface, before any surface
/// water is placed. Combines the wetness index ln(upslope area / slope) with the height above the nearest drainage
/// line (low ground beside washes and streams stays damp), then sharpens it on dry tiles. Drives soil fertility, wet
/// ground (mud, marsh, ponds) and moisture.
/// </summary>
public class WetnessWorker : EnvironmentLayerWorker
{
    public override GenerationStage StageFor(Map map) => EnvironmentLayerDefOf.LL_Height.Worker.StageFor(map);

    public override float[] Compute(EnvironmentContext context)
    {
        // The natural surface, not the terraced one: terrace treads are flat and would read as pits.
        float[] height = context.Get(EnvironmentLayerDefOf.LL_Landform);
        float[] drainage = context.Get(EnvironmentLayerDefOf.LL_DrainageArea);
        if (height == null || drainage == null)
            return null;
        var p = def.GetModExtension<WetnessExtension>() ?? new WetnessExtension();
        float[] slope = GridMath.Slope(GridMath.Blur(height, context.SizeX, context.SizeZ, p.slopeSmoothing), context.SizeX, context.SizeZ);
        float cellSize = (EnvironmentLayerDefOf.LL_Landform.GetModExtension<GeologyGenerationExtension>() ?? new GeologyGenerationExtension()).cellSize;
        int n = height.Length;

        // The water table meets a river at its water surface, not its bed.
        RiverSystem rivers = context.Map.GetComponent<MapComponent_Environment>()?.PlannedRivers;
        float[] drainageHeight = DrainageLineHeight(height, drainage, rivers?.WaterLevel, rivers?.ChannelOf, p.channelArea,
            context.SizeX, context.SizeZ, out float[] lineArea);

        float exponent = Mathf.Lerp(p.aridSharpening, 1f, p.ClimateOf(context.Map));
        var wetness = new float[n];
        for (int i = 0; i < n; i++)
        {
            float area = Mathf.Pow(10f, drainage[i]);
            float tanSlope = slope[i] / cellSize;
            float index = Mathf.Log(area / Mathf.Max(tanSlope, 0.01f));
            float indexWet = Mathf.InverseLerp(p.wetnessIndexRange.min, p.wetnessIndexRange.max, index);
            float aboveDrainage = Mathf.Max(0f, height[i] - drainageHeight[i]);
            float floodScale = p.heightAboveDrainageScale * (1f + p.floodGrowthPerDecade * Mathf.Max(0f, lineArea[i] - p.channelArea))
                               * (rivers?.Regime?.floodplainScale ?? 1f);
            float groundwater = Mathf.Exp(-aboveDrainage / floodScale);
            float topographic = p.wetnessIndexWeight * indexWet + (1f - p.wetnessIndexWeight) * groundwater;
            wetness[i] = Mathf.Pow(Mathf.Clamp01(topographic), exponent);
        }

        // Seeps: drainage lines the ground soaks up are wet by how close they come to running (saturated just below a
        // stream, a damp or dry bed where little water gathers), and the ground beside them nearly as wet.
        if (rivers?.Seep != null)
        {
            for (int i = 0; i < n; i++)
            {
                if (!rivers.Seep[i])
                    continue;
                float seep = p.seepWetness.Evaluate(rivers.SeepWater?[i] ?? 1f);
                wetness[i] = Mathf.Max(wetness[i], seep);
                float fringe = seep * p.seepFringe;
                GridMath.ForEachNeighbour(i, context.SizeX, context.SizeZ, nb => wetness[nb] = Mathf.Max(wetness[nb], fringe));
            }
        }
        return wetness;
    }

    /// <summary>
    /// Height of the drainage line each cell drains to, following steepest descent (D8) until a cell whose drainage
    /// area reaches <paramref name="channelArea"/>. Cells are visited from low to high, so each cell's downhill
    /// neighbour is already resolved. Pits and map edges without a channel count as their own drainage line.
    /// <paramref name="lineArea"/> is the drainage area (log10 cells) of that line where the cell reaches it.
    /// </summary>
    private static float[] DrainageLineHeight(float[] height, float[] drainage, float[] riverLevel, int[] channelOf, float channelArea,
        int sizeX, int sizeZ, out float[] lineArea)
    {
        int n = height.Length;
        var order = new int[n];
        for (int i = 0; i < n; i++)
            order[i] = i;
        System.Array.Sort(order, (a, b) => height[a].CompareTo(height[b]));
        var result = new float[n];
        var area = new float[n];
        // A channel's discharge belongs to its whole width, rims included.
        var channelAreas = new Dictionary<int, float>();
        for (int i = 0; channelOf != null && i < n; i++)
        {
            if (channelOf[i] >= 0)
                channelAreas[channelOf[i]] = Mathf.Max(channelAreas.TryGetValue(channelOf[i], out float a) ? a : 0f, drainage[i]);
        }
        foreach (int c in order)
        {
            if (riverLevel != null && !float.IsNaN(riverLevel[c]))
            {
                result[c] = riverLevel[c];
                area[c] = channelOf != null && channelOf[c] >= 0 ? channelAreas[channelOf[c]] : drainage[c];
                continue;
            }
            if (drainage[c] >= channelArea)
            {
                result[c] = height[c];
                area[c] = drainage[c];
                continue;
            }
            int lowest = -1;
            float best = 0f;
            GridMath.ForEachNeighbour(c, sizeX, sizeZ, nb =>
            {
                float drop = height[c] - height[nb];
                if (drop > best)
                {
                    best = drop;
                    lowest = nb;
                }
            });
            result[c] = lowest >= 0 ? result[lowest] : height[c];
            area[c] = lowest >= 0 ? area[lowest] : drainage[c];
        }
        lineArea = area;
        return result;
    }
}
