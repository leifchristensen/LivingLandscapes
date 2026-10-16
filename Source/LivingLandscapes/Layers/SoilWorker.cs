using LivingLandscapes.Environment;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Layers;

/// <summary>Tuning for <see cref="SoilWorker"/>, set on the layer Def in XML.</summary>
public class SoilExtension : DefModExtension
{
    /// <summary>Vanilla fertility mapped to a soil score of 1.</summary>
    public float richFertility = 1.2f;
}

/// <summary>0..1 soil quality from vanilla terrain fertility. MVP model; soil depth and rockiness come later.</summary>
public class SoilWorker : EnvironmentLayerWorker
{
    public override float[] Compute(EnvironmentContext context)
    {
        var p = def.GetModExtension<SoilExtension>() ?? new SoilExtension();
        Map map = context.Map;
        var soil = new float[context.CellCount];
        foreach (IntVec3 c in map.AllCells)
            soil[map.cellIndices.CellToIndex(c)] = Mathf.Clamp01(map.fertilityGrid.FertilityAt(c) / p.richFertility);
        return soil;
    }
}
