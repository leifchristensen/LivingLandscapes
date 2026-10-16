using System.Collections.Generic;
using LivingLandscapes.Environment;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Overlay;

[StaticConstructorOnStartup]
public static class OverlayTextures
{
    public static readonly Texture2D Toggle = ContentFinder<Texture2D>.Get("UI/LivingLandscapes/EnvironmentOverlay");

    private static readonly Dictionary<EnvironmentLayerDef, Texture2D> icons = new Dictionary<EnvironmentLayerDef, Texture2D>();

    /// <summary>The layer's own toggle icon (<see cref="OverlayProperties.icon"/>), or the generic overlay icon.</summary>
    public static Texture2D IconFor(EnvironmentLayerDef layer)
    {
        if (!icons.TryGetValue(layer, out Texture2D icon))
        {
            string path = layer.overlay?.icon;
            icon = (path.NullOrEmpty() ? null : ContentFinder<Texture2D>.Get(path, reportFailure: true)) ?? Toggle;
            icons[layer] = icon;
        }
        return icon;
    }
}
