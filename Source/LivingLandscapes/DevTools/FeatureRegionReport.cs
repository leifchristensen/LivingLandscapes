using System;
using System.IO;
using System.Text;
using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using LivingLandscapes.Overlay;
using UnityEngine;
using Verse;

namespace LivingLandscapes.DevTools;

/// <summary>
/// The feature regions' lines of an export's summary: the self-check and one line per region with the share of the map it
/// covers. With -ll-export-raw, also each region's relative weight per cell (Region_n.f32), whether or not another region
/// is stronger there.
/// </summary>
public static class FeatureRegionReport
{
    /// <summary>A region's core: where its relative weight is at least this.</summary>
    private const float CoreFrom = 0.8f;

    /// <summary>A region's rim: from this relative weight up to the core.</summary>
    private const float RimFrom = 0.2f;

    public static void Append(Map map, StringBuilder summary)
    {
        // An earlier export of a map with more regions would leave files that belong to no line below.
        foreach (string old in Directory.GetFiles(LayerImageExporter.Folder, "Region_*.f32"))
            File.Delete(old);
        PlannedRegions regions = map.GetComponent<MapComponent_Environment>()?.PlannedRegions;
        if (regions == null)
        {
            summary.AppendLine("feature regions: regions are only known on a freshly generated map");
            return;
        }
        summary.AppendLine(FormattableString.Invariant(
            $"feature regions self-check: {(regions.SelfCheckOk ? "ok" : "FAIL")} (max diff {regions.SelfCheckDiff:G3})"));
        bool raw = GenCommandLine.CommandLineArgPassed("ll-export-raw");
        foreach (PlannedRegion row in regions.Table)
        {
            float[] weights = regions.RelativeWeights(row);
            int core = 0, rim = 0;
            foreach (float w in weights)
            {
                if (w >= CoreFrom)
                    core++;
                else if (w >= RimFrom)
                    rim++;
            }
            FeatureRegion r = row.region;
            string colour = ColorUtility.ToHtmlStringRGB(FeatureTypeColours.Of(r.type)).ToLowerInvariant();
            summary.AppendLine(FormattableString.Invariant(
                $"feature region {row.number} {r.type.defName} ({r.kind}, dir ({r.direction.x:F3},{r.direction.y:F3}), offset {r.offset:F3}, size {r.size:F3}, edge {r.edge:F3}, extra relief {r.extraRelief:F1} m): ") +
                FormattableString.Invariant(
                    $"core {100f * core / weights.Length:F1}% of map, rim {100f * rim / weights.Length:F1}%, peak weight {row.peak:F3}, colour #{colour}"));
            if (raw)
                LayerImageExporter.ExportRaw(weights, Path.Combine(LayerImageExporter.Folder, $"Region_{row.number}.f32"));
        }
    }
}
