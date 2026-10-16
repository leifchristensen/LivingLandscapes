using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>
/// A cliff band or steps on the higher side of a height change, made of a particular rock (granite, marble...).
/// Drawn as thin edges, not a full-cell block: on each side that drops to lower ground (<see cref="LowerSides"/>) a
/// greyscale edge piece tinted with the rock's colour: a short stone face on the south, a thin lip on the north, a
/// thin column on east and west (art: Source/ArtTools/generate_cliff_art.py). Steps draw stairs on their facing side.
/// So stacked cliffs read as closely nested lines whose apparent height matches the drop, and edges along cell sides
/// join up at corners on their own.
/// </summary>
public class Building_Cliff : Building
{
    private const string ArtPath = "Things/Building/LivingLandscapes/Cliff/";
    private static readonly string[] EdgeNames = { "Edge_North", "Edge_East", "Edge_South", "Edge_West" };
    private static readonly string[] StairsNames = { "Stairs_North", "Stairs_East", "Stairs_South", "Stairs_West" };

    private ThingDef rock;
    private byte lowerSides;

    /// <summary>The natural rock this cliff is made of. Set before spawning.</summary>
    public ThingDef Rock
    {
        get => rock;
        set => rock = value;
    }

    /// <summary>Sides that drop to lower ground, as bits by <see cref="Rot4.AsInt"/>. Set before spawning.</summary>
    public byte LowerSides
    {
        get => lowerSides;
        set => lowerSides = value;
    }

    private bool IsSteps => def.passability == Traversability.Standable;

    public override Color DrawColor
    {
        get => rock?.graphicData?.color ?? base.DrawColor;
        set => base.DrawColor = value;
    }

    public override string LabelNoCount => rock == null ? base.LabelNoCount : $"{rock.label} {def.label}";

    public override void Print(SectionLayer layer)
    {
        byte sides = lowerSides != 0 ? lowerSides : (byte)(1 << Rotation.AsInt);
        Vector3 center = Position.ToVector3Shifted();
        center.y = def.Altitude;
        for (int d = 0; d < 4; d++)
        {
            if ((sides & (1 << d)) == 0)
                continue;
            bool stairs = IsSteps && d == Rotation.AsInt;
            Material mat = MaterialPool.MatFrom(ArtPath + (stairs ? StairsNames[d] : EdgeNames[d]), ShaderDatabase.Cutout, DrawColor);
            Printer_Plane.PrintPlane(layer, center, Vector2.one, mat);
        }
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Defs.Look(ref rock, "rock");
        Scribe_Values.Look(ref lowerSides, "lowerSides");
    }
}
