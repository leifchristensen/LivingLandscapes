using Verse;
using Verse.Noise;

namespace LivingLandscapes.Geology;

/// <summary>
/// Stands in for one of vanilla's rock-type noises (<see cref="RockNoises"/>) during map generation: scores 1 where
/// the geology placed this rock type and 0 elsewhere, so <c>GenStep_RocksFromGrid.RockDefAt</c> and everything that
/// asks it (rock walls, rough stone floors, mutators) follows the rock beds.
/// </summary>
public class StrataRockNoise : ModuleBase
{
    private readonly ThingDef rock;
    private readonly ThingDef[] grid;
    private readonly int sizeX, sizeZ;

    public StrataRockNoise(ThingDef rock, ThingDef[] grid, int sizeX, int sizeZ) : base(0)
    {
        this.rock = rock;
        this.grid = grid;
        this.sizeX = sizeX;
        this.sizeZ = sizeZ;
    }

    public override double GetValue(double x, double y, double z)
    {
        int cx = UnityEngine.Mathf.Clamp((int)x, 0, sizeX - 1);
        int cz = UnityEngine.Mathf.Clamp((int)z, 0, sizeZ - 1);
        return grid[cz * sizeX + cx] == rock ? 1.0 : 0.0;
    }

    /// <summary>Replaces vanilla's rock noises with the geology's rock beds for the map being generated.</summary>
    public static void Apply(Map map, ThingDef[] grid)
    {
        if (grid == null || RockNoises.rockNoises == null)
            return;
        foreach (RockNoises.RockNoise rockNoise in RockNoises.rockNoises)
            rockNoise.noise = new StrataRockNoise(rockNoise.rockDef, grid, map.Size.x, map.Size.z);
    }
}
