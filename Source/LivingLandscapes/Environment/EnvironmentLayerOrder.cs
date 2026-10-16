using System.Collections.Generic;
using Verse;

namespace LivingLandscapes.Environment;

/// <summary>All layer Defs sorted so every layer comes after the layers it depends on.</summary>
public static class EnvironmentLayerOrder
{
    private static List<EnvironmentLayerDef> sorted;

    public static IReadOnlyList<EnvironmentLayerDef> Sorted => sorted ??= Sort();

    private static List<EnvironmentLayerDef> Sort()
    {
        var result = new List<EnvironmentLayerDef>();
        var visiting = new HashSet<EnvironmentLayerDef>();
        var done = new HashSet<EnvironmentLayerDef>();
        foreach (EnvironmentLayerDef layer in DefDatabase<EnvironmentLayerDef>.AllDefsListForReading)
            Visit(layer, visiting, done, result);
        return result;
    }

    private static void Visit(EnvironmentLayerDef layer, HashSet<EnvironmentLayerDef> visiting, HashSet<EnvironmentLayerDef> done, List<EnvironmentLayerDef> result)
    {
        if (done.Contains(layer))
            return;
        if (!visiting.Add(layer))
        {
            Log.Error($"[LivingLandscapes] Environment layer dependency cycle through {layer.defName}; it will be computed out of order.");
            return;
        }
        foreach (EnvironmentLayerDef dependency in layer.dependsOn)
            Visit(dependency, visiting, done, result);
        visiting.Remove(layer);
        done.Add(layer);
        result.Add(layer);
    }
}
