using Verse;

namespace LivingLandscapes;

/// <summary>Temporary Def type used to prove XML loading. Delete with 1.7/Defs/SmokeTest.xml.</summary>
public class SmokeTestDef : Def
{
    public string message;
}

/// <summary>Runs after all Defs are loaded. Put your first breakpoint in the static constructor.</summary>
[StaticConstructorOnStartup]
public static class SmokeTest
{
    static SmokeTest()
    {
        var def = DefDatabase<SmokeTestDef>.GetNamedSilentFail("LL_SmokeTest");
        if (def == null)
            Log.Error("[LivingLandscapes] Smoke test FAILED: LL_SmokeTest def not found. Is 1.7/Defs being loaded?");
        else
            Log.Message($"[LivingLandscapes] Smoke test OK: {def.message}");
    }
}
