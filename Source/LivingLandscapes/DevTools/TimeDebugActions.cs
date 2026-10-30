using LudeonTK;
using RimWorld;
using Verse;

namespace LivingLandscapes.DevTools;

/// <summary>Dev-mode actions for the mod's clock (<see cref="GameComponent_LivingTime"/>).</summary>
public static class TimeDebugActions
{
    [DebugAction("Living Landscapes", "Mod time x10 (toggle)", allowedGameStates = AllowedGameStates.Playing)]
    private static void ToggleTimeScale()
    {
        GameComponent_LivingTime.Scale = GameComponent_LivingTime.Scale > 1 ? 1 : 10;
        Messages.Message($"[LivingLandscapes] Mod time x{GameComponent_LivingTime.Scale}: succession and wild plant regrowth run {(GameComponent_LivingTime.Scale > 1 ? "10 times faster than the game" : "at game speed")}.",
            MessageTypeDefOf.NeutralEvent, historical: false);
    }
}
