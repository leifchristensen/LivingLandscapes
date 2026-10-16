using HarmonyLib;
using RimWorld;
using UnityEngine;

namespace LivingLandscapes.Patches;

/// <summary>
/// Vanilla bends each river segment with up to 40 cells of meander, tuned for one straight segment across the map.
/// River segments laid along the geological course already meander, so they stay straight between their points.
/// </summary>
[HarmonyPatch(typeof(TileMutatorWorker_River), "GetDisplacedPoint")]
public static class Patch_TileMutatorWorker_River_GetDisplacedPoint
{
    public static void Postfix(RiverNode riverNode, float t, ref Vector2 __result)
    {
        if (!Patch_TileMutatorWorker_River_GenerateRiverGraph.IsGeologicalNode(riverNode))
            return;
        Vector3 point = riverNode.start + t * (riverNode.end - riverNode.start);
        __result = new Vector2(point.x, point.z);
    }
}
