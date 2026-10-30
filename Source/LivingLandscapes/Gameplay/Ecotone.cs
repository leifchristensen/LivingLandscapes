using LivingLandscapes.Geology;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace LivingLandscapes.Gameplay;

/// <summary>Tuning of <see cref="Ecotone"/>, set on the MixedBiome tile mutator in XML.</summary>
public class EcotoneExtension : DefModExtension
{
    /// <summary>Half-width (cells) of the transition zone on each side of vanilla's boundary.</summary>
    public float halfWidth = 40f;

    /// <summary>Wavelength (cells) of the patches the two biomes interleave in across the zone.</summary>
    public float patchScale = 10f;

    /// <summary>
    /// Majority filter after the noise interleave, one pass: a cell takes the other biome when at least this many of its 8
    /// neighbours hold it. 7 clears lone cells and 2-cell specks and keeps the patches; 5 smooths harder; 0 turns it off.
    /// </summary>
    public int majorityNeighbours = 7;
}

/// <summary>
/// Biome transitions (Docs/GAMEPLAY-PLAN.md, G4). Vanilla's mixed-biome tile splits the map along a displaced line: one
/// biome on one side, the other on the other, no overlap. Here the line becomes a zone: the further a cell lies into the
/// other biome's side the likelier it keeps its own biome, and patch noise decides cell by cell, so the biomes interleave
/// (woodland edge, scrub and glades) before one takes over.
/// </summary>
public static class Ecotone
{
    public static void Blur(Map map, EcotoneExtension p)
    {
        var mixed = map.GetComponent<MixedBiomeMapComponent>();
        bool[] grid = mixed?.biomeGrid;
        if (grid == null)
            return;
        int sizeX = map.Size.x, sizeZ = map.Size.z, n = grid.Length;
        float[] toPrimary = GridMath.DistanceTo(i => !grid[i], sizeX, sizeZ);
        float[] toSecondary = GridMath.DistanceTo(i => grid[i], sizeX, sizeZ);
        var noise = new Perlin(1.0 / p.patchScale, 2.0, 0.5, 3, Rand.Int, QualityMode.Medium);
        var result = new bool[n];
        int changed = 0;
        for (int i = 0; i < n; i++)
        {
            // Signed distance into the secondary side (positive) or the primary side (negative).
            float signed = grid[i] ? toPrimary[i] : -toSecondary[i];
            float secondary = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-p.halfWidth, p.halfWidth, signed));
            float threshold = Mathf.Clamp01(0.5f + 0.7f * (float)noise.GetValue(i % sizeX, 0.0, i / sizeX));
            result[i] = secondary > threshold;
            if (result[i] != grid[i])
                changed++;
        }
        // Majority filter: noise decides cell by cell, so specks of one biome can sit inside the other (a boreal soil cell
        // with its own plants inside an ice sheet). One pass smooths them into their surroundings; patches stay.
        var cleaned = (bool[])result.Clone();
        int islands = 0;
        for (int z = 1; z < sizeZ - 1 && p.majorityNeighbours > 0; z++)
        {
            for (int x = 1; x < sizeX - 1; x++)
            {
                int i = z * sizeX + x, other = 0;
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if ((dx != 0 || dz != 0) && result[i + dz * sizeX + dx] != result[i])
                            other++;
                    }
                }
                if (other >= p.majorityNeighbours)
                {
                    cleaned[i] = !result[i];
                    islands++;
                    changed += cleaned[i] != grid[i] ? 1 : -1;
                }
            }
        }
        result = cleaned;
        mixed.biomeGrid = result;
        Log.Message($"[LivingLandscapes] Biome transition on {map}: {changed} of {n} cells changed biome across a zone {2 * p.halfWidth:0} cells wide; {islands} cells joined their surroundings (majority filter).");
    }
}
