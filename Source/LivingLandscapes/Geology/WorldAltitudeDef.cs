using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace LivingLandscapes.Geology;

/// <summary>
/// Settings of the world altitude model (<see cref="WorldAltitudeMath"/>, #201): the height each hilliness stands for and
/// how firmly a tile holds it, and the continent's slow uplift. Heights are metres above sea level.
/// </summary>
public class WorldAltitudeDef : Def
{
    /// <summary>Turns the model off: tiles then read vanilla's elevation.</summary>
    public bool enabled = true;

    /// <summary>Continental uplift c, metres per tile squared: a plain rises c * r^2 / 2 at r tiles from the sea.</summary>
    public float continentalUplift = 2f;

    /// <summary>Least fall along a world river from one tile to the next downstream, metres (the field is lifted upstream to keep it).</summary>
    public float riverFall = 1f;

    /// <summary>
    /// Whether a tile's temperature follows our altitude (<see cref="WorldAltitude.TemperatureOffset"/>). Vanilla bakes
    /// the cooling of its own elevation into <c>Tile.temperature</c> when it makes the world; with our altitude in its
    /// place the climate (and the plants that grow in it) must follow the altitude the map shows.
    /// </summary>
    public bool temperatureFollowsAltitude = true;

    /// <summary>Vanilla's cooling with elevation (WorldGenStep_Terrain.TemperatureReductionAtElevation): none below this altitude, metres.</summary>
    public float coolingStartAltitude = 250f;

    /// <summary>... rising linearly to <see cref="maxCooling"/> at this altitude, metres.</summary>
    public float coolingFullAltitude = 5000f;

    /// <summary>... degrees Celsius of cooling at <see cref="coolingFullAltitude"/> (vanilla: 40, about 8.4 K per km).</summary>
    public float maxCooling = 40f;

    public float CoolingAt(float altitude)
        => altitude <= coolingStartAltitude ? 0f : maxCooling * System.Math.Min(1f, (altitude - coolingStartAltitude) / (coolingFullAltitude - coolingStartAltitude));

    /// <summary>World map topology overlay: height of one colour band, metres.</summary>
    public float topologyBand = 250f;

    /// <summary>... altitude at the top of the colour ramp, metres.</summary>
    public float topologyTop = 4500f;

    /// <summary>... opacity of the fill over the world's terrain.</summary>
    public float topologyOpacity = 0.6f;

    /// <summary>
    /// The annual mean temperature each biome keeps, degrees Celsius. Vanilla picked a tile's biome from its own climate
    /// (with the cooling of vanilla's elevation); our altitude can differ from that elevation by a kilometre or more, and a
    /// tile whose climate drifts out of its biome's range reads wrongly (a rainforest at 5 C). The tile's temperature
    /// moves toward the range only as far as needed; one already outside it (vanilla's own) is left as vanilla made it.
    /// Biomes without an entry (modded ones) follow the altitude freely.
    /// </summary>
    public List<BiomeTemperature> biomeTemperatures = new List<BiomeTemperature>();

    public class BiomeTemperature
    {
        public BiomeDef biome;
        public float min;
        public float max;
    }

    public List<HillinessAltitude> hilliness = new List<HillinessAltitude>();

    public class HillinessAltitude
    {
        public Hilliness hilliness;

        /// <summary>The height this ruggedness stands for, metres.</summary>
        public float height;

        /// <summary>How firmly the tile holds it against its neighbours (0: none, it takes what they give).</summary>
        public float stiffness;
    }

    private static WorldAltitudeDef current;
    public static WorldAltitudeDef Current
        => current ??= DefDatabase<WorldAltitudeDef>.GetNamedSilentFail("LL_WorldAltitude") ?? new WorldAltitudeDef { enabled = false };

    public HillinessAltitude For(Hilliness h) => hilliness.Find(x => x.hilliness == h);
}
