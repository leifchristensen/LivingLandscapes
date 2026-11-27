using System.Collections.Generic;
using UnityEngine;

namespace LivingLandscapes.Geology;

/// <summary>One line of a map's region table: a feature region that contributes to the terrain, numbered from 1.</summary>
public class PlannedRegion
{
    public int number;
    public FeatureRegion region;

    /// <summary>The most weight its shape can reach (<see cref="FeatureShapeMath.PeakWeight"/>).</summary>
    public float peak;
}

/// <summary>
/// Where each feature region of a freshly generated map lies, per map cell, for the "Feature regions" overlay and the test
/// exports. Never saved: a reloaded map has none. It draws no random number, so it cannot change the map.
/// </summary>
public class PlannedRegions
{
    /// <summary>The largest difference the self-check accepts between the two ways of computing a weight.</summary>
    public const float SelfCheckTolerance = 1e-6f;

    private readonly FeaturePlan plan;
    private readonly int sizeX;
    private readonly int sizeZ;
    private readonly List<PlannedRegion> table = new List<PlannedRegion>();

    public IReadOnlyList<PlannedRegion> Table => table;

    /// <summary>The one region the dev overlay shows (debug action "Feature regions: show one region"); null shows all.</summary>
    public PlannedRegion Shown { get; set; }

    /// <summary>Per map cell (x fastest), the table index of the region with the largest relative weight there; -1 for none.</summary>
    public int[] Dominant { get; }

    /// <summary>Per map cell, that region's relative weight, 0..1.</summary>
    public float[] DominantWeight { get; }

    /// <summary>The largest difference found between <see cref="FeaturePlan.WeightAtMapCell"/> and the generator's own weights.</summary>
    public float SelfCheckDiff { get; }

    public bool SelfCheckOk => SelfCheckDiff <= SelfCheckTolerance;

    /// <param name="conditions">The conditions the plan laid on the erosion model's grid, with each region's weights.</param>
    /// <param name="mapPosition">The map position (cells, not rounded) of a model cell.</param>
    public PlannedRegions(FeaturePlan plan, ConditionFields conditions, int sizeX, int sizeZ, int gx, int gz,
        System.Func<int, int, Vector2> mapPosition)
    {
        this.plan = plan;
        this.sizeX = sizeX;
        this.sizeZ = sizeZ;
        var planIndex = new List<int>();
        for (int r = 0; r < plan.Regions.Count; r++)
        {
            FeatureRegion region = plan.Regions[r];
            // A region that contributes nothing (the bay and cove marker) shapes no ground, so it is not listed.
            if (region.contributions.Count == 0)
                continue;
            table.Add(new PlannedRegion
            {
                number = table.Count + 1, region = region,
                peak = FeatureShapeMath.PeakWeight(region.kind, region.side, region.size, region.edge),
            });
            planIndex.Add(r);
        }

        Dominant = new int[sizeX * sizeZ];
        DominantWeight = new float[sizeX * sizeZ];
        for (int z = 0; z < sizeZ; z++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                int cell = z * sizeX + x;
                Vector2 p = FeatureShapeMath.MapFraction(x, z, sizeX, sizeZ);
                Vector2 warp = plan.WarpAt(p);
                int best = -1;
                float strongest = 0f;
                for (int t = 0; t < table.Count; t++)
                {
                    float w = FeatureShapeMath.RelativeWeight(FeatureShapeMath.WarpedWeight(table[t].region, p, warp), table[t].peak);
                    // A tie keeps the region listed first.
                    if (w > strongest)
                    {
                        strongest = w;
                        best = t;
                    }
                }
                Dominant[cell] = best;
                DominantWeight[cell] = strongest;
            }
        }

        // Self-check: where a model cell lies on the map, the weight asked for by map position is the generator's own weight.
        float largest = 0f;
        for (int z = 0; z < gz; z++)
        {
            for (int x = 0; x < gx; x++)
            {
                Vector2 m = mapPosition(x, z);
                if (m.x < 0f || m.y < 0f || m.x > sizeX - 1 || m.y > sizeZ - 1)
                    continue;
                for (int t = 0; t < table.Count; t++)
                {
                    float diff = Mathf.Abs(plan.WeightAtMapCell(table[t].region, m.x, m.y, sizeX, sizeZ) -
                                           conditions.regionWeights[planIndex[t]][z * gx + x]);
                    // A weight that is not a number must fail the check, which a plain maximum would drop.
                    largest = Mathf.Max(largest, float.IsNaN(diff) ? float.PositiveInfinity : diff);
                }
            }
        }
        SelfCheckDiff = largest;
    }

    /// <summary>One region's relative weight per map cell (x fastest), whether or not another region dominates there.</summary>
    public float[] RelativeWeights(PlannedRegion row)
    {
        var values = new float[sizeX * sizeZ];
        for (int z = 0; z < sizeZ; z++)
        {
            for (int x = 0; x < sizeX; x++)
                values[z * sizeX + x] = FeatureShapeMath.RelativeWeight(plan.WeightAtMapCell(row.region, x, z, sizeX, sizeZ), row.peak);
        }
        return values;
    }
}
