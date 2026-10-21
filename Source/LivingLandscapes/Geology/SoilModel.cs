using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>Tuning for soil depth and bedrock exposure, set on the soil depth layer Def in XML. Lengths in metres.</summary>
public class SoilModelExtension : DefModExtension
{
    /// <summary>Soil depth on level, well-drained ground.</summary>
    public float baseSoilDepth = 1.2f;

    /// <summary>Slope (rise over run) at which no soil can stay: bare rock face. 0.84 is about 40 degrees.</summary>
    public float bareRockSlope = 0.84f;

    /// <summary>Extra soil in hollows and valley floors, where creep and wash collect it (fraction of base depth).</summary>
    public float hollowBonus = 1.0f;

    /// <summary>Soil missing on crests and noses, where it is shed (fraction of base depth).</summary>
    public float crestLoss = 0.5f;

    /// <summary>
    /// Soil depth multiplier on the weakest and the strongest rock: weak rock weathers into deep soil, strong rock
    /// stays bare, so hard beds show as rock ridges.
    /// </summary>
    public float soilOnWeakRock = 1.5f;
    public float soilOnStrongRock = 0.3f;

    /// <summary>
    /// The rock's effect on soil depth grows with slope over this range (rise over run): on level ground soil weathers in
    /// place and covers any rock, so a hard bed shows as bare rock only where it cuts a slope. With near-horizontal beds
    /// that draws the bands of rock along the contours.
    /// </summary>
    public FloatRange rockEffectSlope = new FloatRange(0.03f, 0.25f);

    /// <summary>
    /// Bare-rock slope multiplier on the weakest and strongest rock: strong rock weathers too slowly to keep soil on
    /// moderate slopes, so it is bare at gentler slopes than weak rock.
    /// </summary>
    public float bareSlopeOnWeakRock = 1.3f;
    public float bareSlopeOnStrongRock = 0.5f;

    /// <summary>Below this soil depth bedrock is bare (ground rules: RockDepth, RockDistance).</summary>
    public float rockWallDepth = 0.05f;

    /// <summary>
    /// Share of the map that stands as mountain rock (bare ground at least <see cref="mountainDepth"/> metres from soil), by tile
    /// hilliness: the range vanilla's own maps fall in (measured over seeds). A map draws its target in the range, then
    /// soil depth is shifted evenly until the geology's rock covers that share, so a tile's hilliness means what it does in
    /// vanilla whatever the relief the erosion model produced. Zero range: no calibration.
    /// </summary>
    public FloatRange rockShareFlat = new FloatRange(0.03f, 0.08f);
    public FloatRange rockShareSmallHills = new FloatRange(0.08f, 0.15f);
    public FloatRange rockShareLargeHills = new FloatRange(0.14f, 0.23f);
    public FloatRange rockShareMountainous = new FloatRange(0.22f, 0.32f);
    public FloatRange rockShareImpassable = new FloatRange(0.35f, 0.5f);

    /// <summary>Metres of bare rock from soil at which ground becomes mountain (the ground rules' mountain class).</summary>
    public float mountainDepth = 4f;

    /// <summary>Gaussian radius (cells) of the soil depth the rock calibration works on: the size of the outcrops it makes.</summary>
    public float outcropSigma = 3f;

    /// <summary>Soil depth (metres) of delta land: fresh sediment, never bare rock.</summary>
    public float deltaSoilDepth = 2f;

    /// <summary>
    /// Passes (without a Z-level mod a ridge, bluff or canyon rim must not cut a map in two): every walkable area of at least
    /// this share of the map is joined to the largest by a col, the cheapest way over the rock, preferring thin rock and
    /// gentle climbs. The col is mountain rock thinned to bare rock (walkable); it widens wherever the rock beside it is no
    /// thicker, so it follows the terrain, at least one cell wide. Smaller pockets stay hidden in the rock. 0: no passes.
    /// </summary>
    public float passMinAreaShare = 0.05f;

    /// <summary>Path cost per metre of rock crossed and per metre climbed, against 1 per cell of open ground.</summary>
    public float passRockCost = 3f;
    public float passClimbCost = 2f;

    /// <summary>How far (cells) a col may widen to each side into rock no thicker than itself.</summary>
    public int passWiden = 2;

    public FloatRange RockShareFor(Hilliness hilliness)
    {
        switch (hilliness)
        {
            case Hilliness.Flat: return rockShareFlat;
            case Hilliness.SmallHills: return rockShareSmallHills;
            case Hilliness.LargeHills: return rockShareLargeHills;
            case Hilliness.Mountainous: return rockShareMountainous;
            case Hilliness.Impassable: return rockShareImpassable;
            default: return FloatRange.Zero;
        }
    }

}

/// <summary>
/// Soil depth from landform: soil thins with slope (none stays above the bare-rock slope), collects where flow
/// converges (hollows, valley floors) and is shed from divergent crests. A standard steady-state hillslope
/// approximation; exact soil production models can replace it without changing callers.
/// </summary>
public static class SoilModel
{
    /// <summary>
    /// Thins soil so the share of mountain rock equals <paramref name="target"/>; returns the shift in metres. The shift is applied
    /// to the soil depth smoothed over <see cref="SoilModelExtension.outcropSigma"/> cells, so the rock that appears is outcrops and
    /// knolls where the soil is thinnest over a neighbourhood (crests, strong beds), not single cells. Which ground turns to rock still
    /// follows slope, convergence and rock strength: only how much changes.
    /// </summary>
    /// <summary>Share of the land (cells not under the sea) that stands as mountain rock, from each cell's distance to soil in cells.</summary>
    public static float RockShare(float[] depthCells, float cellSize, SoilModelExtension p, bool[] sea)
    {
        int rock = 0, land = 0;
        for (int i = 0; i < depthCells.Length; i++)
        {
            if (sea != null && sea[i])
                continue;
            land++;
            if (depthCells[i] * cellSize >= p.mountainDepth)
                rock++;
        }
        return rock / (float)Mathf.Max(land, 1);
    }

    /// <param name="exposure">Where thinning bites, 0..1 per cell (hard beds cutting slopes); null thins evenly.</param>
    public static float CalibrateRock(float[] soil, int sizeX, int sizeZ, float cellSize, SoilModelExtension p, float target, bool[] sea = null,
        float[] exposure = null)
    {
        float[] smooth = GridMath.Blur(soil, sizeX, sizeZ, p.outcropSigma);
        float Thinned(int i, float shift) => smooth[i] - shift * (exposure == null ? 1f : exposure[i]);
        float Share(float shift)
        {
            float[] depth = GridMath.DistanceTo(i => Thinned(i, shift) >= p.rockWallDepth, sizeX, sizeZ);
            return RockShare(depth, cellSize, p, sea);
        }
        float low = 0f, high = exposure == null ? 3f : 12f;
        for (int step = 0; step < 14; step++)
        {
            float mid = (low + high) / 2f;
            if (Share(mid) < target)
                low = mid;
            else
                high = mid;
        }
        float shift = (low + high) / 2f;
        for (int i = 0; i < soil.Length; i++)
            soil[i] = Mathf.Min(soil[i], Mathf.Max(0f, Thinned(i, shift)));
        return shift;
    }

    /// <summary>
    /// Where rock would show first if the ground were rockier: hard beds where they cut a slope (strength times slope against
    /// the map's own steep ground), smoothed to outcrop size, 0..1. Calibration thins soil there, so the extra rock a tile's
    /// hilliness asks for appears as bands along the contours rather than as plains of thin soil.
    /// </summary>
    public static float[] Exposure(float[] height, float[] rockStrength, int sizeX, int sizeZ, SoilModelExtension p)
    {
        float[] slope = GridMath.Slope(height, sizeX, sizeZ);
        var sorted = (float[])slope.Clone();
        System.Array.Sort(sorted);
        float steep = Mathf.Max(sorted[(int)(sorted.Length * 0.9f)], 1e-4f);
        var exposure = new float[slope.Length];
        for (int i = 0; i < slope.Length; i++)
            exposure[i] = (rockStrength == null ? 0.5f : rockStrength[i]) * Mathf.Clamp01(slope[i] / steep);
        exposure = GridMath.Blur(exposure, sizeX, sizeZ, p.outcropSigma);
        float max = 1e-4f;
        foreach (float e in exposure)
            max = Mathf.Max(max, e);
        for (int i = 0; i < exposure.Length; i++)
            exposure[i] /= max;
        return exposure;
    }

    /// <param name="rockStrength">Rock strength (0..1) per cell, or null when unknown (no rock effect).</param>
    public static float[] SoilDepth(float[] height, int sizeX, int sizeZ, float cellSize, SoilModelExtension p, float[] rockStrength = null, float bareSlopeScale = 1f)
    {
        float[] slope = GridMath.Slope(height, sizeX, sizeZ);
        float[] area = GridMath.FlowAccumulationMfd(height, sizeX, sizeZ);
        var soil = new float[height.Length];
        for (int i = 0; i < soil.Length; i++)
        {
            float gradient = slope[i] / cellSize;
            // 0 on divergent crests (area ~1 cell), 1 in strongly convergent hollows (area >= 1000 cells).
            float convergence = Mathf.Clamp01(Mathf.Log10(area[i]) / 3f);
            float landform = 1f + p.hollowBonus * convergence - p.crestLoss * (1f - convergence);
            float strength = rockStrength == null ? -1f : rockStrength[i];
            float bareSlope = bareSlopeScale * (strength < 0f ? p.bareRockSlope : p.bareRockSlope * Mathf.Lerp(p.bareSlopeOnWeakRock, p.bareSlopeOnStrongRock, strength));
            float steepness = Mathf.Clamp01(1f - gradient / bareSlope);
            float onSlope = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(p.rockEffectSlope.min, p.rockEffectSlope.max, gradient));
            float rock = strength < 0f ? 1f : Mathf.Lerp(1f, Mathf.Lerp(p.soilOnWeakRock, p.soilOnStrongRock, strength), onSlope);
            soil[i] = Mathf.Max(0f, p.baseSoilDepth * landform * steepness * rock);
        }
        return soil;
    }
}
