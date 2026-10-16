using System.Linq;
using HarmonyLib;
using LivingLandscapes.Compatibility;
using LivingLandscapes.Settings;
using UnityEngine;
using Verse;

namespace LivingLandscapes;

/// <summary>Entry point. RimWorld constructs this once, early, when the mod's assembly loads.</summary>
public class LivingLandscapesMod : Mod
{
    public const string PackageId = "leifchristensen.livinglandscapes";

    public static LivingLandscapesSettings Settings { get; private set; }

    public LivingLandscapesMod(ModContentPack content) : base(content)
    {
        Settings = GetSettings<LivingLandscapesSettings>();

        var harmony = new Harmony(PackageId);
        harmony.PatchAll(typeof(LivingLandscapesMod).Assembly);

        var harmonyVersion = typeof(Harmony).Assembly.GetName().Version;
        Log.Message($"[LivingLandscapes] Loaded. Harmony {harmonyVersion}, {harmony.GetPatchedMethods().Count()} method(s) patched. " +
                    $"Geological terrain: {(Settings.geologicalTerrain ? "on" : "off")}.");
    }

    public override string SettingsCategory() => "LL_SettingsCategory".Translate();

    public override void DoSettingsWindowContents(Rect inRect)
    {
        var listing = new Listing_Standard();
        listing.Begin(inRect);

        listing.CheckboxLabeled("LL_GeologicalTerrain".Translate(), ref Settings.geologicalTerrain, "LL_GeologicalTerrainTip".Translate());
        listing.Label("LL_GeologicalTerrainDesc".Translate());
        listing.Gap();

        string supported = string.Join(", ", TerrainGenerationMods.ActiveSupported());
        listing.Label("LL_SupportedTerrainMods".Translate(supported.Length == 0 ? "LL_None".Translate().ToString() : supported));

        if (TerrainGenerationMods.ForeignPatchOwners.Count > 0)
        {
            GUI.color = new Color(1f, 0.8f, 0.35f);
            listing.Label("LL_UnsupportedTerrainMods".Translate(string.Join(", ", TerrainGenerationMods.ForeignPatchOwners)));
            GUI.color = Color.white;
        }

        listing.End();
    }
}
