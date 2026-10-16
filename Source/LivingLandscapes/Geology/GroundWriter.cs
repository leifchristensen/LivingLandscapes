using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace LivingLandscapes.Geology;

/// <summary>Tuning for <see cref="GroundWriter"/>, set on the wetness layer Def in XML.</summary>
public class GroundWriterExtension : DefModExtension
{
    /// <summary>Weights of wetness and soil depth in the fertility score; the rest is noise for natural variety.</summary>
    public float fertilityWetness = 0.55f;
    public float fertilitySoilDepth = 0.35f;
    public float fertilityNoise = 0.1f;

    /// <summary>Soil depth (metres) counted as fully deep for the fertility score.</summary>
    public float deepSoil = 2f;

    /// <summary>
    /// Dry washes: drainage lines (log10 upslope cells) whose beds are scoured to gravel. On the driest tiles from
    /// <see cref="washAreaArid"/>; the threshold rises with climate wetness and washes stop at <see cref="washMaxClimate"/>,
    /// where vegetation holds the channels and wetness shows them instead.
    /// </summary>
    public float washAreaArid = 2.4f;
    public float washAreaHumid = 3.2f;
    public float washMaxClimate = 0.6f;

    /// <summary>
    /// Streams: standing shallow water on a drainage line of at least this area (log10 cells), no more than
    /// <see cref="streamMaxHalfWidth"/> cells from its bank, becomes moving water (<see cref="StreamWriter"/>).
    /// </summary>
    public float streamArea = 2.8f;
    public float streamMaxHalfWidth = 3f;

    /// <summary>Noise mixed into wetness for the biome's wet patches (mud, marsh, ponds), so their edges look natural.</summary>
    public float wetPatchNoise = 0.2f;
}

/// <summary>
/// Makes vanilla's ground follow the geology ("On" mode). Vanilla picks soil, rich soil, sand and so on from its
/// fertility grid, and mud, marsh and ponds from each biome's patch-maker noise. Both are rank-mapped from our fields:
/// the wettest, deepest-soiled cells get vanilla's highest fertility values, the wettest cells get the patch makers'
/// highest noise values. Each biome keeps its own share of every ground type; only where it lies changes.
/// </summary>
public static class GroundWriter
{
    private static readonly AccessTools.FieldRef<TerrainPatchMaker, ModuleBase> PatchNoise =
        AccessTools.FieldRefAccess<TerrainPatchMaker, ModuleBase>("noise");
    private static readonly AccessTools.FieldRef<TerrainPatchMaker, Map> PatchMap =
        AccessTools.FieldRefAccess<TerrainPatchMaker, Map>("currentlyInitializedForMap");

    /// <summary>Rewrites the fertility grid and the biome's wet patch makers. Call after the elevation grid is written.</summary>
    public static void Write(Map map, float[] wetness, float[] soilDepth, GroundWriterExtension p)
    {
        int sizeX = map.Size.x, sizeZ = map.Size.z, n = wetness.Length;
        var noise = new Perlin(0.05, 2.0, 0.5, 3, Rand.Int, QualityMode.Medium);

        // Fertility: our score, ranked onto vanilla's own fertility values.
        MapGenFloatGrid fertility = MapGenerator.Fertility;
        var vanilla = new float[n];
        var score = new float[n];
        for (int i = 0; i < n; i++)
        {
            IntVec3 c = map.cellIndices.IndexToCell(i);
            vanilla[i] = fertility[c];
            float deep = Mathf.Clamp01(soilDepth[i] / Mathf.Max(0.01f, p.deepSoil));
            score[i] = p.fertilityWetness * wetness[i] + p.fertilitySoilDepth * deep
                       + p.fertilityNoise * (0.5f + 0.5f * (float)noise.GetValue(c.x, 0.0, c.z));
        }
        float[] ranked = RankMap(score, vanilla);
        for (int i = 0; i < n; i++)
            fertility[map.cellIndices.IndexToCell(i)] = ranked[i];

        // Wet patches (mud, marsh, ponds, rich soil rings) in the wettest ground; never on rock or rough stone.
        MapGenFloatGrid elevation = MapGenerator.Elevation;
        int wetMakers = 0;
        foreach (TerrainPatchMaker maker in map.Biome.terrainPatchMakers)
        {
            if (!IsWetPatchMaker(maker))
                continue;
            var own = new Perlin(maker.perlinFrequency, maker.perlinLacunarity, maker.perlinPersistence, maker.perlinOctaves,
                Rand.Range(0, int.MaxValue), QualityMode.Medium);
            var reference = new float[n];
            var wet = new float[n];
            for (int i = 0; i < n; i++)
            {
                IntVec3 c = map.cellIndices.IndexToCell(i);
                reference[i] = (float)own.GetValue(c.x, 0.0, c.z);
                wet[i] = elevation[c] >= 0.61f ? -1f : wetness[i] + p.wetPatchNoise * reference[i];
            }
            PatchNoise(maker) = new GridNoise(RankMap(wet, reference), sizeX, sizeZ);
            PatchMap(maker) = map;
            wetMakers++;
        }
        Log.Message($"[LivingLandscapes] Ground follows wetness and soil on {map}: fertility rank-mapped, {wetMakers} wet patch makers steered.");
    }

    /// <summary>Patch makers that make wet ground: water, mud, marsh or richer-than-normal soil.</summary>
    private static bool IsWetPatchMaker(TerrainPatchMaker maker)
    {
        foreach (TerrainThreshold threshold in maker.thresholds)
        {
            TerrainDef t = threshold.terrain;
            if (t == null)
                continue;
            if (t.IsWater || t.defName.Contains("Mud") || t.defName.Contains("Marsh") || t.fertility > 1f)
                return true;
        }
        return false;
    }

    /// <summary>Gives each cell the reference value of the same rank its score has: highest score, highest value.</summary>
    private static float[] RankMap(float[] score, float[] reference)
    {
        int n = score.Length;
        var order = new int[n];
        for (int i = 0; i < n; i++)
            order[i] = i;
        Array.Sort(order, (a, b) => score[a].CompareTo(score[b]));
        var sorted = (float[])reference.Clone();
        Array.Sort(sorted);
        var result = new float[n];
        for (int k = 0; k < n; k++)
            result[order[k]] = sorted[k];
        return result;
    }

    /// <summary>A precomputed per-cell value served as noise.</summary>
    private class GridNoise : ModuleBase
    {
        private readonly float[] values;
        private readonly int sizeX, sizeZ;

        public GridNoise(float[] values, int sizeX, int sizeZ) : base(0)
        {
            this.values = values;
            this.sizeX = sizeX;
            this.sizeZ = sizeZ;
        }

        public override double GetValue(double x, double y, double z) =>
            values[Mathf.Clamp((int)z, 0, sizeZ - 1) * sizeX + Mathf.Clamp((int)x, 0, sizeX - 1)];
    }
}
