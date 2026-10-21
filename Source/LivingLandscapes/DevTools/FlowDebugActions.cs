using LudeonTK;
using UnityEngine;
using Verse;

namespace LivingLandscapes.DevTools;

/// <summary>
/// Dev-mode tests for the water animation (#53). The water shader reads vanilla's flow texture, built once from the flow
/// map (<see cref="WaterInfo.SetTextures"/>); these change the flow map and have the texture built again, to tell
/// whether still-looking moving water lacks flow where the shader looks for it, or animates too weakly to see.
/// </summary>
public static class FlowDebugActions
{
    [DebugAction("Living Landscapes", "Flow test: also write flow at vertically mirrored cells", allowedGameStates = AllowedGameStates.IsCurrentlyOnMap)]
    private static void Mirror()
    {
        Map map = Find.CurrentMap;
        var flow = map.waterInfo.riverFlowMap;
        if (flow == null)
            return;
        int sizeZ = map.Size.z, count = 0;
        foreach (IntVec3 c in map.AllCells)
        {
            if (!c.GetTerrain(map).IsRiver)
                continue;
            int from = (c.x * sizeZ + c.z) * 2, to = (c.x * sizeZ + sizeZ - 1 - c.z) * 2;
            if (flow[to] != 0f || flow[to + 1] != 0f)
                continue;
            flow[to] = flow[from];
            flow[to + 1] = flow[from + 1];
            count++;
        }
        Rebuild(map, $"Flow written at {count} mirrored cells.");
    }

    [DebugAction("Living Landscapes", "Flow test: flow east everywhere", allowedGameStates = AllowedGameStates.IsCurrentlyOnMap)]
    private static void East()
    {
        Map map = Find.CurrentMap;
        var flow = map.waterInfo.riverFlowMap;
        if (flow == null)
            return;
        for (int i = 0; i < flow.Count; i += 2)
        {
            flow[i] = 2f;
            flow[i + 1] = 0f;
        }
        Rebuild(map, "Flow set to east (length 2) on every cell.");
    }

    /// <summary>Drops the built flow texture so the next frame builds it again from the flow map.</summary>
    private static void Rebuild(Map map, string what)
    {
        if (map.waterInfo.riverFlowTexture != null)
            Object.Destroy(map.waterInfo.riverFlowTexture);
        map.waterInfo.riverFlowTexture = null;
        Messages.Message($"[LivingLandscapes] {what} Flow texture rebuilt; watch the water.", RimWorld.MessageTypeDefOf.NeutralEvent, historical: false);
    }
}
