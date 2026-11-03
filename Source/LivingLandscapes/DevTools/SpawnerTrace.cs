using Verse;

namespace LivingLandscapes.DevTools;

/// <summary>
/// Dev trace of the wild plant spawner over a watched area (the succession benchmark's block): what happens to each spawn
/// attempt, and how many plants die there. Off (no cost beyond a null check) unless a benchmark sets <see cref="Watch"/>.
/// </summary>
public static class SpawnerTrace
{
    public static System.Predicate<IntVec3> Watch;
    public static int Attempts, Rejected, Saturated, Spawned, Deaths;

    public static bool Watching(IntVec3 c) => Watch != null && Watch(c);

    public static void Reset() => Attempts = Rejected = Saturated = Spawned = Deaths = 0;

    public static string Describe() =>
        $"attempts {Attempts}: refused by acceptance {Rejected}, saturated {Saturated}, spawned {Spawned}, other {Attempts - Rejected - Saturated - Spawned}; deaths {Deaths}";
}
