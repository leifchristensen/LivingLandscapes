using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>
/// Translates the geology into vanilla's map-generation grids and plans cliffs ("On" mode). Every cell is classified
/// once by the ordered ground rules (<see cref="GroundRulesDef"/>, XML): water, rock, rough floor, marsh, mud, gravel
/// or soil. The class sets the value in vanilla's elevation grid, which vanilla does not read as a height: every
/// consumer compares it with fixed thresholds, so it is a rock/exposure classifier.
/// <list type="bullet">
/// <item>&gt; 0.70 rock wall (GenStep_RocksFromGrid); &gt; 0.728 thin roof; &gt; 0.798 thick roof (overhead mountain)</item>
/// <item>0.61-0.70 rough stone floor, 0.55-0.61 gravel (MapGenUtility.TerrainFrom); soil by fertility below</item>
/// <item>rock chunks only below 0.55; caves and cave hives only inside rock; ruins and structures only at 0.70 or below</item>
/// </list>
/// Water, marsh and mud are planned as ground cover and placed when vanilla picks terrain
/// (<see cref="Patches.Patch_MapGenUtility_GetNaturalTerrainAt"/>). The walkable ground is stepped into height stages,
/// with a cliff band along every stage edge on dry ground and steps along each band (<see cref="CliffPlan"/>).
/// </summary>
public static class VanillaTerrainWriter
{
    private const float RockNoRoof = 0.71f;
    private const float RockFloor = 0.65f;

    /// <summary>
    /// The fields ground rules can test (<see cref="GroundFields"/>): every layer computed so far, plus the derived ones.
    /// </summary>
    public static GroundFields BuildFields(Map map, Environment.MapComponent_Environment environment, SoilModelExtension p, float cellSize, float climate)
    {
        int sizeX = map.Size.x, sizeZ = map.Size.z;
        var fields = new GroundFields();
        foreach (Environment.EnvironmentLayerDef layer in DefDatabase<Environment.EnvironmentLayerDef>.AllDefs)
        {
            if (environment.TryGetGrid(layer, out Environment.EnvironmentGrid grid))
                fields.Add(layer.defName, grid.ToArray());
        }
        float[] soil = environment.TryGetGrid(Environment.EnvironmentLayerDefOf.LL_SoilDepth, out var soilGrid) ? soilGrid.ToArray() : null;
        float[] landform = environment.TryGetGrid(Environment.EnvironmentLayerDefOf.LL_Landform, out var landformGrid) ? landformGrid.ToArray() : null;
        float[] water = environment.TryGetGrid(Environment.EnvironmentLayerDefOf.LL_SurfaceWater, out var waterGrid) ? waterGrid.ToArray() : null;
        if (soil != null)
        {
            bool Bare(int i) => soil[i] < p.rockWallDepth;
            float[] depth = GridMath.DistanceTo(i => !Bare(i), sizeX, sizeZ);
            float[] distance = GridMath.DistanceTo(Bare, sizeX, sizeZ);
            for (int i = 0; i < depth.Length; i++)
            {
                depth[i] *= cellSize;
                distance[i] *= cellSize;
            }
            fields.Add("RockDepth", depth);
            fields.Add("RockDistance", distance);
        }
        if (water != null)
            fields.Add("WaterDistance", GridMath.DistanceTo(i => water[i] > 0f, sizeX, sizeZ));
        if (landform != null)
        {
            var filled = (float[])landform.Clone();
            GridMath.FillDepressions(filled, sizeX, sizeZ, _ => false, 0f);
            for (int i = 0; i < filled.Length; i++)
                filled[i] -= landform[i];
            fields.Add("Hollow", filled);
        }
        fields.AddConstant("Climate", climate);
        return fields;
    }

    /// <summary>
    /// Classifies every cell, writes vanilla's elevation grid, and plans cliffs. <paramref name="cover"/> receives the
    /// ground the geology places itself (water, marsh, mud) per cell.
    /// </summary>
    public static CliffPlan Write(Map map, GroundFields fields, GroundRulesDef rules, float[] soilDepth, float[] landform, float cellSize,
        SoilModelExtension p, TerraceExtension terraces, RockStrata strata, out GroundCover[] cover)
    {
        int sizeX = map.Size.x, sizeZ = map.Size.z, n = soilDepth.Length;
        int[] rule = fields.Classify(rules, n);
        var values = new float[n];
        cover = new GroundCover[n];
        var perRule = new int[rules.rules.Count];
        for (int i = 0; i < n; i++)
        {
            GroundRule r = rules.rules[rule[i]];
            values[i] = r.elevation;
            cover[i] = r.cover;
            perRule[rule[i]]++;
        }

        float[] depthIntoRock = GridMath.DistanceTo(i => soilDepth[i] >= p.rockWallDepth, sizeX, sizeZ);
        int passCells = strata == null ? 0 : OpenPasses(map, values, depthIntoRock, cellSize, strata, p);

        // Terrace cliffs along stage edges where the ground is cliff-steep, away from mountain rock and water.
        var (stageHeight, stage, steepness) = Terraces.Plan(landform, sizeX, sizeZ, cellSize, terraces);
        var plan = new CliffPlan(n);
        int knobs = MakeRockKnobs(map, values, stage, steepness, terraces);
        GroundCover[] planned = cover;
        plan.PlanBands(map, stage, i => values[i] > 0.7f || steepness[i] < 0.5f || GroundCovers.IsWater(planned[i]));
        int steps = plan.PlanSteps(map, stage, terraces.stepSpacing);
        int bands = 0;
        for (int i = 0; i < n; i++)
        {
            if (plan.Kind[i] == CliffKind.None)
                continue;
            values[i] = Mathf.Max(values[i], RockFloor);
            cover[i] = GroundCover.None;
            if (plan.Kind[i] != CliffKind.Steps)
                bands++;
        }

        // Connectivity: how many separate walkable areas the rock leaves (counting areas over 0.5 % of the map).
        int areas = GridMath.LabelRegions(i => values[i] <= 0.7f, sizeX, sizeZ, out int[] labels);
        var areaSize = new int[areas];
        foreach (int label in labels)
        {
            if (label >= 0)
                areaSize[label]++;
        }
        int largest = 0, sizeable = 0, walkable = 0;
        foreach (int size in areaSize)
        {
            walkable += size;
            largest = Mathf.Max(largest, size);
            if (size > n / 200)
                sizeable++;
        }

        MapGenFloatGrid elevation = MapGenerator.Elevation;
        for (int i = 0; i < n; i++)
            elevation[map.cellIndices.IndexToCell(i)] = values[i];

        var shares = new List<string>();
        for (int r = 0; r < perRule.Length; r++)
        {
            if (perRule[r] > 0)
                shares.Add($"{rules.rules[r].label} {Percent(perRule[r], n)}");
        }
        Log.Message($"[LivingLandscapes] Geological terrain written for {map}: {string.Join(", ", shares)}; stages of {stageHeight:F1} m, " +
                    $"{bands} cliff cells, {steps} steps, {knobs} cells of rock knobs, {passCells} pass cells; {sizeable} walkable areas " +
                    $"(largest holds {Percent(largest, Mathf.Max(1, walkable))} of walkable ground).");
        return plan;
    }

    /// <summary>
    /// Passes: ridges in a 2D map must not be unbroken walls. Where an open joint crosses a ridge (its line within 45
    /// degrees of the ridge's across direction), weathering has opened it into a gap of rough stone. The across
    /// direction comes from the structure tensor of the smoothed rock mask, which is reliable even on a ridge's crest.
    /// Only rock near the edge of a rock mass is opened, so massive mountain interiors stay solid.
    /// </summary>
    private static int OpenPasses(Map map, float[] values, float[] depthIntoRock, float cellSize, RockStrata strata, SoilModelExtension p)
    {
        int sizeX = map.Size.x, sizeZ = map.Size.z, n = values.Length;
        var ones = new float[n];
        var wall = new float[n];
        for (int i = 0; i < n; i++)
        {
            ones[i] = 1f;
            wall[i] = values[i] > 0.7f ? 1f : 0f;
        }
        float[] smooth = GridMath.MaskedBlur(wall, ones, sizeX, sizeZ, 2f);
        var jxx = new float[n];
        var jxz = new float[n];
        var jzz = new float[n];
        for (int z = 0; z < sizeZ; z++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                int i = z * sizeX + x;
                float gx = (smooth[z * sizeX + Mathf.Min(x + 1, sizeX - 1)] - smooth[z * sizeX + Mathf.Max(x - 1, 0)]) * 0.5f;
                float gz = (smooth[Mathf.Min(z + 1, sizeZ - 1) * sizeX + x] - smooth[Mathf.Max(z - 1, 0) * sizeX + x]) * 0.5f;
                jxx[i] = gx * gx;
                jxz[i] = gx * gz;
                jzz[i] = gz * gz;
            }
        }
        jxx = GridMath.MaskedBlur(jxx, ones, sizeX, sizeZ, 4f);
        jxz = GridMath.MaskedBlur(jxz, ones, sizeX, sizeZ, 4f);
        jzz = GridMath.MaskedBlur(jzz, ones, sizeX, sizeZ, 4f);

        const float CrossingCos = 0.7071f;
        int opened = 0;
        for (int i = 0; i < n; i++)
        {
            if (values[i] <= 0.7f || depthIntoRock[i] * cellSize > p.passMaxDepth)
                continue;
            float angle = 0.5f * Mathf.Atan2(2f * jxz[i], jxx[i] - jzz[i]);
            var across = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            IntVec3 c = map.cellIndices.IndexToCell(i);
            for (int set = 0; set < 2; set++)
            {
                Vector2 normal = strata.JointNormal(set);
                var line = new Vector2(-normal.y, normal.x);
                if (Mathf.Abs(Vector2.Dot(line, across)) < CrossingCos)
                    continue;
                if (strata.DistanceToJoint(set, c.x, c.z, p.passMinOpen) <= p.passHalfWidth)
                {
                    values[i] = RockFloor;
                    opened++;
                    break;
                }
            }
        }
        return opened;
    }

    /// <summary>
    /// Rock knobs: a cliff shows which way a slope drops, so a ring of cliffs closing round a small summit says nothing.
    /// A small height stage that is a summit (every neighbour lower, not touching the map edge or rock) with mostly
    /// steep sides is a knob of rock instead: it becomes mountain rock, and no cliffs ring it. Returns the cells made rock.
    /// </summary>
    private static int MakeRockKnobs(Map map, float[] values, int[] stage, float[] steepness, TerraceExtension t)
    {
        int sizeX = map.Size.x, sizeZ = map.Size.z, n = values.Length;
        var visited = new bool[n];
        var region = new List<int>();
        var queue = new Queue<int>();
        int made = 0;
        for (int start = 0; start < n; start++)
        {
            if (visited[start] || values[start] > 0.7f)
                continue;
            region.Clear();
            queue.Enqueue(start);
            visited[start] = true;
            bool summit = true;
            int boundary = 0, steepBoundary = 0;
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                region.Add(c);
                int cx = c % sizeX, cz = c / sizeX;
                if (cx == 0 || cz == 0 || cx == sizeX - 1 || cz == sizeZ - 1)
                    summit = false;
                for (int d = 0; d < 4; d++)
                {
                    IntVec3 off = new Rot4(d).FacingCell;
                    int nx = cx + off.x, nz = cz + off.z;
                    if (nx < 0 || nz < 0 || nx >= sizeX || nz >= sizeZ)
                        continue;
                    int nb = nz * sizeX + nx;
                    if (values[nb] > 0.7f || stage[nb] > stage[c])
                    {
                        summit = false;
                        continue;
                    }
                    if (stage[nb] == stage[c])
                    {
                        if (!visited[nb])
                        {
                            visited[nb] = true;
                            queue.Enqueue(nb);
                        }
                        continue;
                    }
                    boundary++;
                    if (steepness[c] >= 0.5f)
                        steepBoundary++;
                }
            }
            if (!summit || region.Count > t.knobMaxCells || steepBoundary * 2 < boundary)
                continue;
            foreach (int c in region)
                values[c] = RockNoRoof;
            made += region.Count;
        }
        return made;
    }

    private static string Percent(int count, int total) => $"{100f * count / Mathf.Max(total, 1):F0}%";
}
