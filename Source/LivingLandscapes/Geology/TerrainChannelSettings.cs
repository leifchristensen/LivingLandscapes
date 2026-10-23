using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace LivingLandscapes.Geology;

/// <summary>How much of the rain a biome's ground absorbs before water runs off (0..1 of the climate wetness).</summary>
public class BiomeAbsorption
{
    public string biome;
    public float absorption;
}

/// <summary>Channel settings (part of <see cref="GeologyGenerationExtension"/>). Discharge in map cells of runoff.</summary>
public class TerrainChannelSettings
{
    /// <summary>
    /// The ground absorbs water first: runoff per cell is the climate wetness (from rainfall) above what the ground
    /// absorbs, as a share of what is left: (climate - absorption) / (1 - absorption). Absorption by biome (defName);
    /// <see cref="absorption"/> for biomes not listed. Only runoff gathers into channels.
    /// </summary>
    public float absorption = 0.3f;
    public List<BiomeAbsorption> biomeAbsorption = new List<BiomeAbsorption>();

    /// <summary>
    /// Channel heads (Montgomery and Dietrich 1988; Dietrich and Dunne 1993): above them water moves through saturated
    /// ground without banks (seeps, valley-head marsh); a channel begins where the water exceeds what that ground can
    /// carry. The seep's capacity, as the width of channel its discharge would fill, at the map's median channel slope.
    /// Moving water carries only the excess, so it starts one cell wide below a seep and widens as water gathers.
    /// </summary>
    public float seepCapacityWidth = 2f;

    /// <summary>
    /// How the seep's capacity changes with slope: capacity x (median slope / slope)^exponent, within the range. Gentle
    /// valley floors hold wide marshes and need much more water before a channel forms; steep hollows channel sooner
    /// (source area falls with gradient, about A ~ S^-2 in the field data).
    /// </summary>
    public float seepSlopeExponent = 2f;
    public FloatRange seepSlopeFactor = new FloatRange(0.25f, 4f);

    public float AbsorptionOf(string biome)
    {
        float absorbed = absorption;
        foreach (BiomeAbsorption b in biomeAbsorption)
        {
            if (b.biome == biome)
                absorbed = b.absorption;
        }
        return absorbed;
    }

    public float RunoffOf(float climate, string biome)
    {
        float absorbed = AbsorptionOf(biome);
        return Mathf.Clamp01((climate - absorbed) / Mathf.Max(1f - absorbed, 0.05f));
    }

    /// <summary>A channel starts where the discharge (drainage area times climate, plus inflow) reaches this.</summary>
    public float channelDischarge = 1500f;

    /// <summary>
    /// Hydraulic geometry: width = this × √discharge (cells), depth = <see cref="depthPerDischarge"/> × discharge^<see cref="depthExponent"/>
    /// (metres). A world river's inflow is the discharge that gives vanilla's width for its class (compressed to map
    /// scale by <see cref="RiverNetworkSettings.worldRiverWidth"/>), so vanilla's class scales the river, nothing else.
    /// </summary>
    public float widthPerSqrtDischarge = 0.04f;
    public float minWidth = 1f;

    /// <summary>
    /// On a tile without a world river the map's own channels carry the tile's world flow (<see cref="WorldFlow"/>): the
    /// largest gets the share of a creek that flow is (vanilla ends a creek below its degrade threshold), the others keep
    /// their share of it, and none is wider than this (vanilla's creek is 4 cells, 3 at map scale).
    /// </summary>
    public float maxWidthWithoutWorldRiver = 2.5f;

    /// <summary>
    /// Transmission loss: a channel loses water into its bed, this much discharge per cell of length and cell of width,
    /// times the biome's absorption. Small streams shrink and sink into seeps; rivers lose a negligible share. World
    /// rivers keep the width the world gives them.
    /// </summary>
    public float transmissionLoss = 6f;
    public float depthPerDischarge = 0.045f;
    public float depthExponent = 0.3f;
    public float minDepth = 0.15f;

    /// <summary>
    /// Stream power scour: depth is multiplied by (slope / the map's median channel slope)^this, within the range, so
    /// steep reaches scour deep threads and flat reaches shoal.
    /// </summary>
    public float scourExponent = 0.25f;
    public FloatRange scourRange = new FloatRange(0.7f, 1.4f);

    /// <summary>
    /// Noise, as vanilla's river: width varies along the channel, depth in patches (vanilla's "shallowizer"), and the
    /// bank line cell by cell. Amplitudes are shares; wavelengths in cells.
    /// </summary>
    public float widthNoise = 0.15f;
    public float widthNoiseWavelength = 40f;
    public float depthNoise = 0.35f;

    /// <summary>
    /// Shoals (vanilla's "shallowizer"): where a second, slower noise passes the threshold the bed rises toward the
    /// surface, leaving this share of the depth, so wide deep rivers break into deep pools and shallow fords. Only in
    /// channels at least <see cref="minShoalWidth"/> cells wide; wavelength in cells.
    /// </summary>
    public float shoalShare = 0.2f;
    public float shoalThreshold = 0.05f;
    public float shoalWavelength = 26f;
    public float minShoalWidth = 5f;
    public float depthNoiseWavelength = 18f;
    public float bankNoise = 0.2f;
    public float bankNoiseWavelength = 6f;

    /// <summary>Steps up and down a channel over which its flow direction is averaged (D8 steps are 45 degrees apart).</summary>
    public int flowSmoothingSteps = 3;

    /// <summary>
    /// A world river rising on the map (headwater) is fed by springs over this share of the water regime's centre
    /// hollow (<see cref="WaterRegimeDef.centreRadiusShare"/>), so its source is a lake rather than a point.
    /// </summary>
    public float sourceRadiusShare = 0.5f;

    /// <summary>
    /// Relief (metres) left on ground a delta flattens: low swells and swales the distributaries divide around.
    /// Wavelength in cells.
    /// </summary>
    public float deltaApronRelief = 1.5f;
    public float deltaApronWavelength = 24f;

    /// <summary>
    /// Bars (deposition): where a channel's stream power per unit width falls below its average over the reach
    /// upstream (the channel widens or flattens), sediment settles. Share of the drop, times the regimes' deposition
    /// nudge and <see cref="deposition"/>, from <see cref="barThreshold"/> up builds a bar in mid-channel: elongated
    /// along the flow (<see cref="barLengthWidths"/> and <see cref="barWidthShare"/> of the channel), outline warped
    /// by noise, crest from the bed (at the threshold) to <see cref="barRise"/> above the water (at the full drop):
    /// high bars are islands, low ones fords. Only in channels at least <see cref="minBarChannelWidth"/> wide, with
    /// bars at least one bar length apart.
    /// </summary>
    public float deposition = 1f;
    public float barThreshold = 0.35f;

    /// <summary>The in-channel deposit per width at this quantile of the map's channel cells counts as a typical bar-forming deposit.</summary>
    public float barReferenceShare = 0.75f;
    public float barLengthWidths = 2f;
    public float barWidthShare = 0.5f;
    public float barRise = 1.2f;
    public float minBarChannelWidth = 6f;

    /// <summary>A headwater's springs pool where the filled surface lies more than this (metres) above the ground.</summary>
    public float springLakeDepth = 0.1f;

    /// <summary>Ground more than this (metres) above a channel's water surface is not cut into the channel's width.</summary>
    public float maxBankCut = 0.3f;

    /// <summary>
    /// Flow spreading (step 3): how readily water divides where the ground falls away two ways. Higher is more
    /// single-thread; regimes divide it by their spread nudge (braided reaches, deltas and wetlands spread more).
    /// </summary>
    public float spreadExponent = 6f;

    /// <summary>
    /// Confinement field for the channel regimes: the valley floor is ground no higher than this (metres) above the
    /// water, measured across the channel up to this many channel widths each side.
    /// </summary>
    public float valleyFloorHeight = 1.5f;
    public float maxConfinementWidths = 8f;

    /// <summary>
    /// A world river takes the course the erosion model gave it: routing sees a trench this deep (metres) and wide (cells, each side)
    /// along it. On steep ground the valley is already there and this changes nothing; on flat ground, where micro-relief would
    /// steer the river along the map edge or into a hollow, it keeps the river on the way the model drained it.
    /// </summary>
    public float worldCourseTrench = 0.5f;
    public float worldCourseTrenchWidth = 3f;

    /// <summary>
    /// A world river's entry on the map must lie within this cosine of the direction the world says it comes from
    /// (seen from where the rivers meet); otherwise the river enters at the edge facing that neighbour instead.
    /// </summary>
    public float entryAgreement = 0.5f;

    /// <summary>On a coast tile the map edge drains only within this many cells of the sea.</summary>
    public float coastEdgeDrain = 6f;

    /// <summary>The map edge within this many cells of where water enters does not drain (the land beyond slopes in).</summary>
    public float entryGuardCells = 15f;

    /// <summary>
    /// Micro-relief (metres, wavelength in cells) added to the ground before routing water: the same on every call, so
    /// the drainage a map is planned with is the drainage it is built with.
    /// </summary>
    public float flatNoise = 0.08f;
    public float flatNoiseWavelength = 9f;
    public int flatNoiseSeed = 4711;

    /// <summary>Sediment budget and floodplain deposits (<see cref="Sediment"/>).</summary>
    public SedimentSettings sediment = new SedimentSettings();

    /// <summary>Delta distributaries (<see cref="Distributaries"/>).</summary>
    public DistributarySettings distributaries = new DistributarySettings();

    /// <summary>Deltas grown from the mouth (<see cref="DeltaGrowth"/>); when enabled they replace the drawn distributaries.</summary>
    public DeltaGrowthSettings deltaGrowth = new DeltaGrowthSettings();

    /// <summary>Deltas from a solved water surface and sediment balance (<see cref="DeltaHydro"/>); chosen with -ll-delta=hydro.</summary>
    public DeltaHydroSettings deltaHydro = new DeltaHydroSettings();

    /// <summary>Meander planform and oxbows (<see cref="Meanders"/>).</summary>
    public MeanderSettings meander = new MeanderSettings();

    /// <summary>
    /// A world river that runs into a closed hollow fills it with its own load (lakes silt up): the hollow's floor is raised to
    /// this many times the river's depth below its spill level, so the river crosses it as a channel through shallow ground
    /// instead of ending in a deep lake (vanilla's rivers always run through a tile). 0 leaves such lakes as they are.
    /// </summary>
    public float riverInfillDepths = 1.2f;

    /// <summary>Gradient (metres per cell) across filled hollows, so water crosses them toward the spill point.</summary>
    public float fillGradient = 1e-3f;
}
