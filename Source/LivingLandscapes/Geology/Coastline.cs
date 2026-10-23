using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>Tuning of <see cref="Coastline"/>, set with the landform tuning in XML.</summary>
public class CoastlineExtension
{
    /// <summary>
    /// The sea floor (issue #59): a gentle shelf out to the shelf break, shelfDepth metres below the beach at shelfWidth cells
    /// from the shore, then a steeper slope (metres per cell) down to maxDepth. Low coasts have wide shelves and steep coasts
    /// narrow ones (passive and active margins), so the shelf's width follows the tile's hilliness. Both the coast on the map
    /// and the erosion model's sea (the room river sediment fills) use this one profile.
    /// </summary>
    public float shelfWidthFlat = 90f;
    public float shelfWidthSmallHills = 60f;
    public float shelfWidthLargeHills = 35f;
    public float shelfWidthMountainous = 20f;
    public float shelfWidthImpassable = 12f;
    public float shelfDepth = 3f;
    public float slopeGradient = 0.25f;
    public float maxDepth = 14f;

    /// <summary>
    /// Sea stacks (issue #60): within stackReach cells of the shore, rock at least this deep inside a resistant body
    /// (<see cref="RockStrata"/> intrusions, 0..1) across its whole 3x3 neighbourhood survives the waves that cut the softer
    /// rock round it, and stands up to stackHeight metres above the beach. Thin bodies (dikes) are cut through.
    /// </summary>
    public float stackReach = 12f;
    public float stackHardness = 0.6f;
    public float stackHeight = 6f;

    public float ShelfWidthFor(Hilliness hilliness)
    {
        switch (hilliness)
        {
            case Hilliness.Flat: return shelfWidthFlat;
            case Hilliness.SmallHills: return shelfWidthSmallHills;
            case Hilliness.LargeHills: return shelfWidthLargeHills;
            case Hilliness.Mountainous: return shelfWidthMountainous;
            case Hilliness.Impassable: return shelfWidthImpassable;
            default: return shelfWidthSmallHills;
        }
    }

    /// <summary>Metres the sea floor lies below the beach this many map cells from the shore.</summary>
    public float SeaDepth(float cellsOffshore, Hilliness hilliness)
    {
        float shelf = Mathf.Max(ShelfWidthFor(hilliness), 1f);
        return cellsOffshore <= shelf
            ? shelfDepth * cellsOffshore / shelf
            : Mathf.Min(maxDepth, shelfDepth + slopeGradient * (cellsOffshore - shelf));
    }

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

    // Typed delegates, not reflection calls: the noise is read for every map cell and every erosion-model cell. They call the
    // worker's own override (a bay or cove shapes its noise differently).
    private static readonly System.Func<TileMutatorWorker_Coast, IntVec3, float> NoiseAt =
        AccessTools.MethodDelegate<System.Func<TileMutatorWorker_Coast, IntVec3, float>>(AccessTools.Method(typeof(TileMutatorWorker_Coast), "GetNoiseValue"));
    private static readonly System.Func<TileMutatorWorker_Coast, float> SandLimit =
        AccessTools.MethodDelegate<System.Func<TileMutatorWorker_Coast, float>>(AccessTools.PropertyGetter(typeof(TileMutatorWorker_Coast), "MaxForSand"));

    // The coast noise over a map's cells, read once per map and shared by every caller (weakly held: no map is kept alive).
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Map, float[]> MapNoise = new();

    private static float[] NoiseOver(Map map, TileMutatorWorker_Coast coast)
    {
        if (MapNoise.TryGetValue(map, out float[] noise) && noise.Length == map.Area)
            return noise;
        noise = new float[map.Area];
        for (int i = 0; i < noise.Length; i++)
            noise[i] = NoiseAt(coast, new IntVec3(i % map.Size.x, 0, i / map.Size.x));
        MapNoise.AddOrUpdate(map, noise);
        return noise;
    }

    /// <summary>
    /// Cells the map's coast mutator will paint as water or beach (its coast noise below the sand limit), less land rivers
    /// built out over the sea (<see cref="Environment.MapComponent_Environment.PlannedDeltaLand"/>); null on tiles without one.
    /// </summary>
    public static bool[] SeaCells(Map map)
    {
        TileMutatorWorker_Coast coast = Find(map);
        if (coast == null)
            return null;
        float shore = SandLimit(coast);
        float[] noise = NoiseOver(map, coast);
        var environment = map.GetComponent<Environment.MapComponent_Environment>();
        bool[] built = environment?.PlannedDeltaLand, stacks = environment?.PlannedStacks;
        var sea = new bool[map.Area];
        for (int i = 0; i < sea.Length; i++)
            sea[i] = (built == null || !built[i]) && (stacks == null || !stacks[i]) && noise[i] < shore;
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
        float shore = SandLimit(coast);
        offshore = (x, z) => (shore - NoiseAt(coast, new IntVec3(Mathf.RoundToInt(x), 0, Mathf.RoundToInt(z)))) / Mathf.Max(shore, 0.01f);
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

    /// <summary>Whether a cell and all its neighbours lie in resistant rock: a body wide enough to stand against the waves.</summary>
    private static bool Core(float[] resistant, int i, int sizeX, int sizeZ, float hardness)
    {
        int x = i % sizeX, z = i / sizeX;
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, nz = z + dz;
                if (nx < 0 || nz < 0 || nx >= sizeX || nz >= sizeZ || resistant[nz * sizeX + nx] < hardness)
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Reshapes <paramref name="height"/> (metres above sea level) around the map's coast mutators; resistant rock near the
    /// shore (<paramref name="resistant"/>, 0..1 per cell) stays as sea stacks. Returns the number of sea cells.
    /// </summary>
    public static int Apply(Map map, float[] height, CoastlineExtension p, float[] resistant = null)
    {
        TileMutatorWorker_Coast coast = Find(map);
        if (coast == null)
            return 0;
        int sizeX = map.Size.x, n = height.Length;
        float shore = SandLimit(coast);
        float[] mapNoise = NoiseOver(map, coast);
        var noise = new float[n];
        var land = new System.Collections.Generic.List<float>();
        bool[] built = map.GetComponent<Environment.MapComponent_Environment>()?.PlannedDeltaLand;
        for (int i = 0; i < n; i++)
        {
            noise[i] = mapNoise[i];
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
        int sizeZ = n / sizeX;
        float[] fromLand = GridMath.DistanceTo(i => noise[i] >= shore, sizeX, sizeZ);
        Hilliness hilliness = map.TileInfo.hilliness;
        bool[] stacks = null;
        int sea = 0, stackCells = 0;
        for (int i = 0; i < n; i++)
        {
            if (noise[i] < shore && resistant != null && fromLand[i] <= p.stackReach && Core(resistant, i, sizeX, sizeZ, p.stackHardness))
            {
                // Waves cut the softer rock round a resistant body and leave it standing.
                stacks ??= new bool[n];
                stacks[i] = true;
                height[i] = beach + p.stackHeight * resistant[i];
                stackCells++;
            }
            else if (noise[i] < shore)
            {
                height[i] = beach - p.SeaDepth(fromLand[i], hilliness) - 0.3f;
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
        float[] toSea = GridMath.DistanceTo(i => noise[i] < shore, sizeX, sizeZ);
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
                    $"{low} ({(float)low / Mathf.Max(1, near):P0}) within 2 m above it; shelf {p.ShelfWidthFor(hilliness):F0} cells wide ({hilliness}); {stackCells} cells of sea stacks.");
        var plan = map.GetComponent<Environment.MapComponent_Environment>();
        if (plan != null)
            plan.PlannedStacks = stacks;
        return sea;
    }
}
