using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// With geological terrain on, a world river follows the valley the geology carved for it instead of vanilla's
/// straight line: its course becomes a chain of river segments, which vanilla then fills with water, banks and flow.
/// Variants with their own river layout (confluence, delta, river island, headwater) keep vanilla's behaviour.
/// </summary>
[HarmonyPatch(typeof(TileMutatorWorker_River), "GenerateRiverGraph")]
public static class Patch_TileMutatorWorker_River_GenerateRiverGraph
{
    /// <summary>River segments extend this far past the map edge, as vanilla's do.</summary>
    private const float EdgeOverhang = 25f;

    /// <summary>Segments overlap by this fraction of the river width so bends have no gaps on their outside.</summary>
    private const float JointOverlap = 0.5f;

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RiverNode, object> geologicalNodes =
        new System.Runtime.CompilerServices.ConditionalWeakTable<RiverNode, object>();

    /// <summary>True for river segments laid along the geological course (they already meander).</summary>
    public static bool IsGeologicalNode(RiverNode node) => node != null && geologicalNodes.TryGetValue(node, out _);

    public static bool Prefix(TileMutatorWorker_River __instance, Map map)
    {
        if (__instance.GetType() != typeof(TileMutatorWorker_River) || !GeologicalTerrain.AppliesTo(map))
            return true;
        var environment = map.GetComponent<MapComponent_Environment>();
        if (environment == null)
            return true;
        if (!(map.TileInfo is SurfaceTile surface) || surface.Rivers.NullOrEmpty())
            return true;

        WorldContext world = WorldContext.For(map, 1f);
        if (!world.HasRiver)
            return true;
        List<Vector2> course = RiverCourse.FromPlan(environment.PlannedRiverCourse, map.Size.x, map.Size.z);
        if (course == null)
        {
            Log.Warning($"[LivingLandscapes] No planned river course crosses {map}; using vanilla's river.");
            return true;
        }

        float width = RiverCourse.WidthOnMap(surface);
        course[0] -= (course[1] - course[0]).normalized * EdgeOverhang;
        int last = course.Count - 1;
        course[last] += (course[last] - course[last - 1]).normalized * EdgeOverhang;
        for (int i = 0; i < last; i++)
        {
            Vector2 a = course[i], b = course[i + 1];
            Vector2 overlap = (b - a).normalized * width * JointOverlap;
            if (i > 0)
                a -= overlap;
            if (i < last - 1)
                b += overlap;
            var node = new RiverNode
            {
                start = new Vector3(a.x, 0f, a.y),
                end = new Vector3(b.x, 0f, b.y),
                width = width,
            };
            geologicalNodes.Add(node, null);
            map.waterInfo.riverGraph.Add(node);
        }
        Log.Message($"[LivingLandscapes] River on {map} follows the terrain in {last} segments.");
        return false;
    }
}
