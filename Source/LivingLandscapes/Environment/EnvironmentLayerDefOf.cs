using RimWorld;

namespace LivingLandscapes.Environment;

[DefOf]
public static class EnvironmentLayerDefOf
{
    public static EnvironmentLayerDef LL_Elevation;
    public static EnvironmentLayerDef LL_Height;
    public static EnvironmentLayerDef LL_DrainageArea;
    public static EnvironmentLayerDef LL_Slope;
    public static EnvironmentLayerDef LL_WaterDistance;
    public static EnvironmentLayerDef LL_Moisture;
    public static EnvironmentLayerDef LL_Soil;

    static EnvironmentLayerDefOf()
    {
        DefOfHelper.EnsureInitializedInCtor(typeof(EnvironmentLayerDefOf));
    }
}
