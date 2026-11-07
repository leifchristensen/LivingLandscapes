using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using LivingLandscapes.Geology;
using RimWorld.Planet;

namespace LivingLandscapes.Patches;

/// <summary>
/// The world tile tab shows the altitude our code reads (#201), the same as the world topology overlay and the temperature,
/// not vanilla's <c>Tile.elevation</c>, which stays in the world untouched. Vanilla's line is
/// <c>ws.elevation.ToString("F0")</c>; its read of the field becomes <see cref="WorldAltitude.ShownElevation"/>.
/// </summary>
[HarmonyPatch(typeof(WITab_Terrain), "ListGeometricDetails")]
public static class Patch_WITab_Terrain_ListGeometricDetails
{
    private static readonly FieldInfo Elevation = AccessTools.Field(typeof(Tile), nameof(Tile.elevation));
    private static readonly MethodInfo Shown = AccessTools.Method(typeof(WorldAltitude), nameof(WorldAltitude.ShownElevation));

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        LocalBuilder shown = generator.DeclareLocal(typeof(float));
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.LoadsField(Elevation, byAddress: true))
            {
                // the tile is on the stack; ToString needs the float's address, so keep it in a local
                yield return new CodeInstruction(OpCodes.Call, Shown);
                yield return new CodeInstruction(OpCodes.Stloc, shown);
                yield return new CodeInstruction(OpCodes.Ldloca, shown);
            }
            else if (instruction.LoadsField(Elevation))
                yield return new CodeInstruction(OpCodes.Call, Shown);
            else
                yield return instruction;
        }
    }
}
