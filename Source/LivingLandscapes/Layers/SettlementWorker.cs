using LivingLandscapes.Environment;
using LivingLandscapes.Gameplay;
using LivingLandscapes.Geology;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Layers;

/// <summary>
/// Settlement suitability 0..1 of each cell: how good the ground is to build a colony on. Level, dry, open ground with
/// farmland about it, water at a walkable distance (near enough to fetch, far enough to stay out of floods), no
/// swamp or deep water at the doorstep, rock close for mining and a partly enclosing rim for defence. The needs and
/// their curves are XML (<see cref="SettlementExtension"/>); a site's score is their weighted geometric mean, and only
/// open dry ground can score at all. Runs at the Siting stage, when the geology has planned the ground but not yet placed it.
/// </summary>
public class SettlementWorker : EnvironmentLayerWorker
{
    private const float Impassable = 0.70f;
    private const float Soil = 0.6f;

    public override GenerationStage StageFor(Map map) => GenerationStage.Siting;

    public override float[] Compute(EnvironmentContext context)
    {
        MapComponent_Environment environment = context.Environment;
        GroundCover[] cover = environment.PlannedCover;
        float[] slope = context.Get(EnvironmentLayerDefOf.LL_Slope);
        float[] wetness = context.Get(EnvironmentLayerDefOf.LL_Wetness);
        if (cover == null || slope == null || wetness == null)
            return null;
        var p = def.GetModExtension<SettlementExtension>() ?? new SettlementExtension();
        Map map = context.Map;
        int sizeX = context.SizeX, sizeZ = context.SizeZ, n = context.CellCount;
        MapGenFloatGrid elevation = MapGenerator.Elevation, fertility = MapGenerator.Fertility;
        CliffPlan cliffs = environment.PlannedCliffs;

        int nRock = 0, nWet = 0, nCliff = 0, nHigh = 0, nOpen = 0;
        var rock = new bool[n];
        var open = new float[n];
        var farmland = new float[n];
        var barrier = new float[n];
        var hazard = new float[n];
        for (int i = 0; i < n; i++)
        {
            IntVec3 c = map.cellIndices.IndexToCell(i);
            rock[i] = elevation[c] > Impassable;
            bool deepWater = cover[i] == GroundCover.DeepWater || cover[i] == GroundCover.DeepMovingWater;
            bool wet = GroundCovers.IsWetCover(cover[i]);
            bool cliff = cliffs != null && cliffs.Kind[i] != CliffKind.None;
            bool dry = !rock[i] && !wet && !cliff && elevation[c] < Soil;
            open[i] = dry ? 1f : 0f;
            if (rock[i]) nRock++; else if (wet) nWet++; else if (cliff) nCliff++; else if (elevation[c] >= Soil) nHigh++; else nOpen++;
            farmland[i] = dry && fertility[c] >= p.farmlandFertility ? 1f : 0f;
            barrier[i] = rock[i] || deepWater ? 1f : 0f;
            hazard[i] = deepWater || cover[i] == GroundCover.Marsh || cover[i] == GroundCover.Mud ? 1f : 0f;
        }

        Log.Message($"[LivingLandscapes] Settlement ground on {map}: open {nOpen * 100f / n:0}%, rock {nRock * 100f / n:0}%, wet {nWet * 100f / n:0}%, cliff {nCliff * 100f / n:0}%, rough or gravel above {Soil} {nHigh * 100f / n:0}%.");
        var fields = new GroundFields();
        fields.Add("LL_Slope", slope);
        fields.Add("LL_Wetness", wetness);
        fields.Add("Room", Blur(open, sizeX, sizeZ, p.roomSigma));
        fields.Add("Farmland", Blur(farmland, sizeX, sizeZ, p.farmlandSigma));
        fields.Add("Hazard", Blur(hazard, sizeX, sizeZ, p.hazardSigma));
        fields.Add("Refuge", Ring(barrier, sizeX, sizeZ, Mathf.RoundToInt(p.refugeInner), Mathf.RoundToInt(p.refugeOuter)));
        fields.Add("WaterDistance", GridMath.DistanceTo(i => GroundCovers.IsWater(cover[i]), sizeX, sizeZ, 999f));
        fields.Add("RockDistance", GridMath.DistanceTo(i => rock[i], sizeX, sizeZ, 999f));

        foreach (SettlementTerm term in p.terms)
        {
            if (!fields.Has(term.field))
                Log.ErrorOnce($"[LivingLandscapes] Settlement term uses unknown field '{term.field}'; it is ignored.", term.field.GetHashCode());
        }
        var result = new float[n];
        for (int i = 0; i < n; i++)
        {
            if (open[i] <= 0f)
                continue;
            float logSum = 0f, weightSum = 0f;
            foreach (SettlementTerm term in p.terms)
            {
                float value = fields.Value(term.field, i);
                if (float.IsNaN(value))
                    continue;
                logSum += term.weight * Mathf.Log(Mathf.Max(term.curve.Evaluate(value), 0.001f));
                weightSum += term.weight;
            }
            result[i] = weightSum > 0f ? Mathf.Exp(logSum / weightSum) : 0f;
        }
        return result;
    }

    private static float[] Blur(float[] mask, int sizeX, int sizeZ, float sigma)
    {
        var ones = new float[mask.Length];
        for (int i = 0; i < ones.Length; i++)
            ones[i] = 1f;
        return GridMath.MaskedBlur(mask, ones, sizeX, sizeZ, sigma);
    }

    /// <summary>Share of <paramref name="mask"/> in the square ring between two half-widths around each cell (cells outside the map don't count).</summary>
    private static float[] Ring(float[] mask, int sizeX, int sizeZ, int inner, int outer)
    {
        // Summed-area table of the mask, so each box costs four lookups.
        int w = sizeX + 1;
        var sum = new double[w * (sizeZ + 1)];
        for (int z = 0; z < sizeZ; z++)
        {
            double row = 0;
            for (int x = 0; x < sizeX; x++)
            {
                row += mask[z * sizeX + x];
                sum[(z + 1) * w + x + 1] = sum[z * w + x + 1] + row;
            }
        }
        double Box(int x, int z, int r, out int cells)
        {
            int x0 = Mathf.Max(0, x - r), x1 = Mathf.Min(sizeX - 1, x + r), z0 = Mathf.Max(0, z - r), z1 = Mathf.Min(sizeZ - 1, z + r);
            cells = (x1 - x0 + 1) * (z1 - z0 + 1);
            return sum[(z1 + 1) * w + x1 + 1] - sum[z0 * w + x1 + 1] - sum[(z1 + 1) * w + x0] + sum[z0 * w + x0];
        }
        var ring = new float[mask.Length];
        for (int z = 0; z < sizeZ; z++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                double far = Box(x, z, outer, out int farCells);
                double near = Box(x, z, inner, out int nearCells);
                int cells = farCells - nearCells;
                ring[z * sizeX + x] = cells > 0 ? (float)((far - near) / cells) : 0f;
            }
        }
        return ring;
    }
}
