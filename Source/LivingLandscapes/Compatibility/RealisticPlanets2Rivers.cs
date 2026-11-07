using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace LivingLandscapes.Compatibility;

/// <summary>
/// What Realistic Planets 2 (a supported, optional mod) works out for the world's rivers: the rain-weighted flow
/// accumulation of its drainage model, the tile each tile drains to, and the river density chosen when the world was made.
/// Read by reflection so there is no assembly reference; every reader returns "not available" when the mod, a field or the
/// data is missing. The rules mirror its <c>Patch_DrainageRivers</c> (sources: tools/ref/mods-src/RealisticPlanets2).
/// </summary>
public static class RealisticPlanets2Rivers
{
    public const string PackageId = "koth.realisticplanets2";

    private static readonly Type ClimateType = AccessTools.TypeByName("Planets.WorldGen.Climate.ClimateData");

    public static bool Loaded => ModsConfig.IsActive(PackageId) && ClimateType != null;

    private static object Climate()
        => Find.World?.components?.Find(c => c.GetType() == ClimateType);

    private static T Field<T>(object climate, string name) where T : class
        => AccessTools.Field(ClimateType, name)?.GetValue(climate) as T;

    /// <summary>RP2's rain-weighted flow per tile, in the river defs' units (RP2 scales the defs' thresholds by river density, not the flow); null if unavailable.</summary>
    public static float[] Flow()
    {
        if (!Loaded)
            return null;
        float[] flow = Field<float[]>(Climate(), "FlowRain");
        return flow != null && flow.Length > 0 ? flow : null;
    }

    /// <summary>The river density slider of the world being made (1 = 100%; 0 draws no rivers). Worldbuilder's own value wins, as in RP2.</summary>
    public static float RiverDensity()
    {
        try
        {
            if (ModsConfig.IsActive("ferny.Worldbuilder"))
            {
                object data = AccessTools.Field(AccessTools.TypeByName("Worldbuilder.World_ExposeData_Patch"), "worldGenerationData")?.GetValue(null);
                if (AccessTools.Field(data?.GetType(), "riverDensity")?.GetValue(data) is float wb)
                    return wb;
            }
            if (AccessTools.Field(AccessTools.TypeByName("Planets.Core.Planets_GameComponent"), "riverDensity")?.GetValue(null) is float density)
                return density;
        }
        catch (Exception e)
        {
            Log.Warning($"[LivingLandscapes] Could not read Realistic Planets 2's river density ({e.Message}); using 100%.");
        }
        return 1f;
    }

    /// <summary>
    /// The tile each tile drains to, as RP2 routes its rivers (tier-2 hydrology flow and spill direction when it computed them,
    /// else the topology's; -1 for the sea and for closed-basin bottoms). Null if unavailable.
    /// </summary>
    public static int[] Downstream(PlanetLayer layer)
    {
        if (!Loaded)
            return null;
        object climate = Climate();
        int[] dir = Field<int[]>(climate, "FlowDirectionTopology"), spill = Field<int[]>(climate, "SpillDirectionTopology");
        int[] dirHydro = Field<int[]>(climate, "FlowDirectionHydrology"), spillHydro = Field<int[]>(climate, "SpillDirectionHydrology");
        if (dirHydro != null && spillHydro != null && Tier2Hydrology())
        {
            dir = dirHydro;
            spill = spillHydro;
        }
        if (dir == null)
            return null;
        int n = layer.TilesCount;
        var down = new int[n];
        var neighbours = new List<PlanetTile>();
        for (int i = 0; i < n; i++)
        {
            down[i] = -1;
            if (layer[i].PrimaryBiome == BiomeDefOf.Ocean)
                continue;
            int k = i < dir.Length && dir[i] >= 0 ? dir[i] : (spill != null && i < spill.Length ? spill[i] : -1);
            if (k < 0)
                continue;
            Find.WorldGrid.GetTileNeighbors(new PlanetTile(i, layer), neighbours);
            if (k < neighbours.Count)
                down[i] = neighbours[k].tileId;
        }
        return down;
    }

    private static bool Tier2Hydrology()
    {
        try
        {
            object settings = AccessTools.Field(AccessTools.TypeByName("Planets.Core.Controller"), "Settings")?.GetValue(null);
            object features = AccessTools.Field(settings?.GetType(), "featureConfig")?.GetValue(settings)
                              ?? AccessTools.Property(settings?.GetType(), "featureConfig")?.GetValue(settings);
            object on = AccessTools.Property(features?.GetType(), "IsTier2HydrologyEnabled")?.GetValue(features)
                        ?? AccessTools.Field(features?.GetType(), "IsTier2HydrologyEnabled")?.GetValue(features);
            return on is bool b && b;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
