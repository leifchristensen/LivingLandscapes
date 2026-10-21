using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>Tuning of <see cref="Coastline"/>, set with the landform tuning in XML.</summary>
public class CoastlineExtension
{
    /// <summary>Metres the sea floor drops below the beach, at the deepest (the far side of the map from the shore).</summary>
    public float seaDepth = 14f;

    /// <summary>How far inland the land is drawn down to the beach, in the coast noise's units (0.1 is a tenth of the map's width).</summary>
    public float landFalloff = 0.22f;

    /// <summary>Metres the coastal plain rises per cell inland: it slopes gently to the sea instead of lying dead level, so water on it still has somewhere to go.</summary>
    public float plainSlope = 0.012f;
}

/// <summary>
/// The sea as the lowest ground (Docs/GAMEPLAY-PLAN.md, issue #43). Vanilla's coast mutator paints ocean where its own coast
/// noise falls below a threshold, long after the landform exists, so the geology had a sea that was no lower than the shore
/// (and rivers and drainage ended at the map edge, not in the sea). Here the landform is shaped by the same noise: where the
/// mutator will paint water the ground falls below the beach, deeper with distance from the shore, and inland ground is eased
/// down to the beach level so every slope near the coast runs to the sea. Applies to every tile mutator that paints a
/// coast with <c>TileMutatorWorker_Coast</c> (coast, bay, cove, peninsula...), reading its own noise and thresholds.
/// </summary>
public static class Coastline
{
    private static readonly AccessTools.FieldRef<TileMutatorWorker_Coast, Verse.Noise.ModuleBase> NoiseOf =
        AccessTools.FieldRefAccess<TileMutatorWorker_Coast, Verse.Noise.ModuleBase>("coastNoise");

    private static readonly System.Reflection.MethodInfo NoiseAt = AccessTools.Method(typeof(TileMutatorWorker_Coast), "GetNoiseValue");
    private static readonly System.Reflection.MethodInfo SandLimit = AccessTools.PropertyGetter(typeof(TileMutatorWorker_Coast), "MaxForSand");

    /// <summary>
    /// Cells the map's coast mutator will paint as water or beach (its coast noise below the sand limit), less land rivers
    /// built out over the sea (<see cref="Environment.MapComponent_Environment.PlannedDeltaLand"/>); null on tiles without one.
    /// </summary>
    public static bool[] SeaCells(Map map)
    {
        TileMutatorWorker_Coast coast = Find(map);
        if (coast == null)
            return null;
        float shore = (float)SandLimit.Invoke(coast, null);
        bool[] built = map.GetComponent<Environment.MapComponent_Environment>()?.PlannedDeltaLand;
        var sea = new bool[map.Area];
        for (int i = 0; i < sea.Length; i++)
            sea[i] = (built == null || !built[i]) && (float)NoiseAt.Invoke(coast, new object[] { new IntVec3(i % map.Size.x, 0, i / map.Size.x) }) < shore;
        return sea;
    }

    /// <summary>
    /// How far offshore each point lies (0 at the shore, 1 at the coast noise's deepest), for any map coordinates, including
    /// beyond the map's edges (the erosion model's region); negative on land. False on tiles without a coast.
    /// </summary>
    public static bool TryOffshore(Map map, out System.Func<float, float, float> offshore)
    {
        TileMutatorWorker_Coast coast = Find(map);
        offshore = null;
        if (coast == null)
            return false;
        float shore = (float)SandLimit.Invoke(coast, null);
        offshore = (x, z) => (shore - (float)NoiseAt.Invoke(coast, new object[] { new IntVec3(Mathf.RoundToInt(x), 0, Mathf.RoundToInt(z)) })) / Mathf.Max(shore, 0.01f);
        return true;
    }

    private static TileMutatorWorker_Coast Find(Map map)
    {
        foreach (TileMutatorDef mutator in map.TileInfo.Mutators)
        {
            if (mutator.Worker is TileMutatorWorker_Coast worker && NoiseOf(worker) != null)
                return worker;
        }
        return null;
    }

    /// <summary>Reshapes <paramref name="height"/> (metres above sea level) around the map's coast mutators. Returns the number of sea cells.</summary>
    public static int Apply(Map map, float[] height, CoastlineExtension p)
    {
        TileMutatorWorker_Coast coast = Find(map);
        if (coast == null)
            return 0;
        int sizeX = map.Size.x, n = height.Length;
        float shore = (float)SandLimit.Invoke(coast, null);
        var noise = new float[n];
        var land = new System.Collections.Generic.List<float>();
        bool[] built = map.GetComponent<Environment.MapComponent_Environment>()?.PlannedDeltaLand;
        for (int i = 0; i < n; i++)
        {
            noise[i] = (float)NoiseAt.Invoke(coast, new object[] { new IntVec3(i % sizeX, 0, i / sizeX) });
            // Land the rivers built over the sea is land.
            if (built != null && built[i] && noise[i] < shore)
                noise[i] = shore;
            if (noise[i] >= shore)
                land.Add(height[i]);
        }
        if (land.Count == 0)
            return 0;
        // The beach: the low end of the land (a coastal plain), so no inland ground has to run uphill to reach it.
        land.Sort();
        float beach = land[Mathf.Min(land.Count - 1, land.Count / 50)];
        int sea = 0;
        for (int i = 0; i < n; i++)
        {
            if (noise[i] < shore)
            {
                float offshore = Mathf.Clamp01((shore - noise[i]) / Mathf.Max(shore, 0.01f));
                height[i] = beach - p.seaDepth * offshore * offshore - 0.3f;
                sea++;
            }
            else
            {
                float inland = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(shore, shore + p.landFalloff, noise[i]));
                float plain = beach + p.plainSlope * (noise[i] - shore) * sizeX;
                height[i] = Mathf.Lerp(plain, height[i], inland);
            }
        }
        // Delta setting (evaluation): land near the shore below the sea (incised valleys), and how much of it is low plain.
        float[] toSea = GridMath.DistanceTo(i => noise[i] < shore, sizeX, n / sizeX);
        int near = 0, below = 0, low = 0;
        float deepest = 0f;
        for (int i = 0; i < n; i++)
        {
            if (noise[i] < shore || toSea[i] > 30f)
                continue;
            near++;
            if (height[i] < beach)
            {
                below++;
                deepest = Mathf.Max(deepest, beach - height[i]);
            }
            if (height[i] < beach + 2f)
                low++;
        }
        Log.Message($"[LivingLandscapes] Coast setting: of {near} land cells within 30 of the shore, {below} lie below the beach (deepest {deepest:F1} m), " +
                    $"{low} ({(float)low / Mathf.Max(1, near):P0}) within 2 m above it.");
        return sea;
    }
}
