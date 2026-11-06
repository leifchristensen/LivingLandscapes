using System.Collections.Generic;
using LivingLandscapes.Environment;
using LivingLandscapes.Layers;
using UnityEngine;
using Verse;

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
    /// depressions. Spill minus ground is how deep a depression could hold water (whether it does is a water balance), a
    /// closed basin's too: its sink is not an outlet here.
    /// </summary>
    public float[] Spill;

    /// <summary>Upslope area draining through each cell (cells, at least 1), world rivers' inflow included.</summary>
    public float[] Area;

    public int SizeX, SizeZ;

    /// <summary>
    /// The map's balance lake area per catchment area (<see cref="WaterBalance.LakeShare"/>, P23); infinite where rain
    /// outweighs evaporation and every hollow fills to its spill level.
    /// </summary>
    public float LakeShare = float.PositiveInfinity;

    /// <summary>
    /// The balance's lake share for a floor seeping this much (S, mm/yr; <see cref="WaterBalance.LakeShare(Map, out float, float)"/>).
    /// Null: every hollow takes <see cref="LakeShare"/>.
    /// </summary>
    public System.Func<float, float> ShareFor;

    /// <summary>The share of a hollow fed by groundwater, limited by its catchment's recharge (<see cref="WaterBalance.FedLakeShare"/>).</summary>
    public float FedShare = float.NaN;

    /// <summary>
    /// Effective seepage of each cell's floor (S, mm/yr, before the water-table cap; <see cref="FloorSeepageGrid"/>) and its
    /// floor class (<see cref="FloorSeepageRules"/>). Null until the floor layers exist: hollows then seep nothing.
    /// </summary>
    public float[] FloorSeepage;
    public int[] FloorClass;

    /// <summary>
    /// Where each hollow's water stands (height units; NaN outside hollows), from <see cref="LineHeight"/>: the water
    /// balance with its floor's seepage, and at least the water table where the floor lies below it (#149). The lake layer
    /// paints its lakes at these levels.
    /// </summary>
    public float[] HollowLevel;

    /// <summary>One closed hollow's balance, for the test summary (#149).</summary>
    public struct HollowRecord
    {
        public int Cells, FloorClass;
        public float Floor, Spill, Level, Seepage, WaterTableDepth;
    }

    /// <summary>Every hollow <see cref="LineHeight"/> balanced, deep and large enough to hold a lake.</summary>
    public List<HollowRecord> Hollows = new List<HollowRecord>();

    private int[] receiver;
    private (float channelArea, float[] riverLevel, float[] height, float[] area, bool[] water)? lines;

    /// <summary>Depth (height units) below its spill level from which a cell counts as in a hollow.</summary>
    private const float HollowDepth = 1e-4f;

    /// <summary>Each cell's steepest downhill neighbour on <see cref="Filled"/> (D8), or -1 where water leaves the map.</summary>
    public int[] Receiver => receiver ??= GridMath.Receivers(Filled, SizeX, SizeZ);

    /// <summary>
    /// Routes water over <paramref name="ground"/>: fills toward the base levels (every map edge drains where they are
    /// null, in Off mode), then accumulates area over the filled surface, adding <paramref name="inflow"/> (cells) where
    /// water enters from beyond the map.
    /// </summary>
    public static Drainage Build(float[] ground, float[] baseLevel, int sizeX, int sizeZ, float[] inflow = null, bool[] sink = null)
    {
        var filled = (float[])ground.Clone();
        var spill = new float[ground.Length];
        if (baseLevel != null)
            Outlets.Fill(filled, sizeX, sizeZ, baseLevel, FillGradient, spill);
        else
            GridMath.FillDepressions(filled, sizeX, sizeZ, _ => false, FillGradient, spill: spill);
        if (baseLevel != null && sink != null && System.Array.IndexOf(sink, true) >= 0)
        {
            // A closed basin's water ends in its sink, but the basin is as deep as its rim.
            var open = (float[])baseLevel.Clone();
            for (int i = 0; i < open.Length; i++)
            {
                if (sink[i])
                    open[i] = float.NaN;
            }
            Outlets.Fill((float[])ground.Clone(), sizeX, sizeZ, open, 0f, spill);
        }
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
    /// hollow the water table stands at the hollow's water level, set by the water balance as the lake layer sets it (P23,
    /// #122): the spill level where the hollow fills, its floor where it is dry. The hollow's line area is the largest within it. Where
    /// <paramref name="riverLevel"/> is set (a carved river) the line is the river's water surface, and a channel's whole
    /// width shares its largest area. <paramref name="lineArea"/> is the line's area (log10 cells) where the cell reaches it.
    /// <paramref name="lineWater"/> is whether water stands on that line (a river, or a hollow the lake layer fills); a
    /// dry wash or a hollow too shallow to hold a lake keeps its water table below the ground.
    /// </summary>
    public float[] LineHeight(float channelArea, float[] riverLevel, int[] channelOf, out float[] lineArea, out bool[] lineWater)
    {
        // Wetness and LL_HeightAboveDrainage ask for the same lines.
        if (lines != null && lines.Value.channelArea == channelArea && lines.Value.riverLevel == riverLevel)
        {
            lineArea = lines.Value.area;
            lineWater = lines.Value.water;
            return lines.Value.height;
        }
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
        // Each hollow (a connected area below one spill level) gathers its whole catchment: its largest area.
        GridMath.LabelRegions(i => Spill[i] - Ground[i] > HollowDepth, SizeX, SizeZ, out int[] region);
        var hollowArea = new System.Collections.Generic.Dictionary<(int, float), float>();
        var hollowCells = new System.Collections.Generic.Dictionary<(int, float), System.Collections.Generic.List<int>>();
        for (int i = 0; i < n; i++)
        {
            if (region[i] < 0)
                continue;
            var key = (region[i], Spill[i]);
            hollowArea[key] = Mathf.Max(hollowArea.TryGetValue(key, out float a) ? a : 0f, logArea[i]);
            if (!hollowCells.TryGetValue(key, out var cells))
                hollowCells[key] = cells = new System.Collections.Generic.List<int>();
            cells.Add(i);
        }
        // Each hollow's outlet: the cell its water leaves by (its receiver lies outside the hollow); none for a sink.
        int[] next = Receiver;
        var outlet = new System.Collections.Generic.Dictionary<(int, float), int>();
        foreach (var hollow in hollowCells)
        {
            outlet[hollow.Key] = -1;
            foreach (int c in hollow.Value)
            {
                int r = next[c];
                if (r >= 0 && (region[r] != region[c] || Spill[r] != Spill[c]))
                {
                    outlet[hollow.Key] = r;
                    break;
                }
            }
        }
        var lake = EnvironmentLayerDefOf.LL_SurfaceWater.GetModExtension<SurfaceWaterExtension>() ?? new SurfaceWaterExtension();
        var waterLevel = new System.Collections.Generic.Dictionary<(int, float), float>();
        var holdsLake = new System.Collections.Generic.Dictionary<(int, float), bool>();
        HollowLevel = new float[n];
        for (int i = 0; i < n; i++)
            HollowLevel[i] = float.NaN;
        Hollows = new List<HollowRecord>();
        // Receivers lie lower on the filled surface, so visiting low to high resolves each cell's receiver first.
        var order = new int[n];
        var keys = (float[])Filled.Clone();
        for (int i = 0; i < n; i++)
            order[i] = i;
        System.Array.Sort(keys, order);
        var line = new float[n];
        var visited = new bool[n];
        lineArea = new float[n];
        bool[] water = lineWater = new bool[n];
        foreach (int c in order)
        {
            if (region[c] >= 0 && !waterLevel.ContainsKey((region[c], Spill[c])))
                BalanceHollow((region[c], Spill[c]));
            visited[c] = true;
            if (riverLevel != null && !float.IsNaN(riverLevel[c]))
            {
                line[c] = riverLevel[c];
                lineArea[c] = channelOf != null && channelOf[c] >= 0 ? channelAreas[channelOf[c]] : logArea[c];
                lineWater[c] = true;
            }
            else if (region[c] >= 0)
            {
                line[c] = waterLevel[(region[c], Spill[c])];
                lineArea[c] = hollowArea[(region[c], Spill[c])];
                lineWater[c] = holdsLake[(region[c], Spill[c])];
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
                lineWater[c] = lineWater[next[c]];
            }
        }
        lines = (channelArea, riverLevel, line, lineArea, lineWater);
        return line;

        // A hollow's water (P23 extended, #149), once the drainage below its outlet is known. Where water stands on the
        // drainage line its outlet spills into (a river, a lake), that water is the water table there: a gaining stream.
        // A dry line (a wash, a seep) is no water table: in dry ground it lies deeper than the floor seeps to, so the floor
        // drains at its full S and nothing feeds it from below. A sink is the low point its groundwater flows to: the water
        // table stands at its floor. A floor below the water table is fed from below: water stands at least up to the water
        // table, nothing seeps away, and the groundwater that flows in is what its catchment's recharge supplies (water is
        // conserved: in a dry climate there is none, and the runoff balance alone sets the level). A floor above it loses
        // its seepage S x cap(depth) on top of evaporation, and holds water only by the balance of what its catchment
        // brings in.
        void BalanceHollow((int, float) key)
        {
            List<int> cells = hollowCells[key];
            cells.Sort((a, b) => Ground[a].CompareTo(Ground[b]));
            float spill = key.Item2, floor = Ground[cells[0]];
            int r = outlet[key];
            bool gaining = r >= 0 && visited[r] && water[r];
            float waterTable = r < 0 ? floor : gaining ? Mathf.Min(spill, line[r]) : float.NegativeInfinity;
            float depth = floor - waterTable;
            FloorSeepageRules rules = FloorSeepageDef.Current.Rules;
            float seepage = 0f;
            int floorClass = FloorSeepageRules.ClayHardpan;
            if (FloorSeepage != null)
            {
                var votes = new int[rules.Seepage.Length];
                foreach (int c in cells)
                {
                    seepage += FloorSeepage[c];
                    votes[FloorClass[c]]++;
                }
                seepage = seepage / cells.Count * rules.Cap(depth);
                for (int k = 1; k < votes.Length; k++)
                {
                    if (votes[k] > votes[floorClass])
                        floorClass = k;
                }
            }
            float share = ShareFor != null && seepage > 0f ? ShareFor(seepage) : LakeShare;
            float catchment = Mathf.Pow(10f, hollowArea[key]);
            float level = WaterBalance.LakeLevel(cells, Ground, spill, catchment, share);
            // Groundwater raises the water only up to the water table, and only as far as recharge pays for.
            if (depth < 0f && !float.IsNaN(FedShare))
                level = Mathf.Max(level, Mathf.Min(waterTable, WaterBalance.LakeLevel(cells, Ground, spill, catchment, FedShare)));
            waterLevel[key] = level;
            bool deepAndLarge = spill - floor >= lake.minLakeDepth && cells.Count >= lake.minLakeCells;
            holdsLake[key] = deepAndLarge && level - floor >= lake.minLakeDepth;
            foreach (int c in cells)
                HollowLevel[c] = level;
            if (deepAndLarge)
                Hollows.Add(new HollowRecord { Cells = cells.Count, FloorClass = floorClass, Floor = floor, Spill = spill, Level = level,
                                               Seepage = seepage, WaterTableDepth = depth });
        }
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
            RiverInflow(context, context.Environment.PlannedRivers), context.Environment.PlannedSinks);
        drainage.LakeShare = WaterBalance.LakeShare(context.Map, out _);
        Map map = context.Map;
        drainage.ShareFor = seepage => WaterBalance.LakeShare(map, out _, seepage);
        drainage.FedShare = WaterBalance.FedLakeShare(map);
        context.Environment.PlannedDrainage = drainage;
        return drainage;
    }

    /// <summary>
    /// <see cref="Of"/>, with each cell's floor seepage read from the floor layers (alluvium, grain size, soil, rock), for
    /// the layers that balance the hollows (wetness, height above drainage, lakes). Those layers depend on the floor layers.
    /// </summary>
    public static Drainage WithFloor(EnvironmentContext context)
    {
        Drainage drainage = Of(context);
        if (drainage != null && drainage.FloorSeepage == null)
            FloorSeepageGrid.Fill(context, drainage);
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
