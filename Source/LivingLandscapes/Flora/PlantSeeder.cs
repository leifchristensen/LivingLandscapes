using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Flora;

/// <summary>
/// The initial planting of clustering species as a Thomas process (Docs/FLORA-REVIEW.md, 3.2): parents land where the
/// habitat suits the species, and offspring scatter around each with a Gaussian of the species' dispersal distance.
/// Vanilla fills the map in a single random pass, so a clump can only grow into cells the pass has not visited yet and
/// clumps stay small; seeding the clumps first, canopy before shrubs before ground plants, lets them grow whole. The
/// seeder places only a share of each species' target (<see cref="PlantEcologyDef.seedShare"/>); vanilla's pass then
/// fills the rest, against its own saturation and shares, so the map's density and species mix stay vanilla's.
/// Regrowth in play needs no seeder: the spawner's weights, boosted by the same cluster density grids, grow clumps back.
/// </summary>
public static class PlantSeeder
{
    /// <summary>Plants placed, for the log.</summary>
    public static int Seed(Map map, MapComponent_Ecology ecology)
    {
        float strength = MapComponent_Ecology.Strength;
        PlantEcologyDef tuning = PlantEcologyDef.Current;
        if (strength <= 0f || tuning.seedShare <= 0f)
            return 0;
        WildPlantSpawner spawner = map.wildPlantSpawner;
        float desired = spawner.CurrentWholeMapNumDesiredPlants;
        var species = map.Biome.wildPlants.Select(r => r.plant)
            .Where(def => !def.plant.cavePlant && !def.IsDeadPlant && PlantHabitat.For(def).ClusterSigma(def) > 0f && !IsWaterPlant(def))
            .OrderByDescending(def => (int)PlantHabitat.For(def).stratum == (int)PlantStratum.Canopy ? 2 : PlantHabitat.For(def).stratum == PlantStratum.Shrub ? 1 : 0)
            .ThenByDescending(def => def.plant.wildOrder)
            .ToList();

        int placed = 0, clumps = 0;
        var cells = map.cellsInRandomOrder;
        foreach (ThingDef def in species)
        {
            PlantHabitat habitat = PlantHabitat.For(def);
            int wanted = Mathf.RoundToInt(desired * spawner.GetCommonalityPctOfPlant(def) * tuning.seedShare * strength);
            if (wanted < 3)
                continue;
            float sigma = habitat.ClusterSigma(def);
            float clump = Mathf.Max(3f, Mathf.PI * 4f * sigma * sigma * tuning.seedFill);
            int made = 0, failures = 0;
            while (made < wanted && failures < 400)
            {
                IntVec3 parent = cells.Get(Rand.Range(0, map.Area));
                if (!Suits(map, ecology, def, parent, tuning) || !Place(map, def, parent))
                {
                    failures++;
                    continue;
                }
                made++;
                clumps++;
                int offspring = Mathf.RoundToInt(clump * Rand.Range(0.5f, 1.5f));
                for (int i = 0; i < offspring && made < wanted; i++)
                {
                    var cell = new IntVec3(Mathf.RoundToInt(parent.x + Rand.Gaussian(0f, sigma)), 0, Mathf.RoundToInt(parent.z + Rand.Gaussian(0f, sigma)));
                    if (Suits(map, ecology, def, cell, tuning) && Place(map, def, cell))
                        made++;
                }
            }
            placed += made;
        }
        Log.Message($"[LivingLandscapes] Plant seeding on {map}: {placed} plants in {clumps} clumps of {species.Count} clustering species (share {tuning.seedShare:0.##} of target x strength {strength:0.##}).");
        return placed;
    }

    /// <summary>Plants that need water or its shore are placed by vanilla's own rules.</summary>
    private static bool IsWaterPlant(ThingDef def) => def.plant.wildPlantUseDistanceToShore || def.plant.sowTags?.Contains("Water") == true
                                                      || (def.terrainAffordanceNeeded != null && def.terrainAffordanceNeeded.defName.Contains("Shallow"));

    /// <summary>The cell is free, vanilla would allow the species there, and a draw against its fitness succeeds.</summary>
    private static bool Suits(Map map, MapComponent_Ecology ecology, ThingDef def, IntVec3 c, PlantEcologyDef tuning)
    {
        if (!c.InBounds(map) || c.GetPlant(map) != null || c.GetCover(map) != null || c.GetEdifice(map) != null || c.Roofed(map))
            return false;
        if (map.fertilityGrid.FertilityAt(c) <= 0f || !def.CanEverPlantAt(c, map))
            return false;
        float fitness = ecology.Fitness(def, map.cellIndices.CellToIndex(c));
        // Fitness is a product of several curves, so even good ground rarely scores near 1: accept relative to a reference.
        return fitness >= tuning.minFitness && Rand.Value < Mathf.Pow(Mathf.Clamp01(fitness / tuning.seedFitnessReference), tuning.fitnessExponent);
    }

    private static bool Place(Map map, ThingDef def, IntVec3 c)
    {
        WildPlantSpawner.SpawnPlant(def, map, c, setRandomGrowth: true);
        return true;
    }
}
