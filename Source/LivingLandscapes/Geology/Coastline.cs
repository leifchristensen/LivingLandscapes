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
    /// Sea shallower than this (metres) is painted as shallow water, deeper as deep water, from the sea floor itself (shelf,
    /// mouth bars, delta fronts, scoured channels) rather than vanilla's coast noise.
    /// </summary>
    public float shallowWaterDepth = 1.2f;

    /// <summary>
    /// Sea stacks (issue #60): within stackReach cells of the shore, rock at least this deep inside a resistant body
    /// (<see cref="RockStrata"/> intrusions, 0..1) across its whole 3x3 neighbourhood survives the waves that cut the softer
    /// rock round it, and stands up to stackHeight metres above the beach. Thin bodies (dikes) are cut through.
    /// </summary>
    public float stackReach = 12f;
    public float stackHardness = 0.6f;
    public float stackHeight = 6f;

    /// <summary>
    /// The coastal plain is as wide as the sea's shelf times this: one passive-margin feature (low coasts: wide shelf and wide plain;
    /// steep coasts: narrow both). Uplift fades to zero toward the sea over it (<see cref="CoastMath.PlainUplift"/>).
    /// </summary>
    public float plainShelfRatio = 0.6f;

    /// <summary>Lowest metres per cell the ground climbs from the sea across the coastal plain: a graded plain keeps a lowland river's slope.</summary>
    public float plainGradient = 0.006f;

    /// <summary>Width of the coastal plain in map cells.</summary>
    public float PlainWidthFor(Hilliness hilliness) => plainShelfRatio * ShelfWidthFor(hilliness);

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
}

/// <summary>
/// The sea as the lowest ground (Docs/GAMEPLAY-PLAN.md, issue #43). Vanilla's coast mutator paints ocean where its own coast
/// noise falls below a threshold, long after the landform exists, so the geology had a sea that was no lower than the shore
/// (and rivers and drainage ended at the map edge, not in the sea). Here the erosion model takes the same noise as its sea
/// (<see cref="TryOffshore"/>, the base level of P1), so the land beside the shore is graded to the water by the model itself and
/// <see cref="Apply"/> writes only the sea floor (bathymetry, issue #77): where the mutator will paint water the ground falls
/// below the sea level, deeper with distance from the shore. The beach band between the water and the sand limit is land of the
/// model, so a river or creek crosses it to the sea. Applies to every tile mutator that paints a coast with
/// <c>TileMutatorWorker_Coast</c> (coast, bay, cove, peninsula...), reading its own noise and thresholds.
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
    private static readonly System.Func<TileMutatorWorker_Coast, float> WaterLimit =
        AccessTools.MethodDelegate<System.Func<TileMutatorWorker_Coast, float>>(AccessTools.PropertyGetter(typeof(TileMutatorWorker_Coast), "MaxForShallowWater"));

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
    /// Cells the map's coast mutator will paint as water or beach (its coast noise below the sand limit), less sea stacks; null on
    /// tiles without one.
    /// </summary>
    public static bool[] SeaCells(Map map)
    {
        return CoastCells(map, beach: true);
    }

    /// <summary>Cells the map's coast mutator paints as water (its coast noise below the water limit): the sea of the model; null without a coast.</summary>
    public static bool[] WaterCells(Map map)
    {
        return CoastCells(map, beach: false);
    }

    /// <summary>The beach: cells the coast mutator paints as sand, between its water and the land (loose sand); null without a coast.</summary>
    public static bool[] BeachCells(Map map)
    {
        bool[] coast = CoastCells(map, beach: true), water = CoastCells(map, beach: false);
        if (coast == null)
            return null;
        for (int i = 0; i < coast.Length; i++)
            coast[i] &= !water[i];
        return coast;
    }

    private static bool[] CoastCells(Map map, bool beach)
    {
        TileMutatorWorker_Coast coast = Find(map);
        if (coast == null)
            return null;
        float shore = beach ? SandLimit(coast) : WaterLimit(coast);
        float[] noise = NoiseOver(map, coast);
        var environment = map.GetComponent<Environment.MapComponent_Environment>();
        bool[] stacks = environment?.PlannedStacks;
        var sea = new bool[map.Area];
        for (int i = 0; i < sea.Length; i++)
            sea[i] = (stacks == null || !stacks[i]) && noise[i] < shore;
        return sea;
    }

    /// <summary>
    /// How far offshore each point lies (0 at the waterline, 1 at the coast noise's deepest), for any map coordinates, including
    /// beyond the map's edges (the erosion model's region); negative on land, the beach band (water limit to sand limit) included:
    /// the sea is the water, and the beach is land the water has to cross. False on tiles without a coast.
    /// </summary>
    public static bool TryOffshore(Map map, out System.Func<float, float, float> offshore)
    {
        TileMutatorWorker_Coast coast = Find(map);
        offshore = null;
        if (coast == null)
            return false;
        float shore = WaterLimit(coast);
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
    /// Writes the sea floor of the map's coast mutators into <paramref name="height"/> (metres): the shelf profile below
    /// <paramref name="seaLevel"/>, the model's base level (see <see cref="CoastMath"/>). Land is not touched: the erosion model
    /// grades it to that level. Resistant rock near the shore (<paramref name="resistant"/>, 0..1 per cell) stays standing in
    /// the water as sea stacks (#60 moves them into the model). Returns the number of sea cells.
    /// </summary>
    public static int Apply(Map map, float[] height, float seaLevel, CoastlineExtension p, float[] resistant = null)
    {
        TileMutatorWorker_Coast coast = Find(map);
        if (coast == null)
            return 0;
        int sizeX = map.Size.x, n = height.Length, sizeZ = n / sizeX;
        float[] noise = NoiseOver(map, coast);
        float waterline = WaterLimit(coast);
        var water = new bool[n];
        bool any = false;
        for (int i = 0; i < n; i++)
            any |= water[i] = noise[i] < waterline;
        if (!any)
            return 0;
        float[] fromLand = GridMath.DistanceTo(i => !water[i], sizeX, sizeZ);
        Hilliness hilliness = map.TileInfo.hilliness;
        bool[] stacks = null;
        int stackCells = 0;
        System.Func<int, bool> isStack = null;
        if (resistant != null)
        {
            // Waves cut the softer rock round a resistant body and leave it standing.
            stacks = new bool[n];
            for (int i = 0; i < n; i++)
            {
                if (water[i] && fromLand[i] <= p.stackReach && Core(resistant, i, sizeX, sizeZ, p.stackHardness))
                {
                    stacks[i] = true;
                    stackCells++;
                }
            }
            if (stackCells == 0)
                stacks = null;
            isStack = i => stacks != null && stacks[i];
        }
        int sea = CoastMath.ShapeSeaFloor(height, water, fromLand, seaLevel, cells => p.SeaDepth(cells, hilliness), isStack, resistant, p.stackHeight);
        // Evaluation: how much land beside the sea the model left below its base level (should be none but micro-relief).
        float[] toSea = GridMath.DistanceTo(i => water[i], sizeX, sizeZ);
        int near = 0, below = 0;
        float deepest = 0f;
        for (int i = 0; i < n; i++)
        {
            if (water[i] || toSea[i] > 30f)
                continue;
            near++;
            if (height[i] < seaLevel)
            {
                below++;
                deepest = Mathf.Max(deepest, seaLevel - height[i]);
            }
        }
        Log.Message($"[LivingLandscapes] Coast setting: of {near} land cells within 30 of the sea, {below} lie below its level (deepest {deepest:F2} m); " +
                    $"shelf {p.ShelfWidthFor(hilliness):F0} cells wide ({hilliness}); {stackCells} cells of sea stacks.");
        var plan = map.GetComponent<Environment.MapComponent_Environment>();
        if (plan != null)
        {
            plan.PlannedStacks = stacks;
            plan.PlannedSeaLevel = seaLevel;
            plan.PlannedShallowDepth = p.shallowWaterDepth;
        }
        return sea;
    }
}
