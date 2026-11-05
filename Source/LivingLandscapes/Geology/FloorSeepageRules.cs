namespace LivingLandscapes.Geology;

/// <summary>
/// Which floor class the ground under a closed hollow belongs to, how much its wetted floor seeps, and how far the
/// water table below the floor caps that (P23 extended, #149). Feeds <see cref="WaterBalanceMath.LakeShare"/>:
///
///   A_lake = runoff x rain x catchment / (evaporation - rain + S_eff),   S_eff = S(floor class) x cap(HAND)
///
/// S is the effective seepage through the wetted floor in mm/yr - what a lake bed actually lets through once its own
/// fines have sealed it (colmation), not the saturated conductivity of the material; FloorSeepageDef (XML) documents
/// each value. The cap is what keeps deserts unchanged: ground whose water table lies deep below the floor
/// (high HAND, height above drainage) is unsaturated and passes almost nothing.
///
/// Pure arithmetic, no game types: the same rules serve the erosion model's basins and the map's hollows.
/// </summary>
public class FloorSeepageRules
{
    // Floor classes, the order of the XML S table (FloorSeepageDef).
    public const int ClayHardpan = 0, Silt = 1, JointedRock = 2, Sand = 3, Gravel = 4, Karst = 5;

    /// <summary>Names of the floor classes for reports, same order as the class indices and <see cref="Seepage"/>.</summary>
    public string[] ClassNames = { "clay/hardpan", "silt", "jointed rock", "sand", "gravel", "karst" };

    /// <summary>Effective seepage through the wetted floor (mm/yr) per class, same order as ClassNames.</summary>
    public float[] Seepage = { 0f, 20f, 600f, 1500f, 8000f, 20000f };

    /// <summary>LL_GrainSize under this is fine alluvium (a silt floor); under GrainGravel it is sand, over it gravel.</summary>
    public float grainSilt = 0.25f;
    public float grainGravel = 0.7f;

    /// <summary>Soil depth (m, LL_SoilDepth) that counts as deep ground: weathered silt, not bedrock.</summary>
    public float soilSiltDepth = 1f;

    /// <summary>Open joint (RockStrata Site.joint, 0..1) at or above which a rock floor counts as jointed.</summary>
    public float jointOpen = 0.5f;

    /// <summary>HAND (m) at which the floor stands dry: unsaturated ground passes nothing (the desert guard).</summary>
    public float handDry = 25f;

    /// <summary>
    /// The floor class of a tile, from what lies under it: river-laid alluvium sorted by grain size, else deep soil,
    /// else the rock beneath (carbonate rock -> karst, open joints -> jointed rock, else massive hardpan). Alluvium
    /// and soil seal what lies under them, so they win over the rock classes.
    /// </summary>
    public int ClassOf(float alluvium, float grainSize, float soilDepth, bool karst, float joint)
    {
        if (alluvium > 0f)
            return grainSize < grainSilt ? Silt : grainSize <= grainGravel ? Sand : Gravel;
        if (soilDepth >= soilSiltDepth) return Silt;
        if (karst) return Karst;
        if (joint >= jointOpen) return JointedRock;
        return ClayHardpan;
    }

    /// <summary>Effective seepage of the class (mm/yr), before the water-table cap.</summary>
    public float SeepageOf(int floorClass) => Seepage[floorClass];

    /// <summary>
    /// How far the water table below the floor (HAND, m) lets the floor seep: full at the floor, nothing once the
    /// unsaturated zone is handDry thick - a vadose zone that thick holds its water against gravity.
    /// </summary>
    public float Cap(float hand)
    {
        if (hand <= 0f) return 1f;
        if (hand >= handDry) return 0f;
        return 1f - hand / handDry;
    }

    /// <summary>The seepage the balance uses: the class's S, capped by how far the water table lies below the floor.</summary>
    public float SeepageEff(int floorClass, float hand) => Seepage[floorClass] * Cap(hand);
}
