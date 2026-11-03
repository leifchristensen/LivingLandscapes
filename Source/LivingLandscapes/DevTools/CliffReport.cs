using System.Collections.Generic;
using System.Text;
using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using Verse;

namespace LivingLandscapes.DevTools;

/// <summary>
/// Test summary lines for the cliffs (#112): how many cliff cells, how they join into bands (8-connected, as a pawn sees
/// a line) and how long those bands are, and what covers the steep ground: mountain rock, cliff or open slope.
/// </summary>
public static class CliffReport
{
    private const int LongBand = 30;

    public static void Append(Map map, StringBuilder summary)
    {
        int sizeX = map.Size.x, sizeZ = map.Size.z;
        int bands = GridMath.LabelRegions(i => IsCliff(map, i), sizeX, sizeZ, out int[] labels, diagonals: true);
        var length = new int[bands];
        int cells = 0;
        foreach (int label in labels)
        {
            if (label < 0)
                continue;
            length[label]++;
            cells++;
        }
        var sorted = new List<int>(length);
        sorted.Sort();
        int longBands = sorted.FindAll(l => l >= LongBand).Count;
        string lengths = bands > 0
            ? $"median {sorted[bands / 2]}, 90th percentile {sorted[bands * 9 / 10]}, longest {sorted[bands - 1]}"
            : "none";
        summary.AppendLine($"cliffs: {cells} cells in {bands} bands, {longBands} of at least {LongBand} cells; band length {lengths}");

        var environment = map.GetComponent<MapComponent_Environment>();
        if (environment == null || !environment.TryGetGrid(EnvironmentLayerDefOf.LL_Slope, out EnvironmentGrid slope))
            return;
        foreach (float threshold in new[] { 0.5f, 1f })
        {
            int steep = 0, rock = 0, cliff = 0;
            for (int i = 0; i < slope.Count; i++)
            {
                if (slope[i] < threshold)
                    continue;
                steep++;
                if (map.edificeGrid[i]?.def.building?.isNaturalRock == true)
                    rock++;
                else if (labels[i] >= 0)
                    cliff++;
            }
            summary.AppendLine($"cliffs: ground with slope >= {threshold:0.0}: {steep} cells, {Percent(rock, steep)} mountain rock, {Percent(cliff, steep)} cliff");
        }
    }

    private static bool IsCliff(Map map, int i) => map.edificeGrid[i] is { } edifice && CliffDefs.IsCliff(edifice.def);

    private static string Percent(int count, int total) => $"{100f * count / System.Math.Max(total, 1):0}%";
}
