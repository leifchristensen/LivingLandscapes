using System.Linq;
using HarmonyLib;
using Verse;

namespace LivingLandscapes;

/// <summary>Entry point. RimWorld constructs this once, early, when the mod's assembly loads.</summary>
public class LivingLandscapesMod : Mod
{
    public const string PackageId = "leifchristensen.livinglandscapes";

    public LivingLandscapesMod(ModContentPack content) : base(content)
    {
        var harmony = new Harmony(PackageId);
        harmony.PatchAll(typeof(LivingLandscapesMod).Assembly);

        var harmonyVersion = typeof(Harmony).Assembly.GetName().Version;
        Log.Message($"[LivingLandscapes] Loaded. Harmony {harmonyVersion}, {harmony.GetPatchedMethods().Count()} method(s) patched.");
    }
}
