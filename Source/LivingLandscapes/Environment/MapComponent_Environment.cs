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
    /// The channels a world river takes across this map (main channel, side branches, cut-off loops), planned by the
    /// geology during generation. Not saved: only used while the river is placed.
    /// </summary>
    public Geology.RiverPlan PlannedRiver { get; set; }

    /// <summary>
    /// Rock beds planned by the geology: strength (0..1) and rock type beneath each cell. Strength is kept as the
    /// LL_RockStrength layer; the rock types steer vanilla's rock placement during generation. Not saved.
    /// </summary>
    public float[] PlannedRockStrength { get; set; }
    public ThingDef[] PlannedRock { get; set; }


    /// <summary>
    /// Ground the geology places itself (water, marsh, mud) per cell, from the ground rules; applied when vanilla picks
    /// terrain. Not saved.
    /// </summary>
    public Geology.GroundCover[] PlannedCover { get; set; }

    /// <summary>Cliffs planned by the geology during generation, spawned after vanilla places rock. Not saved.</summary>
    public Geology.CliffPlan PlannedCliffs { get; set; }

    /// <summary>Rock chunks to scatter below cliff faces once terrain and water are final. Not saved.</summary>
    public System.Collections.Generic.List<(IntVec3 cell, ThingDef chunk)> PlannedTalus { get; set; }

    public EnvironmentOverlay Overlay { get; }

    public bool HasLayer(EnvironmentLayerDef layer) => layers.ContainsKey(layer);

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
            if (layer.Worker.StageFor(map) != stage)
                continue;
            try
            {
                float[] values = layer.Worker.Compute(context);
                if (values == null)
                {
                    Log.Message($"[LivingLandscapes] {layer.defName}: not computed on {map}{(HasLayer(layer) ? ", keeping stored values" : "")}.");
                    continue;
                }
                context.Set(layer, values);
                layers[layer] = new EnvironmentGrid(values, layer.precision);
                Log.Message($"[LivingLandscapes] {layer.defName} computed for {map}: {Describe(values)}.");
            }
            catch (Exception e)
            {
                Log.Error($"[LivingLandscapes] Failed to compute {layer.defName} on {map}: {e}");
            }
        }
        Overlay.Notify_LayersChanged();
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
        Scribe_Collections.Look(ref layers, "layers", LookMode.Def, LookMode.Deep, ref keysWorking, ref valuesWorking);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            layers ??= new Dictionary<EnvironmentLayerDef, EnvironmentGrid>();
            // Layers whose Def was removed load with a null key.
            layers.RemoveAll(pair => pair.Key == null || pair.Value == null || pair.Value.Count != map.cellIndices.NumGridCells);
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
