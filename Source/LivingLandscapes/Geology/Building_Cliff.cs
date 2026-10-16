using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>
/// A cliff piece cut from a particular rock (granite, marble...). Its sprite is greyscale with a mask, as vanilla
/// apparel and furniture: the rock parts take the rock's colour, the lichen and soil on its ledges take a colour
/// for the biome. Its name includes the rock ("granite cliff").
/// </summary>
public class Building_Cliff : Building
{
    private ThingDef rock;
    private Color lichen = new Color(0.55f, 0.58f, 0.45f);

    /// <summary>The natural rock this cliff is made of. Set before spawning.</summary>
    public ThingDef Rock
    {
        get => rock;
        set => rock = value;
    }

    /// <summary>Colour of the lichen and soil on the cliff's ledges. Set before spawning.</summary>
    public Color Lichen
    {
        get => lichen;
        set => lichen = value;
    }

    public override Color DrawColor
    {
        get => rock?.graphicData?.color ?? base.DrawColor;
        set => base.DrawColor = value;
    }

    public override Color DrawColorTwo => lichen;

    public override string LabelNoCount => rock == null ? base.LabelNoCount : $"{rock.label} {def.label}";

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Defs.Look(ref rock, "rock");
        Scribe_Values.Look(ref lichen, "lichen", new Color(0.55f, 0.58f, 0.45f));
    }

    /// <summary>
    /// Lichen and soil colour for a map: green-grey where it is wet, dusty tan where it is dry, pale grey-green where
    /// it is cold. A placeholder rule until plants and soil drive it.
    /// </summary>
    public static Color LichenColorFor(Map map)
    {
        float rainfall = map.TileInfo?.rainfall ?? 600f;
        float temperature = map.TileInfo?.temperature ?? 10f;
        var wet = new Color(0.42f, 0.55f, 0.32f);
        var dry = new Color(0.72f, 0.63f, 0.47f);
        var cold = new Color(0.62f, 0.66f, 0.58f);
        Color byRain = Color.Lerp(dry, wet, Mathf.InverseLerp(200f, 1600f, rainfall));
        return Color.Lerp(byRain, cold, Mathf.InverseLerp(5f, -15f, temperature));
    }
}
