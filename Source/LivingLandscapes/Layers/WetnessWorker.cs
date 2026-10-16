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

    /// <summary>0..1 climate wetness of a map's tile from its rainfall.</summary>
    public float ClimateOf(Map map)
    {
        float rainfall = map.TileInfo?.rainfall ?? 0f;
        return rainfall > 0f ? Mathf.Clamp01(rainfall / wetRainfallMm) : unknownRainfall;
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
        var ones = new float[height.Length];
        for (int i = 0; i < ones.Length; i++)
            ones[i] = 1f;
        float[] slope = GridMath.Slope(GridMath.MaskedBlur(height, ones, context.SizeX, context.SizeZ, p.slopeSmoothing), context.SizeX, context.SizeZ);
        float cellSize = (EnvironmentLayerDefOf.LL_Landform.GetModExtension<GeologyGenerationExtension>() ?? new GeologyGenerationExtension()).cellSize;
        int n = height.Length;

        float[] drainageHeight = DrainageLineHeight(height, drainage, p.channelArea, context.SizeX, context.SizeZ);

        float exponent = Mathf.Lerp(p.aridSharpening, 1f, p.ClimateOf(context.Map));
        var wetness = new float[n];
        for (int i = 0; i < n; i++)
        {
            float area = Mathf.Pow(10f, drainage[i]);
            float tanSlope = slope[i] / cellSize;
            float index = Mathf.Log(area / Mathf.Max(tanSlope, 0.01f));
            float indexWet = Mathf.InverseLerp(p.wetnessIndexRange.min, p.wetnessIndexRange.max, index);
            float aboveDrainage = Mathf.Max(0f, height[i] - drainageHeight[i]);
            float groundwater = Mathf.Exp(-aboveDrainage / p.heightAboveDrainageScale);
            float topographic = p.wetnessIndexWeight * indexWet + (1f - p.wetnessIndexWeight) * groundwater;
            wetness[i] = Mathf.Pow(Mathf.Clamp01(topographic), exponent);
        }
        return wetness;
    }

    /// <summary>
    /// Height of the drainage line each cell drains to, following steepest descent (D8) until a cell whose drainage
    /// area reaches <paramref name="channelArea"/>. Cells are visited from low to high, so each cell's downhill
    /// neighbour is already resolved. Pits and map edges without a channel count as their own drainage line.
    /// </summary>
    private static float[] DrainageLineHeight(float[] height, float[] drainage, float channelArea, int sizeX, int sizeZ)
    {
        int n = height.Length;
        var order = new int[n];
        for (int i = 0; i < n; i++)
            order[i] = i;
        System.Array.Sort(order, (a, b) => height[a].CompareTo(height[b]));
        var result = new float[n];
        foreach (int c in order)
        {
            if (drainage[c] >= channelArea)
            {
                result[c] = height[c];
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
        }
        return result;
    }
}
