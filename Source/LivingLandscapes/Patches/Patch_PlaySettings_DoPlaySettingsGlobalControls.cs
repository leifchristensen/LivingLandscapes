using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LivingLandscapes.Environment;
using LivingLandscapes.Overlay;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Adds the environment overlay button to the map toggles in the lower right corner. Clicking it lists the current
/// map's layers; the button is highlighted while one is shown.
/// </summary>
[HarmonyPatch(typeof(PlaySettings), nameof(PlaySettings.DoPlaySettingsGlobalControls))]
public static class Patch_PlaySettings_DoPlaySettingsGlobalControls
{
    private static readonly Color ActiveBackground = new Color(1f, 1f, 1f, 0.25f);

    public static void Postfix(WidgetRow row, bool worldView)
    {
        if (worldView || row == null)
            return;
        var environment = Find.CurrentMap?.GetComponent<MapComponent_Environment>();
        if (environment == null)
            return;
        List<EnvironmentLayerDef> layers = environment.Overlay.Available.ToList();
        if (layers.Count == 0)
            return;

        EnvironmentLayerDef selected = environment.Overlay.Selected;
        string tooltip = "LL_OverlayToggleTip".Translate(selected != null ? selected.LabelCap.ToString() : "LL_None".Translate().ToString());
        if (!row.ButtonIcon(OverlayTextures.Toggle, tooltip, backgroundColor: selected != null ? ActiveBackground : (Color?)null))
            return;

        var options = new List<FloatMenuOption>
        {
            new FloatMenuOption("LL_None".Translate().CapitalizeFirst(), () => environment.Overlay.Select(null)),
        };
        foreach (EnvironmentLayerDef layer in layers)
        {
            string label = layer == selected ? $"{layer.LabelCap} ✓" : layer.LabelCap.ToString();
            options.Add(new FloatMenuOption(label, () => environment.Overlay.Select(layer)));
        }
        Find.WindowStack.Add(new FloatMenu(options));
    }
}
