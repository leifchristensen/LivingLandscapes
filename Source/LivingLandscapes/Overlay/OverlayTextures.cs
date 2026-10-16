using UnityEngine;
using Verse;

namespace LivingLandscapes.Overlay;

[StaticConstructorOnStartup]
public static class OverlayTextures
{
    public static readonly Texture2D Toggle = ContentFinder<Texture2D>.Get("UI/LivingLandscapes/EnvironmentOverlay");
}
