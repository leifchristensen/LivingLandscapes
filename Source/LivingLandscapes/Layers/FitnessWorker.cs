using System;
using LivingLandscapes.Environment;
using LivingLandscapes.Flora;
using Verse;

namespace LivingLandscapes.Layers;

/// <summary>
/// Dev view: how well one species would do on each cell (0 cannot live, 1 optimum). The species is chosen with the
/// "Plant fitness: choose species" debug action; with none chosen it shows the best fitness of any wild plant of the
/// map's biome.
/// </summary>
public class FitnessWorker : EnvironmentLayerWorker
{
    /// <summary>The species the dev view shows, or null for the best of the biome's plants.</summary>
    public static ThingDef Selected;

    public override float[] Compute(EnvironmentContext context)
    {
        var sampler = new HabitatSampler(context);
        var fitness = new float[context.CellCount];
        if (Selected != null)
        {
            Accumulate(sampler, Selected, fitness);
            return fitness;
        }
        foreach (ThingDef plant in context.Map.Biome.AllWildPlants)
            Accumulate(sampler, plant, fitness);
        return fitness;
    }

    private static void Accumulate(HabitatSampler sampler, ThingDef plant, float[] fitness)
    {
        PlantHabitat habitat = PlantHabitat.For(plant);
        for (int i = 0; i < fitness.Length; i++)
            fitness[i] = Math.Max(fitness[i], sampler.Fitness(plant, habitat, i));
    }
}
