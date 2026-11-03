using HarmonyLib;
using LivingLandscapes.Environment;
using LivingLandscapes.Overlay;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Adds one map toggle per environment overlay to the lower right corner, beside vanilla's. Each button shows or hides
/// its layer and shows vanilla's check or cross for its state, so several layers can be compared at once.
/// </summary>
[HarmonyPatch(typeof(PlaySettings), nameof(PlaySettings.DoPlaySettingsGlobalControls))]
public static class Patch_PlaySettings_DoPlaySettingsGlobalControls
{
    public static void Postfix(WidgetRow row, bool worldView)
    {
        if (worldView || row == null)
            return;
        EnvironmentOverlay overlay = Find.CurrentMap?.GetComponent<MapComponent_Environment>()?.Overlay;
        if (overlay == null)
            return;
        foreach (EnvironmentLayerDef layer in overlay.Available)
        {
            // Dev views (computed on demand) are for development; players see only the finished layers.
            if (!layer.overlay.toggle || (layer.onDemand && !Prefs.DevMode))
                continue;
            bool on = overlay.IsActive(layer);
            bool wasOn = on;
            string tooltip = "LL_OverlayToggleTip".Translate(layer.LabelCap, layer.description);
            // Vanilla's toggle: draws the green check or red cross beside the icon, like the play settings around it.
            row.ToggleableIcon(ref on, OverlayTextures.IconFor(layer), tooltip);
            if (on != wasOn)
                overlay.Toggle(layer);
        }
    }
}
