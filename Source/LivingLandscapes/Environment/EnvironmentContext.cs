using System.Collections.Generic;
using Verse;

namespace LivingLandscapes.Environment;

/// <summary>What a layer worker gets while computing: the map, plus the values of the layers it depends on.</summary>
public class EnvironmentContext
{
    private readonly MapComponent_Environment environment;
    private readonly Dictionary<EnvironmentLayerDef, float[]> computed = new Dictionary<EnvironmentLayerDef, float[]>();

    public EnvironmentContext(Map map, MapComponent_Environment environment)
    {
        Map = map;
        this.environment = environment;
    }

    public Map Map { get; }

    public int SizeX => Map.Size.x;

    public int SizeZ => Map.Size.z;

    public int CellCount => Map.cellIndices.NumGridCells;

    /// <summary>Values of a layer computed earlier in this run, or the stored values if it wasn't recomputed.</summary>
    public float[] Get(EnvironmentLayerDef layer)
    {
        if (computed.TryGetValue(layer, out float[] values))
            return values;
        return environment.TryGetGrid(layer, out EnvironmentGrid grid) ? grid.ToArray() : null;
    }

    internal void Set(EnvironmentLayerDef layer, float[] values) => computed[layer] = values;
}
