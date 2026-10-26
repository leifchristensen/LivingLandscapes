using System.Collections.Generic;
using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Layers;

/// <summary>A biome's disturbance regime: rotation at the biome's typical rainfall, and the share of events that are fires.</summary>
public class DisturbanceRegime
{
    public BiomeDef biome;

    /// <summary>Mean years between stand-replacing disturbances of a given spot (fire or disturbance rotation R).</summary>
    public float years = 200f;

    /// <summary>Rainfall (mm) at which <see cref="years"/> applies; the tile's own rainfall scales R (P22, equation 4).</summary>
    public float rainfall = 1000f;

    /// <summary>Share of events that are fires (spread on dry ground, stop at wet); the rest are windthrow on exposed ground.</summary>
    public float fireShare = 0.5f;
}

/// <summary>Tuning for <see cref="StandAgeWorker"/>, set on the layer Def in XML (tools/Docs/DISTURBANCE-MOSAIC.md).</summary>
public class StandAgeExtension : DefModExtension
{
    public List<DisturbanceRegime> regimes = new List<DisturbanceRegime>();

    /// <summary>Regime of biomes without an entry.</summary>
    public DisturbanceRegime fallback = new DisturbanceRegime();

    /// <summary>Fuel builds with rain: fuel = 1 - exp(-P / this) (mm).</summary>
    public float fuelRainfall = 400f;

    /// <summary>Dry spells fade with rain: dryness = exp(-P / this) (mm).</summary>
    public float drynessRainfall = 1500f;

    /// <summary>Limits on how far the tile's climate moves R from the biome's value (times).</summary>
    public FloatRange climateScale = new FloatRange(0.33f, 3f);

    /// <summary>Years simulated, in rotations: older ground has not burnt or blown down within the record.</summary>
    public float horizonRotations = 3f;

    /// <summary>Patch sizes (cells): P(size > s) ~ s^-beta, truncated to this range.</summary>
    public IntRange patchCells = new IntRange(20, 3000);

    /// <summary>beta for fires (heavy tail: a few large burns).</summary>
    public float fireSizeExponent = 0.4f;

    /// <summary>beta for windthrow (steeper: mostly small gaps).</summary>
    public float windSizeExponent = 1f;

    /// <summary>Moisture above which a fire stops (wet ground, marsh).</summary>
    public float fireBreakMoisture = 0.75f;

    /// <summary>Soil depth (m) below which ground is bare rock: nothing to burn or blow down.</summary>
    public float bareRockSoil = 0.08f;

    /// <summary>Radius (cells) of the surroundings an exposed spot stands above (windthrow).</summary>
    public float exposureRadius = 12f;

    /// <summary>Spread cost: random factor range per step, which makes patch edges irregular.</summary>
    public FloatRange spreadNoise = new FloatRange(0.5f, 1.5f);
}

/// <summary>
/// Years since the last stand-replacing disturbance (P22, shifting mosaic). Disturbances are simulated over a few
/// rotations: events arrive at random (so ages follow P(age > a) = exp(-a / R)), each starts where fuel is driest
/// (fire) or the ground most exposed (windthrow), draws a heavy-tailed size, and spreads over the cheapest ground,
/// stopping at water, wet ground and bare rock. A cell's age is the time since the latest event over it; ground no event
/// reached is older than the record. Plants read it for succession (stage by age); nothing changes plants yet.
/// </summary>
public class StandAgeWorker : EnvironmentLayerWorker
{
    public override float[] Compute(EnvironmentContext context)
    {
        Map map = context.Map;
        float[] moisture = context.Get(EnvironmentLayerDefOf.LL_Moisture);
        float[] sun = context.Get(EnvironmentLayerDefOf.LL_Sun);
        float[] soil = context.Get(EnvironmentLayerDefOf.LL_SoilDepth);
        float[] elevation = context.Get(EnvironmentLayerDefOf.LL_Elevation);
        if (moisture == null || soil == null)
            return null;
        var p = def.GetModExtension<StandAgeExtension>() ?? new StandAgeExtension();
        int sizeX = context.SizeX, sizeZ = context.SizeZ, n = context.CellCount;

        DisturbanceRegime regime = p.regimes.Find(r => r.biome == map.Biome) ?? p.fallback;
        float rotation = Rotation(regime, map.TileInfo?.rainfall ?? regime.rainfall, p);
        float horizon = rotation * p.horizonRotations;

        // Ground that can carry a stand: not water, not bare rock.
        var stand = new bool[n];
        int standCells = 0;
        for (int i = 0; i < n; i++)
        {
            stand[i] = !map.terrainGrid.TerrainAt(i).IsWater && soil[i] >= p.bareRockSoil;
            if (stand[i])
                standCells++;
        }
        var age = new float[n];
        for (int i = 0; i < n; i++)
            age[i] = horizon;
        if (standCells == 0)
            return age;

        // Exposure for windthrow: height above the mean of the surroundings.
        float[] exposure = null;
        if (elevation != null)
        {
            float[] around = GridMath.Blur(elevation, sizeX, sizeZ, p.exposureRadius);
            exposure = new float[n];
            for (int i = 0; i < n; i++)
                exposure[i] = elevation[i] - around[i];
        }

        // Events in the record: rate x horizon = standCells x horizonRotations / mean patch size.
        float meanFire = MeanSize(p.patchCells, p.fireSizeExponent), meanWind = MeanSize(p.patchCells, p.windSizeExponent);
        float meanSize = regime.fireShare * meanFire + (1f - regime.fireShare) * meanWind;
        int events = Mathf.RoundToInt(standCells * p.horizonRotations / meanSize);
        var times = new List<float>(events);
        for (int e = 0; e < events; e++)
            times.Add(Rand.Range(0f, horizon));
        // Oldest first, so later events overwrite earlier ones.
        times.Sort((a, b) => b.CompareTo(a));

        var stamp = new int[n];
        int painted = 0;
        for (int e = 0; e < times.Count; e++)
        {
            bool fire = Rand.Chance(regime.fireShare);
            int size = Mathf.RoundToInt(DrawSize(p.patchCells, fire ? p.fireSizeExponent : p.windSizeExponent));
            int start = PickStart(stand, moisture, sun, exposure, fire, n);
            if (start < 0)
                continue;
            painted += Spread(start, size, e + 1, times[e], fire, stand, moisture, sun, p, sizeX, sizeZ, stamp, age);
        }
        Log.Message($"[LivingLandscapes] Stand age on {map}: rotation {rotation:F0} years ({map.Biome.defName}, rainfall {map.TileInfo?.rainfall ?? 0f:F0} mm), " +
                    $"{events} disturbances over {horizon:F0} years, fire share {regime.fireShare:0.##}, {painted} cells painted.");
        return age;
    }

    /// <summary>
    /// R for the tile: the biome's R scaled by fire weather (P22, equation 4): 1 / R ~ fuel(P) x dryness(P), with
    /// fuel = 1 - exp(-P / P0) and dryness = exp(-P / P1), relative to the biome's typical rainfall.
    /// </summary>
    public static float Rotation(DisturbanceRegime regime, float rainfall, StandAgeExtension p)
    {
        float Weather(float rain) => (1f - Mathf.Exp(-rain / p.fuelRainfall)) * Mathf.Exp(-rain / p.drynessRainfall);
        float tile = Weather(Mathf.Max(rainfall, 1f)), typical = Weather(Mathf.Max(regime.rainfall, 1f));
        float scale = tile > 0f ? Mathf.Clamp(typical / tile, p.climateScale.min, p.climateScale.max) : p.climateScale.max;
        return regime.years * scale;
    }

    /// <summary>Inverse CDF of the truncated power law P(size > s) ~ s^-beta on [min, max].</summary>
    private static float DrawSize(IntRange range, float beta)
    {
        float a = Mathf.Pow(range.min, -beta), b = Mathf.Pow(range.max, -beta);
        return Mathf.Pow(a - Rand.Value * (a - b), -1f / beta);
    }

    private static float MeanSize(IntRange range, float beta)
    {
        // Sampled once per map; a closed form exists but this stays obviously right for any beta.
        double sum = 0;
        var state = new System.Random(17);
        float a = Mathf.Pow(range.min, -beta), b = Mathf.Pow(range.max, -beta);
        for (int k = 0; k < 4000; k++)
            sum += Mathf.Pow(a - (float)state.NextDouble() * (a - b), -1f / beta);
        return (float)(sum / 4000);
    }

    /// <summary>A start cell weighted by dry, sunny fuel (fire) or by exposure (windthrow), from a handful of random tries.</summary>
    private static int PickStart(bool[] stand, float[] moisture, float[] sun, float[] exposure, bool fire, int n)
    {
        int best = -1;
        float bestScore = float.MinValue;
        for (int k = 0; k < 8; k++)
        {
            int i = Rand.Range(0, n);
            if (!stand[i])
                continue;
            float score = fire ? (1f - moisture[i]) * (sun?[i] ?? 1f) : (exposure?[i] ?? 0f);
            score += Rand.Value * 0.05f;
            if (score > bestScore)
            {
                bestScore = score;
                best = i;
            }
        }
        return best;
    }

    /// <summary>
    /// Grows one patch from <paramref name="start"/> over the cheapest ground (Dijkstra with a random factor per step) until
    /// it reaches <paramref name="size"/> cells or runs out of ground. Fire is cheap on dry, sunny ground and stops at
    /// wet ground; windthrow spreads evenly. Returns the cells painted.
    /// </summary>
    private static int Spread(int start, int size, int id, float yearsAgo, bool fire, bool[] stand, float[] moisture, float[] sun,
                              StandAgeExtension p, int sizeX, int sizeZ, int[] stamp, float[] age)
    {
        var open = new SortedSet<(float cost, int cell)>();
        var best = new Dictionary<int, float> { [start] = 0f };
        open.Add((0f, start));
        int painted = 0;
        while (open.Count > 0 && painted < size)
        {
            (float cost, int cell) = open.Min;
            open.Remove(open.Min);
            if (stamp[cell] == id)
                continue;
            stamp[cell] = id;
            age[cell] = yearsAgo;
            painted++;
            int x = cell % sizeX, z = cell / sizeX;
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0)
                        continue;
                    int nx = x + dx, nz = z + dz;
                    if (nx < 0 || nz < 0 || nx >= sizeX || nz >= sizeZ)
                        continue;
                    int next = nz * sizeX + nx;
                    if (!stand[next] || stamp[next] == id)
                        continue;
                    if (fire && moisture[next] >= p.fireBreakMoisture)
                        continue;
                    float resistance = fire ? 0.5f + moisture[next] * 2f - ((sun?[next] ?? 0.7f) - 0.7f) : 1f;
                    float step = (dx != 0 && dz != 0 ? 1.41f : 1f) * Mathf.Max(resistance, 0.1f) * p.spreadNoise.RandomInRange;
                    float total = cost + step;
                    if (best.TryGetValue(next, out float known) && known <= total)
                        continue;
                    best[next] = total;
                    open.Add((total, next));
                }
            }
        }
        return painted;
    }
}
