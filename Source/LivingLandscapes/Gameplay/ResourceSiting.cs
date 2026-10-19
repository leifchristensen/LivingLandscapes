using System.Collections.Generic;
using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace LivingLandscapes.Gameplay;

/// <summary>
/// Chooses the ore of a mineable lump by where it lies (<see cref="ResourceSitingExtension"/>). One instance per map being
/// generated: the rock depth grid, the ores' patch noise and the normalisers that keep each ore's overall share at vanilla's.
/// </summary>
public class ResourceSiting
{
    private const int NormaliserSamples = 500;

    private static ResourceSiting current;

    private readonly Map map;
    private readonly float[] rockStrength;
    private readonly float[] rockDepth;
    private readonly IntVec3 anchor;
    private readonly Dictionary<ThingDef, Perlin> patches = new Dictionary<ThingDef, Perlin>();
    private readonly Dictionary<ThingDef, float> normalisers = new Dictionary<ThingDef, float>();

    private ResourceSiting(Map map, MapComponent_Environment environment)
    {
        this.map = map;
        int sizeX = map.Size.x, sizeZ = map.Size.z;
        MapGenFloatGrid elevation = MapGenerator.Elevation;
        rockDepth = GridMath.DistanceTo(i => elevation[map.cellIndices.IndexToCell(i)] <= 0.7f, sizeX, sizeZ);
        rockStrength = environment.TryGetGrid(EnvironmentLayerDefOf.LL_RockStrength, out EnvironmentGrid grid) ? grid.ToArray() : null;
        anchor = environment.SettlementAnchor;
    }

    /// <summary>The siting for this map, or null when the map is not one the geology sited (siting off, other map kinds).</summary>
    public static ResourceSiting For(Map map)
    {
        if (!GameplaySiting.Enabled || MapGenerator.Elevation == null)
            return null;
        var environment = map.GetComponent<MapComponent_Environment>();
        if (environment == null || !environment.SettlementAnchor.IsValid)
            return null;
        if (current == null || current.map != map)
            current = new ResourceSiting(map, environment);
        return current;
    }

    /// <summary>The ore to scatter at <paramref name="cell"/>: vanilla's commonality times the siting weight there.</summary>
    public ThingDef Choose(IntVec3 cell, float maxValue)
    {
        ThingDef chosen = null;
        float total = 0f;
        foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
        {
            float commonality = Commonality(def, maxValue);
            if (commonality <= 0f)
                continue;
            float share = commonality * Weight(def, cell) / Normaliser(def, maxValue);
            total += share;
            if (share > 0f && Rand.Value * total < share)
                chosen = def;
        }
        return chosen;
    }

    private static float Commonality(ThingDef def, float maxValue)
    {
        if (def.building == null)
            return 0f;
        return def.building.mineableThing != null && def.building.mineableThing.BaseMarketValue > maxValue ? 0f : def.building.mineableScatterCommonality;
    }

    /// <summary>The ore's siting weight at a cell; 1 for an ore with no extension.</summary>
    private float Weight(ThingDef def, IntVec3 cell)
    {
        var p = def.GetModExtension<ResourceSitingExtension>();
        if (p == null)
            return 1f;
        int i = map.cellIndices.CellToIndex(cell);
        float weight = 1f;
        if (p.rockStrength != null && rockStrength != null)
            weight *= p.rockStrength.Evaluate(rockStrength[i]);
        if (p.rockDepth != null)
            weight *= p.rockDepth.Evaluate(rockDepth[i]);
        if (p.distance != null)
            weight *= p.distance.Evaluate((cell - anchor).LengthHorizontal);
        if (p.patchScale > 0f)
        {
            if (!patches.TryGetValue(def, out Perlin noise))
                patches[def] = noise = new Perlin(1.0 / p.patchScale, 2.0, 0.5, 2, Rand.Int, QualityMode.Medium);
            // Perlin sums stay within about +-0.7: stretch to 0..1, so a patch factor averages about 1 and the richest doubles.
            float patch = Mathf.Clamp01(0.5f + 0.7f * (float)noise.GetValue(cell.x, 0.0, cell.z));
            weight *= Mathf.Lerp(1f, 2f * patch, p.patchContrast);
        }
        return Mathf.Max(weight, 0f);
    }

    /// <summary>Mean weight of the ore over a sample of the map's rock, so the siting moves the ore without changing how much of it there is.</summary>
    private float Normaliser(ThingDef def, float maxValue)
    {
        if (normalisers.TryGetValue(def, out float known))
            return known;
        float sum = 0f;
        int found = 0;
        for (int tries = 0; tries < NormaliserSamples * 8 && found < NormaliserSamples; tries++)
        {
            IntVec3 cell = map.cellsInRandomOrder.Get(Rand.Range(0, map.Area));
            if (rockDepth[map.cellIndices.CellToIndex(cell)] <= 0f)
                continue;
            sum += Weight(def, cell);
            found++;
        }
        float mean = found > 0 ? sum / found : 1f;
        return normalisers[def] = Mathf.Max(mean, 0.05f);
    }
}
