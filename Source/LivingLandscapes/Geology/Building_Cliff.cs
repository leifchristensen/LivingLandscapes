using Verse;

namespace LivingLandscapes.Geology;

/// <summary>
/// A terrace cliff band or steps, made of a particular rock (granite, marble...). The greyscale sprite takes that
/// rock's colour, like vanilla rock walls, and the name includes it ("granite cliff").
/// </summary>
public class Building_Cliff : Building
{
    private ThingDef rock;

    /// <summary>The natural rock this cliff is made of. Set before spawning.</summary>
    public ThingDef Rock
    {
        get => rock;
        set => rock = value;
    }

    public override UnityEngine.Color DrawColor
    {
        get => rock?.graphicData?.color ?? base.DrawColor;
        set => base.DrawColor = value;
    }

    public override string LabelNoCount => rock == null ? base.LabelNoCount : $"{rock.label} {def.label}";

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Defs.Look(ref rock, "rock");
    }
}
