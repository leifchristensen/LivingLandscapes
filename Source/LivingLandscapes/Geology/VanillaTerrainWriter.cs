using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>
/// Translates the geology into vanilla's map-generation elevation grid and plans cliffs. Vanilla does not read that
/// grid as a height: every consumer compares it with fixed thresholds, so it is a rock/exposure classifier.
/// <list type="bullet">
/// <item>&gt; 0.70 rock wall (GenStep_RocksFromGrid); &gt; 0.728 thin roof; &gt; 0.798 thick roof (overhead mountain)</item>
/// <item>0.61-0.70 rough stone floor, 0.55-0.61 gravel (MapGenUtility.TerrainFrom); soil by fertility below</item>
/// <item>rock chunks only below 0.55; caves and cave hives only inside rock; ruins and structures only at 0.70 or below</item>
/// </list>
/// Where bedrock is exposed, the cell becomes one of: mountain rock (inside a large rock mass, mineable, roofed by
/// depth), a sheer cliff or a scarp (steep faces, see <see cref="CliffPlan"/>), or rough stone floor (gentle bare
/// rock). Scree collects below rock; everything else is soil.
/// </summary>
public static class VanillaTerrainWriter
{
    private const float ThickRoof = 0.82f;
    private const float ThinRoof = 0.75f;
    private const float RockNoRoof = 0.71f;
    private const float RockFloor = 0.65f;
    private const float Scree = 0.58f;
    private const float Soil = 0.3f;

    public static CliffPlan Write(Map map, float[] soilDepth, float[] height, float cellSize, SoilModelExtension p)
    {
        int sizeX = map.Size.x, sizeZ = map.Size.z, n = soilDepth.Length;
        bool IsExposed(int i) => soilDepth[i] < p.rockWallDepth;

        float[] depthIntoRock = GridMath.DistanceTo(i => !IsExposed(i), sizeX, sizeZ);
        float[] distanceToRock = GridMath.DistanceTo(IsExposed, sizeX, sizeZ);
        float[] slope = GridMath.Slope(height, sizeX, sizeZ);

        var plan = new CliffPlan(n);
        var values = new float[n];
        bool IsCliffBand(int i) => IsExposed(i) && depthIntoRock[i] * cellSize < p.massiveRockDepth && slope[i] / cellSize >= p.scarpSlope;
        for (int i = 0; i < n; i++)
        {
            float gradient = slope[i] / cellSize;
            float metresIntoRock = depthIntoRock[i] * cellSize;
            if (IsExposed(i) && metresIntoRock >= p.massiveRockDepth)
            {
                values[i] = metresIntoRock >= p.thickRoofDepth ? ThickRoof : metresIntoRock >= p.thinRoofDepth ? ThinRoof : RockNoRoof;
            }
            else if (IsCliffBand(i))
            {
                // Steep bare rock: the band's ground is rough stone; its cliff stands at the foot (below).
                values[i] = RockFloor;
            }
            else if (soilDepth[i] < p.rockFloorDepth)
            {
                values[i] = RockFloor;
            }
            else if (distanceToRock[i] * cellSize <= p.screeReach && gradient >= p.screeMinSlope)
            {
                values[i] = Scree;
            }
            else
            {
                values[i] = Soil;
            }
        }

        PlanCliffs(map, plan, height, IsCliffBand, cellSize, p);

        MapGenFloatGrid elevation = MapGenerator.Elevation;
        int rock = 0, floor = 0, scree = 0, sheer = 0, scarp = 0;
        for (int i = 0; i < n; i++)
        {
            elevation[map.cellIndices.IndexToCell(i)] = values[i];
            if (plan.Kind[i] == CliffKind.Sheer) sheer++;
            else if (plan.Kind[i] == CliffKind.Scarp) scarp++;
            else if (values[i] > 0.7f) rock++;
            else if (values[i] == RockFloor) floor++;
            else if (values[i] == Scree) scree++;
        }
        Log.Message($"[LivingLandscapes] Geological terrain written for {map}: rock {Percent(rock, n)}, sheer cliff {Percent(sheer, n)}, " +
                    $"scarp {Percent(scarp, n)}, rock floor {Percent(floor, n)}, scree {Percent(scree, n)}, " +
                    $"soil {Percent(n - rock - sheer - scarp - floor - scree, n)}.");
        return plan;
    }

    /// <summary>
    /// One cliff per band, at its foot: the lowest band cell along the downhill direction. Its drop is measured from
    /// the top of the band to the ground just below the foot; the band's average steepness decides sheer or scarp.
    /// </summary>
    private static void PlanCliffs(Map map, CliffPlan plan, float[] height, System.Func<int, bool> isBand, float cellSize, SoilModelExtension p)
    {
        CellIndices indices = map.cellIndices;
        for (int i = 0; i < height.Length; i++)
        {
            if (!isBand(i))
                continue;
            Rot4 facing = CliffPlan.DownhillFacing(height, map.Size.x, map.Size.z, i);
            IntVec3 foot = indices.IndexToCell(i);
            IntVec3 below = foot + facing.FacingCell;
            if (below.InBounds(map) && isBand(indices.CellToIndex(below)))
                continue; // not the foot: the band continues downhill

            // Walk uphill across the band to its top.
            IntVec3 top = foot;
            int run = 1;
            while (true)
            {
                IntVec3 next = top - facing.FacingCell;
                if (!next.InBounds(map) || !isBand(indices.CellToIndex(next)))
                {
                    if (next.InBounds(map))
                        top = next; // first cell above the band: its height is the top of the cliff
                    break;
                }
                top = next;
                run++;
            }
            float bottom = below.InBounds(map) ? height[indices.CellToIndex(below)] : height[i];
            float drop = Mathf.Max(0f, height[indices.CellToIndex(top)] - bottom);
            plan.Kind[i] = drop / (run * cellSize) >= p.sheerSlope ? CliffKind.Sheer : CliffKind.Scarp;
            plan.Facing[i] = facing;
            plan.Drop[i] = drop;
        }
    }

    private static string Percent(int count, int total) => $"{100f * count / Mathf.Max(total, 1):F0}%";
}
