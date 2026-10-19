using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Layers;

/// <summary>Tuning for <see cref="SunWorker"/>, set on the layer Def in XML.</summary>
public class SunExtension : DefModExtension
{
    /// <summary>Exposure of level ground.</summary>
    public float flatExposure = 0.7f;

    /// <summary>Exposure gained per unit of slope facing the sun (and lost facing away) at mid latitudes.</summary>
    public float slopeGain = 1.5f;

    /// <summary>Latitude (degrees) from which slope aspect counts in full. Nearer the equator the sun is overhead and aspect matters less.</summary>
    public float fullAspectLatitude = 40f;
}

/// <summary>
/// Sun exposure 0..1 of each site: level ground gets the flat value, slopes facing the sun more, slopes facing away
/// less. The sun stands to the south in the northern hemisphere and to the north in the southern; near the equator
/// aspect matters little.
/// </summary>
public class SunWorker : EnvironmentLayerWorker
{
    /// <summary>Latitude of the map's tile in degrees, positive in the north.</summary>
    public static float LatitudeOf(Map map) => Find.WorldGrid.LongLatOf(map.Tile).y;

    public override float[] Compute(EnvironmentContext context)
    {
        float[] slope = context.Get(EnvironmentLayerDefOf.LL_Slope);
        float[] aspect = context.Get(EnvironmentLayerDefOf.LL_Aspect);
        if (slope == null || aspect == null)
            return null;

        var p = def.GetModExtension<SunExtension>() ?? new SunExtension();
        float latitude = LatitudeOf(context.Map);
        float sunDirection = latitude >= 0f ? -Mathf.PI / 2f : Mathf.PI / 2f;
        float gain = p.slopeGain * Mathf.Clamp01(Mathf.Abs(latitude) / Mathf.Max(p.fullAspectLatitude, 1f));

        var sun = new float[slope.Length];
        for (int i = 0; i < sun.Length; i++)
            sun[i] = Mathf.Clamp01(p.flatExposure + gain * GridMath.SlopeToward(slope[i], aspect[i], sunDirection));
        return sun;
    }
}
