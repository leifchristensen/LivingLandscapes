using RimWorld;
using Verse;

namespace LivingLandscapes;

/// <summary>
/// The mod's own clock. Every in-game process of the mod that depends on elapsed time (stand age and succession today)
/// reads <see cref="Ticks"/> instead of the game's ticks, so a dev toggle can run the mod's simulation faster than the
/// game (<see cref="Scale"/>) to test scenarios over time. While faster, vanilla's wild plant spawner gets the same
/// extra ticks, so regrowth keeps pace with succession; plant growth stays vanilla's (dev "fast ecology" speeds it).
/// </summary>
public class GameComponent_LivingTime : GameComponent
{
    private double ticks = -1;

    /// <summary>Mod ticks per game tick: 1, or more while the dev toggle is on. Not saved: a loaded game runs at 1.</summary>
    public static int Scale { get; set; } = 1;

    public GameComponent_LivingTime(Game game)
    {
    }

    /// <summary>The mod's ticks now; the game's ticks where there is no game component yet (map generation of a new game).</summary>
    public static int Ticks
    {
        get
        {
            var clock = Current.Game?.GetComponent<GameComponent_LivingTime>();
            if (clock == null)
                return Find.TickManager?.TicksGame ?? 0;
            clock.EnsureStarted();
            return (int)clock.ticks;
        }
    }

    // A new game, or a save from before the clock existed: start at the game's ticks, so times saved in game ticks keep
    // their meaning.
    private void EnsureStarted()
    {
        if (ticks < 0)
            ticks = Find.TickManager?.TicksGame ?? 0;
    }

    /// <summary>Dev benchmarks that advance game time without ticking the game: advance the mod's clock with it.</summary>
    public static void Advance(int gameTicks)
    {
        var clock = Current.Game?.GetComponent<GameComponent_LivingTime>();
        if (clock == null)
            return;
        clock.EnsureStarted();
        clock.ticks += (double)gameTicks * Scale;
    }

    public override void GameComponentTick()
    {
        EnsureStarted();
        ticks += Scale;
        if (Scale <= 1)
            return;
        foreach (Map map in Find.Maps)
        {
            for (int i = 1; i < Scale; i++)
                map.wildPlantSpawner.WildPlantSpawnerTick();
        }
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref ticks, "ticks", -1);
    }
}
