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
        RiverPlan plan = environment.PlannedRiver;
        if (plan == null)
        {
            Log.Warning($"[LivingLandscapes] No planned river course crosses {map}; using vanilla's river.");
            return true;
        }

        // The main channel in pieces of equal width (narrower beside islands), then the side branches.
        int segments = 0;
        int start = 0;
        for (int i = 1; i <= plan.Main.Count; i++)
        {
            if (i < plan.Main.Count && plan.MainWidth[i] == plan.MainWidth[start])
                continue;
            List<Vector2> piece = plan.Main.GetRange(start, Mathf.Min(i + 1, plan.Main.Count) - start);
            segments += AddNodes(map, piece, plan.MainWidth[start], start == 0, i >= plan.Main.Count);
            start = i;
        }
        foreach (RiverPlan.SideBranch side in plan.Sides)
            segments += AddNodes(map, side.Points, side.Width, false, false);
        Log.Message($"[LivingLandscapes] River on {map} follows the terrain in {segments} segments ({plan.Sides.Count} side channels, {plan.Oxbows.Count} cut-off loops).");
        return false;
    }

    /// <summary>Lays a polyline as river segments, overlapping at joints; ends at the map edge run on past it.</summary>
    private static int AddNodes(Map map, List<Vector2> line, float width, bool upstreamEdge, bool downstreamEdge)
    {
        List<Vector2> course = RiverCourse.Simplify(line, 1f);
        if (course.Count < 2)
            return 0;
        int last = course.Count - 1;
        if (upstreamEdge)
            course[0] -= (course[1] - course[0]).normalized * EdgeOverhang;
        if (downstreamEdge)
            course[last] += (course[last] - course[last - 1]).normalized * EdgeOverhang;
        for (int i = 0; i < last; i++)
        {
            Vector2 a = course[i], b = course[i + 1];
            Vector2 overlap = (b - a).normalized * width * JointOverlap;
            if (i > 0 || !upstreamEdge)
                a -= overlap;
            if (i < last - 1 || !downstreamEdge)
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
        return last;
    }
}
