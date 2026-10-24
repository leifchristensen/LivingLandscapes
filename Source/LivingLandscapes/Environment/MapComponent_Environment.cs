using System;
using System.Collections.Generic;
using LivingLandscapes.Overlay;
using Verse;

namespace LivingLandscapes.Environment;

/// <summary>
/// Holds every environment layer of one map, computed once at generation and saved with the map.
/// A layer that is missing (old save, generation step skipped) reads as absent, so callers fall back to vanilla.
/// </summary>
public class MapComponent_Environment : MapComponent
{
    private Dictionary<EnvironmentLayerDef, EnvironmentGrid> layers = new Dictionary<EnvironmentLayerDef, EnvironmentGrid>();

    private List<EnvironmentLayerDef> keysWorking;
    private List<EnvironmentGrid> valuesWorking;

    public MapComponent_Environment(Map map) : base(map)
    {
        Overlay = new EnvironmentOverlay(this);
    }

    public Map Map => map;

    /// <summary>
    /// The map's channel network (the world river and the map's own creeks and rivers, with side branches and cut-off
    /// loops) and the water level and flow the carving left, planned by the geology during generation. Not saved:
    /// only used while the map is generated.
    /// </summary>
    public Geology.RiverSystem PlannedRivers { get; set; }

    /// <summary>
    /// Rock beds planned by the geology: strength (0..1) and rock type beneath each cell. Strength is kept as the
    /// LL_RockStrength layer; the rock types steer vanilla's rock placement during generation. Not saved.
    /// </summary>
    public float[] PlannedRockStrength { get; set; }

    /// <summary>
    /// River valleys the sea drowned as it rose (P1 base level, the erosion model's sea-level history): metres below the sea,
    /// 0 where none. The coast makes them sea. Generation only.
    /// </summary>
    public float[] PlannedDrowned { get; set; }

    /// <summary>
    /// Sea stacks (issue #60): resistant rock near the shore that waves left standing above the sea. The coast mutator leaves
    /// it land; it is bare rock, not deep soil.
    /// </summary>
    public bool[] PlannedStacks { get; set; }

    /// <summary>The sea's surface (the beach's height, <see cref="Geology.Coastline"/>); NaN without a coast. Not saved.</summary>
    public float PlannedSeaLevel { get; set; } = float.NaN;

    /// <summary>Sea shallower than this (metres) is shallow water (<see cref="Geology.CoastlineExtension.shallowWaterDepth"/>). Not saved.</summary>
    public float PlannedShallowDepth { get; set; } = float.NaN;

    /// <summary>Hard beds at or below each cell's surface (<see cref="Geology.RockStrata.HardBedsBelow"/>): the terrace stages follow it. Not saved.</summary>
    public int[] PlannedRockStage { get; set; }
    public ThingDef[] PlannedRock { get; set; }


    /// <summary>
    /// Ground the geology places itself (water, marsh, mud) per cell, from the ground rules; applied when vanilla picks
    /// terrain. Not saved.
    /// </summary>
    public Geology.GroundCover[] PlannedCover { get; set; }

    /// <summary>The terrace plan (stages and steepness) LL_Height was built from; the cliffs are placed from it. Not saved.</summary>
    public Geology.TerracePlan PlannedTerraces { get; set; }

    /// <summary>Cliffs planned by the geology during generation, spawned after vanilla places rock. Not saved.</summary>
    public Geology.CliffPlan PlannedCliffs { get; set; }

    /// <summary>Rock chunks to scatter below cliff faces once terrain and water are final. Not saved.</summary>
    public System.Collections.Generic.List<(IntVec3 cell, ThingDef chunk)> PlannedTalus { get; set; }

    /// <summary>
    /// Where the map's best settlement ground lies (<see cref="Gameplay.SettlementSiting"/>), chosen before rocks, roads and
    /// resources so they can use it; the start spot is picked near it. Invalid when siting is off. Not saved.
    /// </summary>
    public IntVec3 SettlementAnchor { get; set; } = IntVec3.Invalid;

    /// <summary>The colony's start spot as map generation left it (vanilla's or the one moved to the anchor); for tests and exports. Not saved.</summary>
    public IntVec3 StartSpot { get; set; } = IntVec3.Invalid;

    /// <summary>LL_Settlement smoothed over the neighbourhood of a camp, per cell. Not saved.</summary>
    public float[] SettlementSmoothed { get; set; }

    public EnvironmentOverlay Overlay { get; }

    public bool HasLayer(EnvironmentLayerDef layer) => layers.ContainsKey(layer);

    private float[] groundHeight;

    /// <summary>
    /// The natural ground (LL_Landform, metres) smoothed over <see cref="Gameplay.SlopeMovementExtension.heightSmoothing"/> cells,
    /// kept for per-step lookups (slope movement); null without the layer. The natural surface, not the terraced one (a terrace
    /// step is a cliff, and cliffs cost their own time), and smoothed so the hummocks of micro-relief do not slow a walk.
    /// </summary>
    public float[] GroundHeight
    {
        get
        {
            if (groundHeight == null && layers.TryGetValue(EnvironmentLayerDefOf.LL_Landform, out EnvironmentGrid grid))
            {
                float sigma = Gameplay.SlopeMovement.Tuning.heightSmoothing;
                groundHeight = sigma > 0f ? Geology.GridMath.Blur(grid.ToArray(), map.Size.x, map.Size.z, sigma) : grid.ToArray();
            }
            return groundHeight;
        }
    }

    public bool TryGetGrid(EnvironmentLayerDef layer, out EnvironmentGrid grid) => layers.TryGetValue(layer, out grid);

    /// <summary>Value of a layer at a cell, or <paramref name="fallback"/> when the map doesn't have that layer.</summary>
    public float ValueAt(EnvironmentLayerDef layer, IntVec3 cell, float fallback = 0f)
    {
        return layers.TryGetValue(layer, out EnvironmentGrid grid) ? grid[map.cellIndices.CellToIndex(cell)] : fallback;
    }

    /// <summary>
    /// Computes, in dependency order, every layer whose worker runs at <paramref name="stage"/>.
    /// A layer that fails or returns null keeps its stored values.
    /// </summary>
    public void ComputeLayers(GenerationStage stage)
    {
        var context = new EnvironmentContext(map, this);
        foreach (EnvironmentLayerDef layer in EnvironmentLayerOrder.Sorted)
        {
            if (layer.onDemand || layer.Worker.StageFor(map) != stage)
                continue;
            Compute(layer, context);
        }
        Overlay.Notify_LayersChanged();
    }

    /// <summary>
    /// Computes an on-demand dev layer (<see cref="EnvironmentLayerDef.onDemand"/>) now, from the layers the map holds and
    /// its current state. Returns false if it could not be computed (its inputs are missing).
    /// </summary>
    public bool ComputeOnDemand(EnvironmentLayerDef layer)
    {
        return Compute(layer, new EnvironmentContext(map, this));
    }

    private bool Compute(EnvironmentLayerDef layer, EnvironmentContext context)
    {
        try
        {
            float[] values = layer.Worker.Compute(context);
            if (values == null)
            {
                Log.Message($"[LivingLandscapes] {layer.defName}: not computed on {map}{(HasLayer(layer) ? ", keeping stored values" : "")}.");
                return false;
            }
            context.Set(layer, values);
            layers[layer] = new EnvironmentGrid(values, layer.precision);
            if (layer == EnvironmentLayerDefOf.LL_Landform)
                groundHeight = null;
            Log.Message($"[LivingLandscapes] {layer.defName} computed for {map}: {Describe(values)}.");
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"[LivingLandscapes] Failed to compute {layer.defName} on {map}: {e}");
            return false;
        }
    }

    private static string Describe(float[] values)
    {
        float min = float.MaxValue, max = float.MinValue, sum = 0f;
        foreach (float v in values)
        {
            min = Math.Min(min, v);
            max = Math.Max(max, v);
            sum += v;
        }
        return $"min {min:F2} / mean {sum / values.Length:F2} / max {max:F2}";
    }

    public override void ExposeData()
    {
        // On-demand dev layers are views of the moment, not map data: they are left out of the save.
        Dictionary<EnvironmentLayerDef, EnvironmentGrid> all = layers;
        if (Scribe.mode == LoadSaveMode.Saving)
        {
            layers = new Dictionary<EnvironmentLayerDef, EnvironmentGrid>();
            foreach (KeyValuePair<EnvironmentLayerDef, EnvironmentGrid> pair in all)
            {
                if (!pair.Key.onDemand)
                    layers[pair.Key] = pair.Value;
            }
        }
        Scribe_Collections.Look(ref layers, "layers", LookMode.Def, LookMode.Deep, ref keysWorking, ref valuesWorking);
        if (Scribe.mode == LoadSaveMode.Saving)
            layers = all;
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            layers ??= new Dictionary<EnvironmentLayerDef, EnvironmentGrid>();
            // Layers whose Def was removed load with a null key.
            layers.RemoveAll(pair => pair.Key == null || pair.Key.onDemand || pair.Value == null || pair.Value.Count != map.cellIndices.NumGridCells);
        }
    }

    public override void MapComponentUpdate()
    {
        if (map != Find.CurrentMap)
            return;
        Overlay.Update();
        DevTools.TestRun.Tick();
    }

    public override void MapComponentOnGUI()
    {
        if (map == Find.CurrentMap)
            Overlay.OnGUI();
    }
}
