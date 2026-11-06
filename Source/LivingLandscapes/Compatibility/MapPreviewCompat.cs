using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace LivingLandscapes.Compatibility;

/// <summary>
/// Soft integration with the Map Preview mod (m00nl1ght.MapPreview, issue #198). Map Preview generates a preview by running
/// the real terrain generation steps on a throwaway map, but only builds the map components on its allow list. Our geology
/// is planned by <c>MapComponent_Environment</c>; without it the elevation patch finds no component and leaves vanilla
/// terrain, so every preview showed vanilla terrain. We add our component to that allow list. Nothing is required: when Map
/// Preview is absent (or changes its internals) this does nothing and the preview simply stays as it was.
/// </summary>
[StaticConstructorOnStartup]
public static class MapPreviewCompat
{
    private const string GeneratorType = "MapPreview.MapPreviewGenerator";
    private static readonly string[] AllowLists = { "IncludedMapComponentsMinimal", "IncludedMapComponentsFull" };

    /// <summary>True once our component is on Map Preview's allow lists.</summary>
    public static bool Registered { get; private set; }

    static MapPreviewCompat()
    {
        // Map Preview (a Lunar component) loads its assemblies after the startup constructors have run, so look for it afterwards.
        LongEventHandler.ExecuteWhenFinished(Register);
    }

    private static void Register()
    {
        try
        {
            Type generator = AccessTools.TypeByName(GeneratorType);
            if (generator == null)
                return;
            string component = typeof(Environment.MapComponent_Environment).FullName;
            foreach (string name in AllowLists)
            {
                if (AccessTools.Field(generator, name)?.GetValue(null) is HashSet<string> allowed)
                    allowed.Add(component);
            }
            Registered = true;
            Log.Message("[LivingLandscapes] Map Preview found: its previews now use Living Landscapes terrain.");
        }
        catch (Exception e)
        {
            Log.Warning($"[LivingLandscapes] Could not register with Map Preview; its previews show vanilla terrain. {e.Message}");
        }
    }
}
