using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>
/// Translates the geology into vanilla's map-generation elevation grid and plans terrace cliffs. Vanilla does not read
/// that grid as a height: every consumer compares it with fixed thresholds, so it is a rock/exposure classifier.
/// <list type="bullet">
/// <item>&gt; 0.70 rock wall (GenStep_RocksFromGrid); &gt; 0.728 thin roof; &gt; 0.798 thick roof (overhead mountain)</item>
/// <item>0.61-0.70 rough stone floor, 0.55-0.61 gravel (MapGenUtility.TerrainFrom); soil by fertility below</item>
/// <item>rock chunks only below 0.55; caves and cave hives only inside rock; ruins and structures only at 0.70 or below</item>
/// </list>
/// Bare rock deep inside a rock mass (from the natural landform) is mountain: mineable, roofed by depth. Other bare
/// rock is rough stone; scree collects below rock; the rest is soil. The walkable ground is stepped into height
/// stages, with a cliff band along every stage edge and steps along each band (<see cref="CliffPlan"/>).
/// </summary>
public static class VanillaTerrainWriter
{
    private const float ThickRoof = 0.82f;
    private const float ThinRoof = 0.75f;
    private const float RockNoRoof = 0.71f;
    private const float RockFloor = 0.65f;
    private const float Scree = 0.58f;
    private const float Soil = 0.3f;

    /// <param name="drainage">Drainage area (log10 cells) per cell, or null: dry washes are then left out.</param>
    /// <param name="climate">Climate wetness 0..1 from tile rainfall.</param>
    public static CliffPlan Write(Map map, float[] soilDepth, float[] landform, float cellSize, SoilModelExtension p, TerraceExtension terraces,
        float[] drainage, float climate, GroundWriterExtension ground)
    {
        int sizeX = map.Size.x, sizeZ = map.Size.z, n = soilDepth.Length;
        bool IsExposed(int i) => soilDepth[i] < p.rockWallDepth;

        float[] depthIntoRock = GridMath.DistanceTo(i => !IsExposed(i), sizeX, sizeZ);
        float[] distanceToRock = GridMath.DistanceTo(IsExposed, sizeX, sizeZ);
        float[] slope = GridMath.Slope(landform, sizeX, sizeZ);

        var values = new float[n];
        for (int i = 0; i < n; i++)
        {
            float metresIntoRock = depthIntoRock[i] * cellSize;
            if (IsExposed(i) && metresIntoRock >= p.massiveRockDepth)
                values[i] = metresIntoRock >= p.thickRoofDepth ? ThickRoof : metresIntoRock >= p.thinRoofDepth ? ThinRoof : RockNoRoof;
            else if (soilDepth[i] < p.rockFloorDepth)
                values[i] = RockFloor;
            else if (distanceToRock[i] * cellSize <= p.screeReach && slope[i] / cellSize >= p.screeMinSlope)
                values[i] = Scree;
            else
                values[i] = Soil;
        }

        // Dry washes: on dry tiles, flash floods scour drainage lines down to gravel.
        int washes = 0;
        if (drainage != null && climate < ground.washMaxClimate)
        {
            float washArea = Mathf.Lerp(ground.washAreaArid, ground.washAreaHumid, climate / ground.washMaxClimate);
            for (int i = 0; i < n; i++)
            {
                if (values[i] == Soil && drainage[i] >= washArea)
                {
                    values[i] = Scree;
                    washes++;
                }
            }
        }

        // Terrace cliffs along stage edges where the ground is cliff-steep, away from mountain rock.
        float stageHeight = Terraces.StageHeight(landform, terraces);
        int[] stage = Terraces.Stages(landform, stageHeight);
        var plan = new CliffPlan(n);
        float[] steepness = Terraces.Steepness(landform, sizeX, sizeZ, cellSize, terraces);
        plan.PlanBands(map, stage, i => values[i] > 0.7f || steepness[i] < 0.5f);
        int steps = plan.PlanSteps(map, stage, terraces.stepSpacing);
        int bands = 0;
        for (int i = 0; i < n; i++)
        {
            if (plan.Kind[i] == CliffKind.None)
                continue;
            values[i] = Mathf.Max(values[i], RockFloor);
            if (plan.Kind[i] != CliffKind.Steps)
                bands++;
        }

        MapGenFloatGrid elevation = MapGenerator.Elevation;
        int rock = 0, floor = 0, scree = 0;
        for (int i = 0; i < n; i++)
        {
            elevation[map.cellIndices.IndexToCell(i)] = values[i];
            if (values[i] > 0.7f) rock++;
            else if (values[i] == RockFloor) floor++;
            else if (values[i] == Scree) scree++;
        }
        Log.Message($"[LivingLandscapes] Geological terrain written for {map}: stages of {stageHeight:F1} m, {bands} cliff cells, {steps} steps; " +
                    $"rock {Percent(rock, n)}, rock floor {Percent(floor, n)}, scree and washes {Percent(scree, n)} (washes {Percent(washes, n)}), " +
                    $"soil {Percent(n - rock - floor - scree, n)}.");
        return plan;
    }

    private static string Percent(int count, int total) => $"{100f * count / Mathf.Max(total, 1):F0}%";
}
