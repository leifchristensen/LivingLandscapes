using System.Collections.Generic;
using LivingLandscapes.Environment;
using RimWorld;
using Verse;

namespace LivingLandscapes.Flora;

/// <summary>
/// Stand age in play (P22, tools/Docs/DISTURBANCE-MOSAIC.md): the generated LL_StandAge layer plus the succession years
/// that have passed since (<see cref="PlantEcologyDef.successionSpeed"/> per year of the mod's clock,
/// <see cref="GameComponent_LivingTime"/>), and the cells reset to age 0
/// since by fire or by cutting a tree. Saves the generation tick and the resets; the layer itself is saved with the
/// environment.
/// </summary>
public class MapComponent_StandAge : MapComponent
{
    private int generatedTick = -1;
    private Dictionary<int, int> resets = new Dictionary<int, int>();

    private float[] generated;
    private int[] resetTicks;
    private bool loaded;

    public MapComponent_StandAge(Map map) : base(map)
    {
    }

    public override void FinalizeInit()
    {
        base.FinalizeInit();
        // A new map, or a save from before stand age existed: the layer's ages count from now.
        if (generatedTick < 0)
            generatedTick = GameComponent_LivingTime.Ticks;
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref generatedTick, "generatedTick", -1);
        Scribe_Collections.Look(ref resets, "resets", LookMode.Value, LookMode.Value);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            resets ??= new Dictionary<int, int>();
            loaded = false;
        }
    }

    /// <summary>Succession years since the last stand-replacing disturbance on a cell; very old where the map has no stand age layer.</summary>
    public float Age(int index)
    {
        if (!loaded)
            Load();
        if (generated == null)
            return float.MaxValue;
        int now = GameComponent_LivingTime.Ticks;
        int since = resetTicks[index] >= 0 ? resetTicks[index] : generatedTick < 0 ? now : generatedTick;
        float years = (now - since) / (float)GenDate.TicksPerYear * PlantEcologyDef.Current.successionSpeed;
        return resetTicks[index] >= 0 ? years : generated[index] + years;
    }

    /// <summary>A stand-replacing disturbance on a cell (fire, a tree cut down): its stand starts again at age 0.</summary>
    public void Reset(IntVec3 cell)
    {
        if (!cell.InBounds(map))
            return;
        if (!loaded)
            Load();
        int index = map.cellIndices.CellToIndex(cell), now = GameComponent_LivingTime.Ticks;
        resets[index] = now;
        if (resetTicks != null)
            resetTicks[index] = now;
    }

    private int nextAttempt;

    /// <summary>The environment layers have just been built or loaded: look for the stand age layer again now.</summary>
    public void LayersReady()
    {
        loaded = false;
        nextAttempt = 0;
    }

    private void Load()
    {
        // Until the layer exists (still generating, or a map without it) look again at most every 2000 ticks.
        int now = GameComponent_LivingTime.Ticks;
        if (now < nextAttempt)
            return;
        nextAttempt = now + 2000;
        var environment = map.GetComponent<MapComponent_Environment>();
        if (environment == null || !environment.TryGetGrid(EnvironmentLayerDefOf.LL_StandAge, out EnvironmentGrid grid))
            return;
        loaded = true;
        generated = grid.ToArray();
        resetTicks = new int[generated.Length];
        for (int i = 0; i < resetTicks.Length; i++)
            resetTicks[i] = -1;
        foreach (KeyValuePair<int, int> reset in resets)
        {
            if (reset.Key >= 0 && reset.Key < resetTicks.Length)
                resetTicks[reset.Key] = reset.Value;
        }
    }
}
