using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>How tall cliff faces are drawn, set on the cliff ThingDefs in XML.</summary>
public class CliffGraphicsExtension : DefModExtension
{
    /// <summary>Screen height (cells) drawn per metre of drop, like vanilla's compressed tall trees.</summary>
    public float cellsPerMetre = 0.3f;

    /// <summary>Tallest face drawn, in cells above the cliff's own cell.</summary>
    public float maxExtraCells = 6f;

    /// <summary>
    /// Share of the height shown for each facing. Seen from the south, a south-facing face shows in full, east and
    /// west faces in profile, and a north-facing face hardly at all (only its lip).
    /// </summary>
    public float southShare = 1f;
    public float sideShare = 0.6f;
    public float northShare = 0.15f;
}

/// <summary>
/// A cliff at the foot of a band of steep rock. It is made of a particular rock (granite, marble...): its masked
/// sprite takes that rock's colour, the lichen on its ledges a colour for the biome, and its name includes the rock
/// ("granite cliff"). It is drawn as tall as the ground drops across the band, extending up over the band's bare rock,
/// following RimWorld's convention that tall things rise towards the top of the screen.
/// </summary>
public class Building_Cliff : Building
{
    private static readonly Color DefaultLichen = new Color(0.55f, 0.58f, 0.45f);

    private ThingDef rock;
    private Color lichen = DefaultLichen;
    private float drop;

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

    /// <summary>Metres the ground drops across this cliff's band. Set before spawning.</summary>
    public float Drop
    {
        get => drop;
        set => drop = value;
    }

    public override Color DrawColor
    {
        get => rock?.graphicData?.color ?? base.DrawColor;
        set => base.DrawColor = value;
    }

    public override Color DrawColorTwo => lichen;

    public override string LabelNoCount => rock == null ? base.LabelNoCount : $"{rock.label} {def.label}";

    public override string GetInspectString()
    {
        string text = base.GetInspectString();
        string height = "LL_CliffDrop".Translate(drop.ToString("0"));
        return text.NullOrEmpty() ? height : text + "\n" + height;
    }

    /// <summary>Extra screen height in cells for this cliff's drop and facing.</summary>
    private float ExtraCells()
    {
        var g = def.GetModExtension<CliffGraphicsExtension>() ?? new CliffGraphicsExtension();
        float share = Rotation == Rot4.South ? g.southShare : Rotation == Rot4.North ? g.northShare : g.sideShare;
        return Mathf.Min(drop * g.cellsPerMetre * share, g.maxExtraCells);
    }

    public override void Print(SectionLayer layer)
    {
        Graphic graphic = Graphic;
        Vector2 size = Rotation.IsHorizontal ? graphic.drawSize.Rotated() : graphic.drawSize;
        float extra = ExtraCells();
        size.y += extra;
        // Stretch the sprite upwards (north on screen): its foot stays on the cliff's cell.
        Vector3 center = this.TrueCenter() + new Vector3(0f, 0f, extra * 0.5f);
        bool flip = (Rotation == Rot4.West && graphic.WestFlipped) || (Rotation == Rot4.East && graphic.EastFlipped);
        Printer_Plane.PrintPlane(layer, center, size, graphic.MatAt(Rotation, this), 0f, flip);
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Defs.Look(ref rock, "rock");
        Scribe_Values.Look(ref lichen, "lichen", DefaultLichen);
        Scribe_Values.Look(ref drop, "drop");
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
