using LivingLandscapes.Environment;
using UnityEngine;

namespace LivingLandscapes.Geology;

/// <summary>
/// Where a map's water goes (P1 base levels, P2 routing, #98): the landform filled toward its outlets
/// (<see cref="Outlets.Fill"/>), each cell's spill level and the upslope area draining through it (multiple flow
/// directions). Built once per map from the final landform and shared by every layer that routes water, so wetness, soil,
/// lakes and the drainage area read one hydrology. Not saved: the layers built from it are.
/// </summary>
public class Drainage
{
    /// <summary>Slope (height units per cell) given to filled hollows so water still crosses them toward the spill point.</summary>
    public const float FillGradient = 1e-3f;

    /// <summary>The surface the water runs over (not changed).</summary>
    public float[] Ground;

    /// <summary>The ground filled to its spill levels plus <see cref="FillGradient"/>: every cell has a downhill path out.</summary>
    public float[] Filled;

    /// <summary>
    /// The level each cell's water stands at before it spills on: the rim of its depression, or the ground itself outside
    /// depressions. Spill minus ground is how deep a depression could hold water (whether it does is a water balance).
    /// </summary>
    public float[] Spill;

    /// <summary>Upslope area draining through each cell (cells, at least 1), world rivers' inflow included.</summary>
    public float[] Area;

    public int SizeX, SizeZ;

    private int[] receiver;

    /// <summary>Depth (height units) below its spill level from which a cell counts as in a hollow.</summary>
    private const float HollowDepth = 1e-4f;

    /// <summary>Each cell's steepest downhill neighbour on <see cref="Filled"/> (D8), or -1 where water leaves the map.</summary>
    public int[] Receiver => receiver ??= GridMath.Receivers(Filled, SizeX, SizeZ);

    /// <summary>
    /// Routes water over <paramref name="ground"/>: fills toward the base levels (every map edge drains where they are
    /// null, in Off mode), then accumulates area over the filled surface, adding <paramref name="inflow"/> (cells) where
    /// water enters from beyond the map.
    /// </summary>
    public static Drainage Build(float[] ground, float[] baseLevel, int sizeX, int sizeZ, float[] inflow = null)
    {
        var filled = (float[])ground.Clone();
        var spill = new float[ground.Length];
        if (baseLevel != null)
            Outlets.Fill(filled, sizeX, sizeZ, baseLevel, FillGradient, spill);
        else
            GridMath.FillDepressions(filled, sizeX, sizeZ, _ => false, FillGradient, spill: spill);
        return new Drainage
        {
            Ground = ground,
            Filled = filled,
            Spill = spill,
            Area = GridMath.FlowAccumulationMfd(filled, sizeX, sizeZ, inflow: inflow),
            SizeX = sizeX,
            SizeZ = sizeZ,
        };
    }

    /// <summary>
    /// Height of the drainage line each cell's water reaches (the base of height above nearest drainage, HAND), following
    /// <see cref="Receiver"/> until a cell whose area reaches <paramref name="channelArea"/> (log10 cells). In a closed
    /// hollow the water table stands at the hollow's water level (P23); until the water balance sets that level (#99) it
    /// is the hollow's floor, the level water collects at, so the filled surface's routing across the hollow plays no part. Where
    /// <paramref name="riverLevel"/> is set (a carved river) the line is the river's water surface, and a channel's whole
    /// width shares its largest area. <paramref name="lineArea"/> is the line's area (log10 cells) where the cell reaches it.
    /// </summary>
    public float[] LineHeight(float channelArea, float[] riverLevel, int[] channelOf, out float[] lineArea)
    {
        int n = Ground.Length;
        var logArea = new float[n];
        for (int i = 0; i < n; i++)
            logArea[i] = Mathf.Log10(Area[i]);
        var channelAreas = new System.Collections.Generic.Dictionary<int, float>();
        for (int i = 0; channelOf != null && i < n; i++)
        {
            if (channelOf[i] >= 0)
                channelAreas[channelOf[i]] = Mathf.Max(channelAreas.TryGetValue(channelOf[i], out float a) ? a : 0f, logArea[i]);
        }
        // Each hollow (a connected area below one spill level) and its floor: its lowest ground and that cell's area.
        GridMath.LabelRegions(i => Spill[i] - Ground[i] > HollowDepth, SizeX, SizeZ, out int[] region);
        var floors = new System.Collections.Generic.Dictionary<(int, float), int>();
        for (int i = 0; i < n; i++)
        {
            if (region[i] < 0)
                continue;
            var key = (region[i], Spill[i]);
            if (!floors.TryGetValue(key, out int lowest) || Ground[i] < Ground[lowest])
                floors[key] = i;
        }
        // Receivers lie lower on the filled surface, so visiting low to high resolves each cell's receiver first.
        var order = new int[n];
        var keys = (float[])Filled.Clone();
        for (int i = 0; i < n; i++)
            order[i] = i;
        System.Array.Sort(keys, order);
        var line = new float[n];
        lineArea = new float[n];
        int[] next = Receiver;
        foreach (int c in order)
        {
            if (riverLevel != null && !float.IsNaN(riverLevel[c]))
            {
                line[c] = riverLevel[c];
                lineArea[c] = channelOf != null && channelOf[c] >= 0 ? channelAreas[channelOf[c]] : logArea[c];
            }
            else if (region[c] >= 0)
            {
                int floor = floors[(region[c], Spill[c])];
                line[c] = Ground[floor];
                lineArea[c] = logArea[floor];
            }
            else if (logArea[c] >= channelArea || next[c] < 0)
            {
                line[c] = Ground[c];
                lineArea[c] = logArea[c];
            }
            else
            {
                line[c] = line[next[c]];
                lineArea[c] = lineArea[next[c]];
            }
        }
        return line;
    }

    /// <summary>
    /// The map's drainage of its final landform: the one kept on the environment component, built here the first time a
    /// layer asks (or again if the landform changed since).
    /// </summary>
    public static Drainage Of(EnvironmentContext context)
    {
        float[] landform = context.Get(EnvironmentLayerDefOf.LL_Landform);
        if (landform == null)
            return null;
        Drainage drainage = context.Environment.PlannedDrainage;
        if (drainage != null && drainage.Ground.Length == landform.Length && System.Linq.Enumerable.SequenceEqual(drainage.Ground, landform))
            return drainage;
        drainage = Build((float[])landform.Clone(), context.Environment.PlannedBaseLevels, context.SizeX, context.SizeZ,
            RiverInflow(context, context.Environment.PlannedRivers));
        context.Environment.PlannedDrainage = drainage;
        return drainage;
    }

    /// <summary>
    /// Channels entering from beyond the map bring the water of all the land upstream: the drainage area the erosion
    /// model gathered for them enters where each crosses into the map.
    /// </summary>
    private static float[] RiverInflow(EnvironmentContext context, RiverSystem rivers)
    {
        if (rivers == null)
            return null;
        var inflow = new float[context.SizeX * context.SizeZ];
        foreach (RiverPlan channel in rivers.Channels)
        {
            int entry = channel.Main.FindIndex(v => v.x >= 0f && v.y >= 0f && v.x < context.SizeX && v.y < context.SizeZ);
            if (entry >= 0 && channel.EntryArea > 0f)
                inflow[RiverGrid.Index(channel.Main[entry], context.SizeX, context.SizeZ)] += channel.EntryArea;
        }
        return inflow;
    }
}
