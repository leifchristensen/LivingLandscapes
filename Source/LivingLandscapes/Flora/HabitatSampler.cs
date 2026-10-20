using LivingLandscapes.Environment;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Flora;

/// <summary>
/// The environment as plants see it, read from the layer grids once so fitness can be evaluated for any number of
/// species and cells.
/// </summary>
public class HabitatSampler
{
    public readonly Map Map;
    private readonly float[] moisture, soilDepth, slope, sun;

    /// <summary>A sampler over the layer arrays of the running map (<see cref="MapComponent_Ecology"/>); shade is kept live there and read separately.</summary>
    public HabitatSampler(Map map, float[] moisture, float[] soilDepth, float[] slope, float[] sun)
    {
        Map = map;
        this.moisture = moisture;
        this.soilDepth = soilDepth;
        this.slope = slope;
        this.sun = sun;
    }

    /// <summary>
    /// The site's fitness 0..1 for a species from the ground alone: wetness, soil depth, slope, sun and fertility. No
    /// terrain, edifice or shade: those change as the colony builds and plants grow, so callers read them when asked.
    /// </summary>
    public float Site(PlantHabitat habitat, int index)
    {
        float fitness = Response(habitat.wetness, moisture, index)
            * Response(habitat.soilDepth, soilDepth, index)
            * Response(habitat.slope, slope, index)
            * Response(habitat.sun, sun, index);
        if (habitat.fertility != null)
            fitness *= habitat.fertility.Response(Map.fertilityGrid.FertilityAt(Map.cellIndices.IndexToCell(index)));
        return fitness;
    }

    private static float Response(HabitatCurve curve, float[] grid, int index)
    {
        return curve == null || grid == null ? 1f : curve.Response(grid[index]);
    }
}
