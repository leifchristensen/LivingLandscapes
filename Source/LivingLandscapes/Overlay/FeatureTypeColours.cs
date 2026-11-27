using System.Collections.Generic;
using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Overlay;

/// <summary>
/// Builds the colour stops of every overlay that shows feature types (<see cref="OverlayProperties.featureTypeColors"/>),
/// after the defs load, so another mod's feature type gets a stop too: value = the type's index, colour = its
/// <see cref="FeatureTypeDef.debugColor"/>, label = its label.
/// </summary>
[StaticConstructorOnStartup]
public static class FeatureTypeColours
{
    private static readonly Color NoColour = Color.grey;

    static FeatureTypeColours()
    {
        List<FeatureTypeDef> types = DefDatabase<FeatureTypeDef>.AllDefsListForReading;
        foreach (FeatureTypeDef type in types)
        {
            if (type.debugColor.a <= 0f)
                Log.Warning($"[LivingLandscapes] Feature type {type.defName} has no debugColor: the \"Feature regions\" overlay draws it grey.");
        }
        foreach (EnvironmentLayerDef layer in DefDatabase<EnvironmentLayerDef>.AllDefsListForReading)
        {
            if (layer.overlay?.featureTypeColors != true)
                continue;
            layer.overlay.colors = new List<ColorStop>();
            for (int i = 0; i < types.Count; i++)
                layer.overlay.colors.Add(new ColorStop { value = i, color = Of(types[i]), label = types[i].label ?? types[i].defName });
        }
    }

    /// <summary>The colour a feature type is drawn in.</summary>
    public static Color Of(FeatureTypeDef type) => type.debugColor.a > 0f ? type.debugColor : NoColour;
}
