using Verse;

namespace LivingLandscapes.Gameplay;

/// <summary>
/// Where one mineable resource (a vanilla ore's <c>ThingDef</c>) likes to lie, as multipliers on its vanilla commonality
/// (Docs/GAMEPLAY-PLAN.md, G2). Vanilla scatters lumps at random rock cells and picks the ore by commonality alone; with this
/// extension the pick also depends on the cell. Every curve left out means no preference. Overall shares stay vanilla's:
/// <see cref="ResourceSiting"/> rescales each ore so its weight averages 1 over the map's rock, so these only move the ore.
/// </summary>
public class ResourceSitingExtension : DefModExtension
{
    /// <summary>By LL_RockStrength at the cell (0 soft .. 1 hard): which beds host the ore.</summary>
    public SimpleCurve rockStrength;

    /// <summary>By cells into the rock from its nearest open edge: common ores near the edge (reachable), rare ones deeper.</summary>
    public SimpleCurve rockDepth;

    /// <summary>By cells from the settlement anchor: common ores near the start, rare ones far, so there is something to find.</summary>
    public SimpleCurve distance;

    /// <summary>Wavelength (cells) of the patches the ore forms: rich and poor ground, found rather than spread evenly. 0: no patches.</summary>
    public float patchScale;

    /// <summary>How strongly patches matter, 0 (none) to 1 (the ore lies only in the richest patches).</summary>
    public float patchContrast = 0.8f;
}
