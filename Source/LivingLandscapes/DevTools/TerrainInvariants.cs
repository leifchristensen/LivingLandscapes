using System.Text;
using LivingLandscapes.Geology;
using RimWorld;
using Verse;

namespace LivingLandscapes.DevTools;

/// <summary>
/// Terrain measurements reported in the export summary. The bounds live in tools/test-expectations.json (checked by
/// check-battery.py), not here: this only measures.
/// Hollows: closed hollows that hold no water. Depression filling runs on a copy of the natural landform (LL_Landform,
/// not the terraced height), with the map edge and every cell with water as outlets and epsilon 0. A hollow cell is a land
/// cell the fill raises by more than <see cref="HollowDepth"/>. Water cells, and mineable rock (natural rock walls do not
/// drain in game terms), are not land. Reports the share of land, the number of separate hollows (one deep hollow is a
/// different defect from many dimples) and the deepest fill.
/// </summary>
public static class TerrainInvariants
{
    /// <summary>Fill depth (metres) above which a land cell counts as lying in a hollow.</summary>
    private const float HollowDepth = 0.05f;

    public static void Report(Map map, float[] landform, float[] surfaceWater, StringBuilder summary)
    {
        int sizeX = map.Size.x, sizeZ = map.Size.z, n = landform.Length;
        var wet = new bool[n];
        var rock = new bool[n];
        for (int i = 0; i < n; i++)
        {
            TerrainDef terrain = map.terrainGrid.TerrainAt(i);
            wet[i] = (surfaceWater != null && surfaceWater[i] > 0f) || (terrain != null && (terrain.IsWater || terrain.IsRiver));
            Building edifice = map.cellIndices.IndexToCell(i).GetEdifice(map);
            rock[i] = edifice != null && edifice.def.building != null && edifice.def.building.isNaturalRock;
        }

        float[] filled = (float[])landform.Clone();
        GridMath.FillDepressions(filled, sizeX, sizeZ, i => wet[i], 0f);

        var hollow = new bool[n];
        int land = 0, hollowCells = 0;
        float deepest = 0f;
        for (int i = 0; i < n; i++)
        {
            if (wet[i] || rock[i])
                continue;
            land++;
            float depth = filled[i] - landform[i];
            if (depth > HollowDepth)
            {
                hollow[i] = true;
                hollowCells++;
                if (depth > deepest)
                    deepest = depth;
            }
        }
        int regions = GridMath.LabelRegions(i => hollow[i], sizeX, sizeZ, out _);
        summary.AppendLine($"terrain hollows: {(land > 0 ? hollowCells * 100f / land : 0f):F2}% of {land} land cells in {regions} closed hollows " +
                           $"(fill > {HollowDepth} m, no water, not rock), deepest {deepest:F2} m");
    }
}
