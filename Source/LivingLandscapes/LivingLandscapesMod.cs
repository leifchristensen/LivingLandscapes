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
        Log.Message($"[LivingLandscapes] {DevTools.BuildStamp.Text}");
    }

    public override string SettingsCategory() => "LL_SettingsCategory".Translate();

    public override void DoSettingsWindowContents(Rect inRect)
    {
        var listing = new Listing_Standard();
        listing.Begin(inRect);

        listing.CheckboxLabeled("LL_GeologicalTerrain".Translate(), ref Settings.geologicalTerrain, "LL_GeologicalTerrainTip".Translate());
        listing.Label("LL_GeologicalTerrainDesc".Translate());
        listing.Gap();

        listing.CheckboxLabeled("LL_ScatteredRuins".Translate(), ref Settings.scatteredRuins, "LL_ScatteredRuinsTip".Translate());
        listing.Gap();

        listing.CheckboxLabeled("LL_PlantEcology".Translate(), ref Settings.plantEcology, "LL_PlantEcologyTip".Translate());
        if (Settings.plantEcology)
        {
            listing.Label("LL_HabitatStrength".Translate(Settings.habitatStrength.ToStringPercent()), tooltip: "LL_HabitatStrengthTip".Translate());
            Settings.habitatStrength = listing.Slider(Settings.habitatStrength, 0f, 1f);
        }
        listing.Gap();

        listing.CheckboxLabeled("LL_GameplaySiting".Translate(), ref Settings.gameplaySiting, "LL_GameplaySitingTip".Translate());
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
