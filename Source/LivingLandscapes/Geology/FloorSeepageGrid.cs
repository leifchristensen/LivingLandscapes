using LivingLandscapes.Environment;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>
/// Each cell's floor class and its effective seepage S (mm/yr, <see cref="FloorSeepageRules"/>, values in
/// FloorSeepage.xml), read from what lies under it: river alluvium sorted by grain size (LL_Alluvium, LL_GrainSize), else
/// deep soil (LL_SoilDepth), else the rock beneath (the planned rock of the map's beds: carbonate rock is karst). The
/// hollows' water balance takes it (<see cref="Drainage.LineHeight"/>, P23 extended, #149).
/// Open joints are known only inside the landscape generator, so a rock floor here is karst or hardpan; the jointed
/// rock class serves the erosion model's basins.
/// </summary>
public static class FloorSeepageGrid
{
    public static void Fill(EnvironmentContext context, Drainage drainage)
    {
        float[] alluvium = context.Get(EnvironmentLayerDefOf.LL_Alluvium);
        EnvironmentLayerDef grainLayer = DefDatabase<EnvironmentLayerDef>.GetNamedSilentFail("LL_GrainSize");
        float[] grain = grainLayer != null ? context.Get(grainLayer) : null;
        float[] soil = context.Get(EnvironmentLayerDefOf.LL_SoilDepth);
        if (alluvium == null || soil == null)
            return;
        ThingDef[] rock = context.Environment.PlannedRock;
        FloorSeepageDef def = FloorSeepageDef.Current;
        FloorSeepageRules rules = def.Rules;
        int n = alluvium.Length;
        var seepage = new float[n];
        var classes = new int[n];
        for (int i = 0; i < n; i++)
        {
            bool karst = rock != null && i < rock.Length && rock[i] != null && def.karstRockDefNames.Contains(rock[i].defName);
            int floorClass = rules.ClassOf(alluvium[i], grain != null ? grain[i] : 0f, soil[i], karst, 0f);
            classes[i] = floorClass;
            seepage[i] = rules.SeepageOf(floorClass);
        }
        drainage.FloorClass = classes;
        drainage.FloorSeepage = seepage;
    }
}
