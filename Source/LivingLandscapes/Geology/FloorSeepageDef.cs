using System.Collections.Generic;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>
/// The seepage table of the wetted floor under closed hollows (P23 extended, #149): the effective seepage S (mm/yr)
/// of each floor class, the thresholds that sort ground into a class, and the water-table depth at which a floor
/// stands dry. S is what a lake bed actually lets through once its own fines have sealed it (colmation), not the
/// saturated conductivity of the material - see the table's comments in FloorSeepage.xml. The pure rules built from
/// it (<see cref="FloorSeepageRules"/>) feed <see cref="WaterBalanceMath.LakeShare"/>.
/// </summary>
public class FloorSeepageDef : Def
{
    // Effective seepage through the wetted floor, mm/yr, one per floor class.
    public float seepageClayMmPerYear = 0f;
    public float seepageSiltMmPerYear = 20f;
    public float seepageJointedRockMmPerYear = 600f;
    public float seepageSandMmPerYear = 1500f;
    public float seepageGravelMmPerYear = 8000f;
    public float seepageKarstMmPerYear = 20000f;

    // Sorting thresholds (FloorSeepageRules.ClassOf).
    public float grainSilt = 0.25f;
    public float grainGravel = 0.7f;
    public float soilSiltDepth = 1f;
    public float jointOpen = 0.5f;

    /// <summary>HAND (m, height above drainage) at or above which the floor stands dry and seeps nothing.</summary>
    public float handDry = 25f;

    /// <summary>Rock defNames whose ground weathers to karst (carbonate rock: limestone and its metamorphic marble).</summary>
    public List<string> karstRockDefNames = new List<string> { "Limestone", "Marble" };

    private FloorSeepageRules rules;

    /// <summary>The pure rules built from this table, for the balance and the floor grid.</summary>
    public FloorSeepageRules Rules => rules ??= new FloorSeepageRules
    {
        Seepage = new[]
        {
            seepageClayMmPerYear, seepageSiltMmPerYear, seepageJointedRockMmPerYear,
            seepageSandMmPerYear, seepageGravelMmPerYear, seepageKarstMmPerYear,
        },
        grainSilt = grainSilt,
        grainGravel = grainGravel,
        soilSiltDepth = soilSiltDepth,
        jointOpen = jointOpen,
        handDry = handDry,
    };

    private static FloorSeepageDef current;

    public static FloorSeepageDef Current
        => current ??= DefDatabase<FloorSeepageDef>.GetNamedSilentFail("LL_FloorSeepage") ?? new FloorSeepageDef();
}
