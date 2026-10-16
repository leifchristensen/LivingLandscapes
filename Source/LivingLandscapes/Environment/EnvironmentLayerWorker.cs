using Verse;

namespace LivingLandscapes.Environment;

/// <summary>Computes the values of one <see cref="EnvironmentLayerDef"/> for a map.</summary>
public abstract class EnvironmentLayerWorker
{
    public EnvironmentLayerDef def;

    /// <summary>When this layer is computed on this map. Layers that drive terrain placement return Elevation.</summary>
    public virtual GenerationStage StageFor(Map map) => GenerationStage.Plants;

    /// <summary>
    /// Returns one value per cell, indexed like <c>map.cellIndices</c>, or null when the layer can't be computed
    /// now (e.g. its source data only exists during map generation). Null keeps any previously stored values.
    /// </summary>
    public abstract float[] Compute(EnvironmentContext context);
}
