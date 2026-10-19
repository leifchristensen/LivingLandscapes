using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Vanilla bends each river segment with up to 40 cells of meander, tuned for one straight segment across the map.
/// River segments laid along the geological course already meander, so they stay straight between their points.
/// </summary>
[HarmonyPatch]
public static class Patch_TileMutatorWorker_River_GetDisplacedPoint
{
    /// <summary>The base method and every river variant's override.</summary>
    public static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
        return System.Linq.Enumerable.Where(System.Linq.Enumerable.Select(
            System.Linq.Enumerable.Concat(new[] { typeof(TileMutatorWorker_River) }, typeof(TileMutatorWorker_River).AllSubclassesNonAbstract()),
            type => type.GetMethod("GetDisplacedPoint", flags, null, new[] { typeof(RiverNode), typeof(float) }, null)),
            method => method != null);
    }

    public static void Postfix(RiverNode riverNode, float t, ref Vector2 __result)
    {
        if (!Patch_TileMutatorWorker_River_GenerateRiverGraph.IsGeologicalNode(riverNode))
            return;
        Vector3 point = riverNode.start + t * (riverNode.end - riverNode.start);
        __result = new Vector2(point.x, point.z);
    }
}
