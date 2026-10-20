using System;
using LivingLandscapes.Environment;
using LivingLandscapes.Flora;
using RimWorld;
using Verse;

namespace LivingLandscapes.Layers;

/// <summary>
/// Dev view (on demand): how well one species would do on each cell now (0 cannot live, 1 optimum), as the plant ecology
/// scores it for the spawner (<see cref="MapComponent_Ecology.Fitness"/>: ground and live shade). The species is chosen
/// with the "Plant fitness: choose species" debug action; with none chosen it shows the best of the biome's wild plants.
/// Water, buildings and ground too poor for the species read 0. Not computed where the ecology is off.
/// </summary>
public class FitnessWorker : EnvironmentLayerWorker
{
    /// <summary>The species the dev view shows, or null for the best of the biome's plants.</summary>
    public static ThingDef Selected;

    public override float[] Compute(EnvironmentContext context)
    {
        Map map = context.Map;
        var ecology = map.GetComponent<MapComponent_Ecology>();
        if (ecology == null || !ecology.Ready)
            return null;
        var fitness = new float[context.CellCount];
        foreach (ThingDef plant in Selected != null ? new[] { Selected } : (System.Collections.Generic.IEnumerable<ThingDef>)map.Biome.AllWildPlants)
        {
            for (int i = 0; i < fitness.Length; i++)
            {
                IntVec3 cell = map.cellIndices.IndexToCell(i);
                TerrainDef terrain = cell.GetTerrain(map);
                if (terrain.IsWater || terrain.fertility < plant.plant.fertilityMin || cell.GetEdifice(map) != null)
                    continue;
                fitness[i] = Math.Max(fitness[i], ecology.Fitness(plant, i));
            }
        }
        return fitness;
    }
}
