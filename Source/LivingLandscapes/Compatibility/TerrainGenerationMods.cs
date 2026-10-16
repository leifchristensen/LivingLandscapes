using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LivingLandscapes.Compatibility;

/// <summary>
/// Which other mods touch terrain generation. Supported mods are known to work with geological terrain; any other
/// mod patching the terrain steps is reported so players can decide whether to turn geological terrain off.
/// Only detects Harmony patches; XML-only changes to map generation aren't visible here.
/// </summary>
[StaticConstructorOnStartup]
public static class TerrainGenerationMods
{
    /// <summary>packageId -> display name of mods Living Landscapes supports alongside geological terrain.</summary>
    public static readonly IReadOnlyDictionary<string, string> Supported = new Dictionary<string, string>
    {
        { "koth.realisticplanets2", "Realistic Planets 2" },
    };

    /// <summary>Harmony ids of mods that patch the terrain steps of map generation, excluding Living Landscapes.</summary>
    public static IReadOnlyList<string> ForeignPatchOwners { get; }

    static TerrainGenerationMods()
    {
        var methods = new List<MethodBase>
        {
            AccessTools.Method(typeof(GenStep_ElevationFertility), nameof(GenStep_ElevationFertility.Generate)),
            AccessTools.Method(typeof(GenStep_RocksFromGrid), nameof(GenStep_RocksFromGrid.Generate)),
            AccessTools.Method(typeof(GenStep_Terrain), nameof(GenStep_Terrain.Generate)),
        };

        var owners = new SortedSet<string>();
        foreach (MethodBase method in methods.Where(m => m != null))
        {
            HarmonyLib.Patches info = Harmony.GetPatchInfo(method);
            if (info == null)
                continue;
            foreach (string owner in info.Owners)
            {
                if (owner != LivingLandscapesMod.PackageId)
                    owners.Add(owner);
            }
        }
        ForeignPatchOwners = owners.ToList();

        string supported = string.Join(", ", ActiveSupported());
        string foreign = ForeignPatchOwners.Count == 0 ? "none" : string.Join(", ", ForeignPatchOwners);
        Log.Message($"[LivingLandscapes] Supported terrain mods active: {(supported.Length == 0 ? "none" : supported)}. Other mods patching terrain generation: {foreign}.");
    }

    public static IEnumerable<string> ActiveSupported() =>
        Supported.Where(pair => ModsConfig.IsActive(pair.Key)).Select(pair => pair.Value);
}
