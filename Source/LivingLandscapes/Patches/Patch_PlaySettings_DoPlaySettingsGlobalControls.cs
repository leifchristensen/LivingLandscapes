using HarmonyLib;
using LivingLandscapes.Environment;
using LivingLandscapes.Overlay;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Adds one map toggle per environment overlay to the lower right corner, beside vanilla's. Each button shows or hides
/// its layer and is highlighted while active, so several layers can be compared at once.
/// </summary>
[HarmonyPatch(typeof(PlaySettings), nameof(PlaySettings.DoPlaySettingsGlobalControls))]
public static class Patch_PlaySettings_DoPlaySettingsGlobalControls
{
    private static readonly Color ActiveBackground = new Color(1f, 1f, 1f, 0.25f);

    public static void Postfix(WidgetRow row, bool worldView)
    {
        if (worldView || row == null)
            return;
        EnvironmentOverlay overlay = Find.CurrentMap?.GetComponent<MapComponent_Environment>()?.Overlay;
        if (overlay == null)
            return;
        foreach (EnvironmentLayerDef layer in overlay.Available)
        {
            bool on = overlay.IsActive(layer);
            string tooltip = "LL_OverlayToggleTip".Translate(layer.LabelCap, layer.description);
            if (row.ButtonIcon(OverlayTextures.IconFor(layer), tooltip, backgroundColor: on ? ActiveBackground : (Color?)null))
                overlay.Toggle(layer);
        }
    }
}
