using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Layers;

/// <summary>Tuning for <see cref="SunWorker"/>, set on the layer Def in XML.</summary>
public class SunExtension : DefModExtension
{
    /// <summary>Exposure of level ground: the scale's anchor, so other layers read slopes as more or less than level ground.</summary>
    public float flatExposure = 0.7f;
}

/// <summary>
/// Sun exposure 0..1 of each site, continuous: the direct sun a slope receives relative to level ground at the map's
/// latitude, times the level ground's exposure. Insolation on a tilted surface (Lambert's cosine law) at the equinox
/// noon, the sun at zenith angle Z = |latitude| due south (north in the southern hemisphere):
///   cos i = cos(beta) cos(Z) + sin(beta) sin(Z) cos(aspect - sun azimuth),   ratio = max(0, cos i) / cos(Z),
/// beta = atan(slope). Sun-facing slopes get more than level ground, slopes facing away less, down to none on steep
/// ones; near the equator every steep slope gets a little less, whatever its aspect.
/// </summary>
public class SunWorker : EnvironmentLayerWorker
{
    /// <summary>Latitude of the map's tile in degrees, positive in the north.</summary>
    public static float LatitudeOf(Map map) => Find.WorldGrid.LongLatOf(map.Tile).y;

    /// <summary>The sun's zenith angle is held below this (degrees), so polar tiles keep a defined ratio.</summary>
    private const float MaxZenith = 80f;

    public override float[] Compute(EnvironmentContext context)
    {
        float[] slope = context.Get(EnvironmentLayerDefOf.LL_Slope);
        float[] aspect = context.Get(EnvironmentLayerDefOf.LL_Aspect);
        if (slope == null || aspect == null)
            return null;

        var p = def.GetModExtension<SunExtension>() ?? new SunExtension();
        float latitude = LatitudeOf(context.Map);
        float sunDirection = latitude >= 0f ? -Mathf.PI / 2f : Mathf.PI / 2f;
        float zenith = Mathf.Min(Mathf.Abs(latitude), MaxZenith) * Mathf.Deg2Rad;
        float cosZ = Mathf.Cos(zenith), sinZ = Mathf.Sin(zenith);

        var sun = new float[slope.Length];
        for (int i = 0; i < sun.Length; i++)
        {
            float beta = Mathf.Atan(slope[i]);
            float cosI = Mathf.Cos(beta) * cosZ + Mathf.Sin(beta) * sinZ * Mathf.Cos(aspect[i] - sunDirection);
            sun[i] = Mathf.Clamp01(p.flatExposure * Mathf.Max(0f, cosI) / cosZ);
        }
        return sun;
    }
}
