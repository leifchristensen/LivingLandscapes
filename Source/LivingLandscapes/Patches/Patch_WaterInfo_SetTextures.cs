using HarmonyLib;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Patches;

/// <summary>
/// Compatibility with level mods that stack a map's levels as bands in one tall map (As above, So below II): such a mod
/// tells the water shader the map is one band tall (<c>_MapSize</c>), so ripples are not stretched. The shader also uses
/// that size to look up each cell's flow in vanilla's flow texture, which covers the whole stacked map, so on any band
/// above the first the lookup leaves the texture and moving water gets no flow: it stops animating. When the published
/// map size is shorter than the map, this folds vanilla's flow pixels into a texture one band tall that repeats, so the
/// band-sized lookup lands on the right cell of every band. Runs after other postfixes; does nothing on ordinary maps.
/// </summary>
[HarmonyPatch(typeof(WaterInfo), nameof(WaterInfo.SetTextures))]
public static class Patch_WaterInfo_SetTextures
{
    private static readonly AccessTools.FieldRef<WaterInfo, Color[]> FlowMapPixels = AccessTools.FieldRefAccess<WaterInfo, Color[]>("flowMapPixels");

    private static Texture2D source, banded;
    private static int bandedHeight;

    [HarmonyPriority(Priority.Last)]
    public static void Postfix(WaterInfo __instance)
    {
        Map map = __instance.map;
        Texture2D flow = __instance.riverFlowTexture;
        Color[] pixels = flow != null ? FlowMapPixels(__instance) : null;
        if (map == null || pixels == null)
            return;
        int sizeX = map.Size.x, sizeZ = map.Size.z;
        int band = Mathf.RoundToInt(Shader.GetGlobalVector(ShaderPropertyIDs.MapSize).y);
        if (band <= 0 || band >= sizeZ || pixels.Length < sizeX * sizeZ)
            return;
        if (flow != source || band != bandedHeight || banded == null)
            Build(flow, pixels, sizeX, sizeZ, band);
        Shader.SetGlobalTexture(ShaderPropertyIDs.WaterOffsetTex, banded);
    }

    /// <summary>
    /// One band's rows; each row holds the flow of that row in every band (moving water rarely lies at the same place on
    /// two levels, so the bands are summed).
    /// </summary>
    private static void Build(Texture2D flow, Color[] pixels, int sizeX, int sizeZ, int band)
    {
        if (banded != null)
            Object.Destroy(banded);
        var folded = new Color[sizeX * band];
        for (int z = 0; z < sizeZ; z++)
        {
            int row = (z % band) * sizeX, from = z * sizeX;
            for (int x = 0; x < sizeX; x++)
            {
                Color p = pixels[from + x];
                if (p.r != 0f || p.g != 0f)
                    folded[row + x] += p;
            }
        }
        banded = new Texture2D(sizeX, band, TextureFormat.RGFloat, mipChain: false) { wrapMode = TextureWrapMode.Repeat };
        banded.SetPixels(folded);
        banded.Apply();
        source = flow;
        bandedHeight = band;
        Log.Message($"[LivingLandscapes] Water flow folded into bands {band} cells tall for the water shader (stacked levels, {sizeZ / band} bands).");
    }
}
