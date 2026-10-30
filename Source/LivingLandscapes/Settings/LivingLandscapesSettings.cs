using System.Collections.Generic;
using Verse;

namespace LivingLandscapes.Settings;

/// <summary>Player options, saved by RimWorld in the mod's config file.</summary>
public class LivingLandscapesSettings : ModSettings
{
    /// <summary>
    /// Reshape newly generated maps from geological rules (drainage, basins, valleys). Off: the mod only reads
    /// the terrain other generators made, for players who run unsupported terrain-generation mods.
    /// </summary>
    public bool geologicalTerrain = true;

    /// <summary>
    /// Vanilla's scattered ruins and ancient debris (<c>LL_ScatteredRuins</c>). Off for now: they look out of place
    /// until terrain generation is polished.
    /// </summary>
    public bool scatteredRuins;

    /// <summary>
    /// Plant ecology: wild plants follow the ground (wetness, soil, slope, sun), cluster by dispersal and shade each other
    /// (Docs/FLORA-REVIEW.md). Off: vanilla's wild plant placement.
    /// </summary>
    public bool plantEcology = true;

    /// <summary>How strongly the ecology applies, 0 (vanilla) to 1 (full).</summary>
    public float habitatStrength = 1f;

    /// <summary>
    /// Gameplay siting ("dial back to fun"): the start spot, mineable resources and discoveries follow the ground's suitability
    /// instead of vanilla's random picks. Off: vanilla's placement.
    /// </summary>
    public bool gameplaySiting = true;

    /// <summary>Pawns walk more slowly uphill than across or downhill, and plan routes over gentle ground.</summary>
    public bool slopeMovement = true;

    /// <summary>Overrides of the generation tuning, as text by the value's path (<see cref="Tuning"/>); empty keeps XML's values.</summary>
    /// <summary>
    /// In-game hours a large fire burns before vanilla's fire response (weather changes sooner, rain 15x likelier) starts.
    /// 0 = vanilla; <see cref="FireRainNever"/> or more = never.
    /// </summary>
    public float fireRainDelayHours;

    public const float FireRainNever = 49f;

    public Dictionary<string, string> tuning = new();

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref geologicalTerrain, "geologicalTerrain", true);
        Scribe_Values.Look(ref scatteredRuins, "scatteredRuins", false);
        Scribe_Values.Look(ref plantEcology, "plantEcology", true);
        Scribe_Values.Look(ref habitatStrength, "habitatStrength", 1f);
        Scribe_Values.Look(ref gameplaySiting, "gameplaySiting", true);
        Scribe_Values.Look(ref slopeMovement, "slopeMovement", true);
        Scribe_Values.Look(ref fireRainDelayHours, "fireRainDelayHours", 0f);
        Scribe_Collections.Look(ref tuning, "tuning", LookMode.Value, LookMode.Value);
        tuning ??= new Dictionary<string, string>();
    }
}
