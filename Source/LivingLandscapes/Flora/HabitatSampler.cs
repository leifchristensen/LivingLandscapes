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
    private readonly float[] moisture, soilDepth, slope, sun, shade;

    /// <param name="shadeOverride">Shade to use instead of the stored LL_Shade layer (the placement pass keeps its own).</param>
    public HabitatSampler(EnvironmentContext context, float[] shadeOverride = null)
    {
        Map = context.Map;
        moisture = context.Get(EnvironmentLayerDefOf.LL_Moisture);
        soilDepth = context.Get(EnvironmentLayerDefOf.LL_SoilDepth);
        slope = context.Get(EnvironmentLayerDefOf.LL_Slope);
        sun = context.Get(EnvironmentLayerDefOf.LL_Sun);
        shade = shadeOverride ?? context.Get(EnvironmentLayerDefOf.LL_Shade);
    }

    /// <summary>Fitness 0..1 of a species on one cell: 0 where it cannot live, 1 at its optimum on every factor.</summary>
    public float Fitness(ThingDef plant, PlantHabitat habitat, int index)
    {
        IntVec3 cell = Map.cellIndices.IndexToCell(index);
        TerrainDef terrain = cell.GetTerrain(Map);
        if (terrain.IsWater || terrain.fertility < plant.plant.fertilityMin || cell.GetEdifice(Map) != null)
            return 0f;

        float fitness = Response(habitat.wetness, moisture, index)
            * Response(habitat.soilDepth, soilDepth, index)
            * Response(habitat.slope, slope, index)
            * Response(habitat.sun, sun, index);
        if (habitat.fertility != null)
            fitness *= habitat.fertility.Response(Map.fertilityGrid.FertilityAt(cell));

        // Shade hurts plants that need light, by how little of it the species tolerates.
        float shaded = shade != null ? shade[index] : 0f;
        return fitness * Mathf.Lerp(1f - shaded, 1f, habitat.shadeTolerance);
    }

    private static float Response(HabitatCurve curve, float[] grid, int index)
    {
        return curve == null || grid == null ? 1f : curve.Response(grid[index]);
    }
}
