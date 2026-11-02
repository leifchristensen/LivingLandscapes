using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using LivingLandscapes.Layers;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Climate;

/// <summary>
/// One temperature map (P23, #90, owner 2026-11-02): an outdoor cell's temperature is the map's outdoor temperature,
/// which stays the mean, plus a local offset from three parts, each a grid computed once from saved layers (not saved):
/// height (lapse rate, always), slope to the sun (by day) and cold air pooling in hollows (on clear, calm nights).
/// Vanilla's cell temperature reads it (<see cref="Patches.Patch_GenTemperature_TryGetTemperatureForCell"/>), so pawns,
/// plant growth, sowing and everything else that asks a cell for its temperature see the same value.
/// </summary>
public class MapComponent_LocalClimate : MapComponent
{
    private float[] height, sun, pool;
    private int nextAttempt;
    private int factorsTick = -1;
    private float day, night;

    public MapComponent_LocalClimate(Map map) : base(map)
    {
    }

    /// <summary>The offset (K) of an outdoor cell from the map's outdoor temperature now; 0 until the layers exist.</summary>
    public float OffsetAt(int index)
    {
        if (height == null && !TryBuild())
            return 0f;
        UpdateFactors();
        return height[index] + day * sun[index] + night * pool[index];
    }

    /// <summary>
    /// The offset (K) of a cell averaged over days and nights, for where species grow (their thermal niche): height, the
    /// sun part for the share of the day it acts, the cold pool for the share of the time one stands.
    /// </summary>
    public float MeanOffsetAt(int index)
    {
        if (height == null && !TryBuild())
            return 0f;
        LocalClimateDef tuning = LocalClimateDef.Current;
        return height[index] + tuning.meanDayShare * sun[index] + tuning.meanPoolShare * pool[index];
    }

    /// <summary>The three parts at a cell (K): height, sun at full day, pool on a clear, calm night. For the dev readout and census.</summary>
    public bool TryGetParts(int index, out float heightPart, out float sunPart, out float poolPart)
    {
        heightPart = sunPart = poolPart = 0f;
        if (height == null && !TryBuild())
            return false;
        heightPart = height[index];
        sunPart = sun[index];
        poolPart = pool[index];
        return true;
    }

    /// <summary>
    /// Day: the sky's glow (0 at night, 1 at noon) scales the sun part. Night: the rest, times calm air and a clear sky,
    /// scales the pool part: cold air drains into hollows only when the ground radiates freely and wind does not mix it.
    /// </summary>
    private void UpdateFactors()
    {
        int now = Find.TickManager?.TicksGame ?? 0;
        if (now == factorsTick)
            return;
        factorsTick = now;
        LocalClimateDef tuning = LocalClimateDef.Current;
        day = Mathf.Clamp01(map.skyManager.CurSkyGlow);
        float calm = 1f - Mathf.Clamp01(map.windManager.WindSpeed / Mathf.Max(tuning.mixingWind, 0.01f));
        WeatherDef weather = map.weatherManager.curWeather;
        float clear = weather != null && weather.rainRate > 0f ? tuning.pooledUnderCloud : 1f;
        night = (1f - day) * calm * clear;
    }

    private bool TryBuild()
    {
        int now = Find.TickManager?.TicksGame ?? 0;
        if (now < nextAttempt)
            return false;
        nextAttempt = now + 2000;
        var environment = map.GetComponent<MapComponent_Environment>();
        if (environment == null
            || !environment.TryGetGrid(EnvironmentLayerDefOf.LL_Landform, out EnvironmentGrid landformGrid)
            || !environment.TryGetGrid(EnvironmentLayerDefOf.LL_Sun, out EnvironmentGrid sunGrid))
            return false;

        LocalClimateDef tuning = LocalClimateDef.Current;
        int sizeX = map.Size.x, sizeZ = map.Size.z;
        float[] landform = landformGrid.ToArray();
        float[] exposure = sunGrid.ToArray();
        float flatExposure = EnvironmentLayerDefOf.LL_Sun.GetModExtension<SunExtension>()?.flatExposure ?? 0.7f;

        // Height: the lapse rate from the map's mean ground height (the map's temperature is its mean).
        float mean = 0f;
        foreach (float h in landform)
            mean += h;
        mean /= landform.Length;

        // Hollows: depth below the spill level of the smoothed ground (Priority-Flood fill, outlets at the map edge).
        float[] smooth = GridMath.Blur(landform, sizeX, sizeZ, tuning.poolSmoothing);
        float[] filled = (float[])smooth.Clone();
        GridMath.FillDepressions(filled, sizeX, sizeZ, _ => false, 0f);

        var heightPart = new float[landform.Length];
        var sunPart = new float[landform.Length];
        var poolPart = new float[landform.Length];
        for (int i = 0; i < landform.Length; i++)
        {
            heightPart[i] = -tuning.lapseRate * tuning.lapseScale * (landform[i] - mean);
            sunPart[i] = tuning.sunWarming * (exposure[i] - flatExposure);
            poolPart[i] = -tuning.poolCooling * Mathf.Clamp01((filled[i] - smooth[i]) / Mathf.Max(tuning.poolDepthFull, 0.01f));
        }
        height = heightPart;
        sun = sunPart;
        pool = poolPart;
        Log.Message($"[LivingLandscapes] Local climate on {map}: height {Range(height)} K, sun by day {Range(sun)} K, " +
                    $"cold pool on a calm clear night {Range(pool)} K, hollow cells {Share(pool, p => p < -0.5f):P0}.");
        return true;
    }

    private static string Range(float[] values)
    {
        float[] sorted = (float[])values.Clone();
        System.Array.Sort(sorted);
        float At(float q) => sorted[Mathf.Clamp((int)(q * (sorted.Length - 1)), 0, sorted.Length - 1)];
        return $"{At(0f):F1}/{At(0.05f):F1}/{At(0.5f):F1}/{At(0.95f):F1}/{At(1f):F1} (min/p5/median/p95/max)";
    }

    private static float Share(float[] values, System.Func<float, bool> test)
    {
        int count = 0;
        foreach (float v in values)
            if (test(v))
                count++;
        return values.Length == 0 ? 0f : (float)count / values.Length;
    }

    private static Map cachedMap;
    private static MapComponent_LocalClimate cachedClimate;

    /// <summary>The offset of a cell on a map, with the component cached (vanilla asks for cell temperatures often).</summary>
    public static float OffsetAt(Map map, IntVec3 c)
    {
        if (map != cachedMap)
        {
            cachedMap = map;
            cachedClimate = map.GetComponent<MapComponent_LocalClimate>();
        }
        return cachedClimate == null ? 0f : cachedClimate.OffsetAt(map.cellIndices.CellToIndex(c));
    }
}
