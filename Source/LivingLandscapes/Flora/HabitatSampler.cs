using UnityEngine;
using Verse;

namespace LivingLandscapes.Flora;

/// <summary>
/// The environment as plants see it, read from the layer grids once so fitness can be evaluated for any number of
/// species and cells. Two inputs (PLANT-TRAITS.md section 4): Water (moisture, drier on sun-facing slopes, with the
/// soil's room for the roots) and Light (sun times canopy transmittance, which changes as plants grow, so the ecology
/// applies it live through <see cref="Light"/>).
/// </summary>
public class HabitatSampler
{
    public readonly Map Map;
    private readonly float[] moisture, soilDepth, sun;

    /// <summary>A sampler over the layer arrays of the running map (<see cref="MapComponent_Ecology"/>); shade is kept live there.</summary>
    public HabitatSampler(Map map, float[] moisture, float[] soilDepth, float[] sun)
    {
        Map = map;
        this.moisture = moisture;
        this.soilDepth = soilDepth;
        this.sun = sun;
    }

    /// <summary>Water 0..1 at a cell: moisture less <see cref="PlantEcologyDef.sunDrying"/> x (sun - level ground's 0.7).</summary>
    public float Water(int index)
    {
        float level = sun == null ? 0f : sun[index] - 0.7f;
        return Mathf.Clamp01(moisture[index] - PlantEcologyDef.Current.sunDrying * level);
    }

    /// <summary>Light 0..1 at a cell under the given canopy transmittance (1 - shade).</summary>
    public float Light(int index, float transmittance) => (sun == null ? 0.7f : sun[index]) * transmittance;

    /// <summary>
    /// The site's fitness 0..1 for a species from the ground alone: its water curve times root room,
    /// (min(1, soil depth / rooting depth))^<see cref="PlantEcologyDef.rootRoomExponent"/>. Light is applied by the caller.
    /// <paramref name="fundamental"/>: the water curve saturates above its optimum (what the site can carry, P15) instead of
    /// the realised niche (where the species wins against others).
    /// </summary>
    public float Site(PlantHabitat habitat, int index, bool fundamental = false)
    {
        float water = Water(index);
        float fitness = habitat.water == null ? 1f : fundamental ? habitat.water.ResponseUpTo(water) : habitat.water.Response(water);
        if (soilDepth != null && habitat.rootingDepth > 0f)
            fitness *= Mathf.Pow(Mathf.Min(1f, soilDepth[index] / habitat.rootingDepth), PlantEcologyDef.Current.rootRoomExponent);
        return fitness;
    }
}
