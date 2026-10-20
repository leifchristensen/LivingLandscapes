using System.Collections.Generic;
using LivingLandscapes.Environment;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>
/// One kind of loose material laid on the ground where its conditions hold: rock chunks and stones on gravel bars, and so on.
/// Conditions test environment layers by defName (as ground rules do) plus the cell's terrain.
/// </summary>
public class GroundScatterRule
{
    public string label;
    public List<FieldCondition> conditions = new List<FieldCondition>();

    /// <summary>Only on these terrains (defNames, or "Gravel" for the biome's gravel); empty means any dry ground.</summary>
    public List<string> terrains = new List<string>();

    /// <summary>
    /// What to place: a ThingDef by defName, looked up when used, so things from optional mods (Scattered Stones'
    /// <c>Owl_Filth_Rocks</c>) are skipped when the mod is absent. "LocalRockChunk" places a chunk of the nearest rock.
    /// </summary>
    public string thing;

    /// <summary>Chance per qualifying cell.</summary>
    public float chance = 0.1f;

    /// <summary>Tint the thing with the colour of the nearest rock (for stone clutter).</summary>
    public bool tintWithRock;

    /// <summary>Only in these biomes (defNames); empty means any.</summary>
    public List<string> biomes = new List<string>();

    /// <summary>
    /// Fields rather than a sprinkle: only where low-frequency noise (wavelength in cells) lies in the top
    /// <see cref="patchShare"/> of its range. 0 scale: everywhere the conditions hold.
    /// </summary>
    public float patchScale;
    public float patchShare = 0.3f;
}

/// <summary>The scatter rules (<c>1.7/Defs/GroundRules/GroundScatter.xml</c>), applied once after plants are placed.</summary>
public class GroundScatterDef : Def
{
    public List<GroundScatterRule> rules = new List<GroundScatterRule>();
}

public static class GroundScatter
{
    public const string LocalRockChunk = "LocalRockChunk";

    private const int RockSearchRadius = 30;

    public static void Apply(Map map)
    {
        var environment = map.GetComponent<MapComponent_Environment>();
        if (environment == null)
            return;
        var fields = new GroundFields();
        foreach (EnvironmentLayerDef layer in DefDatabase<EnvironmentLayerDef>.AllDefs)
        {
            if (environment.TryGetGrid(layer, out EnvironmentGrid grid))
                fields.Add(layer.defName, grid.ToArray());
        }
        var placed = new List<string>();
        foreach (GroundScatterDef def in DefDatabase<GroundScatterDef>.AllDefs)
        {
            foreach (GroundScatterRule rule in def.rules)
            {
                ThingDef fixedThing = rule.thing == LocalRockChunk ? null : DefDatabase<ThingDef>.GetNamedSilentFail(rule.thing ?? "");
                if (rule.thing != LocalRockChunk && fixedThing == null)
                    continue;
                bool known = rule.conditions.TrueForAll(c => fields.Has(c.field));
                if (!known)
                    continue;
                var patches = rule.patchScale > 0f ? new Verse.Noise.Perlin(1.0 / rule.patchScale, 2.0, 0.5, 3, Rand.Int, Verse.Noise.QualityMode.Medium) : null;
                int count = 0;
                for (int i = 0; i < map.Area; i++)
                {
                    if (!Matches(rule, fields, i) || !Rand.Chance(rule.chance))
                        continue;
                    IntVec3 cell = map.cellIndices.IndexToCell(i);
                    if (rule.biomes.Count > 0 && !rule.biomes.Contains(map.BiomeAt(cell)?.defName))
                        continue;
                    // Perlin is about -1..1: the top share of its range forms the patches.
                    if (patches != null && (float)patches.GetValue(cell.x, 0.0, cell.z) < 1f - 2f * rule.patchShare)
                        continue;
                    TerrainDef terrain = cell.GetTerrain(map);
                    if (terrain == null || terrain.IsWater || !cell.Standable(map) || cell.GetFirstItem(map) != null || cell.GetPlant(map)?.def.plant.IsTree == true)
                        continue;
                    if (rule.terrains.Count > 0 && !OnTerrain(rule, terrain, map.BiomeAt(cell)))
                        continue;
                    ThingDef rock = rule.thing == LocalRockChunk || rule.tintWithRock ? NearestRock(map, cell) : null;
                    ThingDef thing = fixedThing ?? rock?.building?.mineableThing;
                    if (thing == null)
                        continue;
                    Thing spawned = GenSpawn.Spawn(thing, cell, map);
                    if (rule.tintWithRock && rock?.graphicData != null)
                        spawned.DrawColor = rock.graphicData.color;
                    count++;
                }
                if (count > 0)
                    placed.Add($"{count} {rule.label}");
            }
        }
        if (placed.Count > 0)
            Log.Message($"[LivingLandscapes] Ground scatter on {map}: {string.Join(", ", placed)}.");
    }

    private static bool Matches(GroundScatterRule rule, GroundFields fields, int cell)
    {
        foreach (FieldCondition condition in rule.conditions)
        {
            float v = fields.Value(condition.field, cell);
            if (float.IsNaN(v) || !condition.Holds(v))
                return false;
        }
        return true;
    }

    private static bool OnTerrain(GroundScatterRule rule, TerrainDef terrain, BiomeDef biome)
    {
        foreach (string name in rule.terrains)
        {
            if (terrain.defName == name || (name == "Gravel" && terrain == (biome?.gravelTerrain ?? TerrainDefOf.Gravel)))
                return true;
        }
        return false;
    }

    /// <summary>The natural rock nearest the cell (its walls, or the rough floor it left), or the tile's first rock type.</summary>
    private static ThingDef NearestRock(Map map, IntVec3 cell)
    {
        foreach (IntVec3 c in GenRadial.RadialCellsAround(cell, RockSearchRadius, true))
        {
            if (!c.InBounds(map))
                continue;
            Building edifice = c.GetEdifice(map);
            if (edifice != null && edifice.def.building?.isNaturalRock == true && edifice.def.building.mineableThing != null)
                return edifice.def;
        }
        foreach (ThingDef rock in Find.World.NaturalRockTypesIn(map.Tile))
            return rock;
        return null;
    }
}
