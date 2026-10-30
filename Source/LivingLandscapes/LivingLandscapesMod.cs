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

    /// <summary>Whether the settings window shows the generation tuning instead of the general settings.</summary>
    private static bool showTuning;

    public override void DoSettingsWindowContents(Rect inRect)
    {
        if (Widgets.ButtonText(new Rect(inRect.xMax - 220f, inRect.y, 220f, 30f), (showTuning ? "LL_TuningBack" : "LL_TuningOpen").Translate()))
            showTuning = !showTuning;
        if (showTuning)
        {
            TuningPanel.Draw(new Rect(inRect.x, inRect.y + 36f, inRect.width, inRect.height - 36f), Settings);
            return;
        }
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

        string delay = Settings.fireRainDelayHours >= LivingLandscapesSettings.FireRainNever ? "LL_FireRainNever".Translate().ToString()
            : Settings.fireRainDelayHours < 0.5f ? "LL_FireRainVanilla".Translate().ToString() : "LL_FireRainHours".Translate(Settings.fireRainDelayHours.ToString("0")).ToString();
        listing.Label("LL_FireRainDelay".Translate(delay), tooltip: "LL_FireRainDelayTip".Translate());
        Settings.fireRainDelayHours = UnityEngine.Mathf.Round(listing.Slider(Settings.fireRainDelayHours, 0f, LivingLandscapesSettings.FireRainNever));
        listing.Gap();

        listing.CheckboxLabeled("LL_GameplaySiting".Translate(), ref Settings.gameplaySiting, "LL_GameplaySitingTip".Translate());
        listing.Gap();

        listing.CheckboxLabeled("LL_SlopeMovement".Translate(), ref Settings.slopeMovement, "LL_SlopeMovementTip".Translate());
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
