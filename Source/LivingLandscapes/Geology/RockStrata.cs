using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace LivingLandscapes.Geology;

/// <summary>
/// How the beds lie on tiles of one hilliness. Most sedimentary land is near-horizontal: outcrops then follow the contours
/// and the beds read like a topographic map. Steep tilts (straight stripes that ignore the terrain: hogbacks, flatirons)
/// belong to mountain belts, so only rugged tiles get them, and only sometimes.
/// </summary>
public class StrataByHilliness
{
    public Hilliness hilliness;

    /// <summary>Tilt of the beds in degrees.</summary>
    public FloatRange dip = new FloatRange(0f, 2f);

    /// <summary>Chance of a steeply tilted map instead (an upturned mountain front), and its tilt.</summary>
    public float steepChance;
    public FloatRange steepDip = new FloatRange(20f, 45f);

    /// <summary>
    /// How many beds the map's relief cuts through (each bed's thickness varies about relief / this). More beds make finer
    /// bands; each band then marks a smaller step in height. 0 uses <see cref="RockStrataSettings.bedThickness"/>.
    /// </summary>
    public FloatRange bedsAcrossRelief = FloatRange.Zero;
}

/// <summary>Tuning for <see cref="RockStrata"/>, nested in <see cref="GeologyGenerationExtension"/>. Lengths in map cells.</summary>
public class RockStrataSettings
{
    /// <summary>Thickness of each bed, measured across the bedding (where no hilliness entry sets beds across the relief).</summary>
    public FloatRange bedThickness = new FloatRange(8f, 30f);

    /// <summary>Beds are never thinner than this (cells), however many the relief asks for.</summary>
    public float minBedThickness = 1.5f;

    /// <summary>Tilt of the beds in degrees where no hilliness entry applies; the dip direction is random per map.</summary>
    public FloatRange dip = new FloatRange(5f, 35f);

    /// <summary>Tilt and bed count by the tile's hilliness (<see cref="StrataByHilliness"/>).</summary>
    public List<StrataByHilliness> byHilliness = new List<StrataByHilliness>();

    public StrataByHilliness For(Hilliness hilliness) => byHilliness.Find(s => s.hilliness == hilliness);

    /// <summary>Bends the beds so outcrops are never straight bands: amplitude and wavelength of the warp.</summary>
    public float warpAmplitude = 12f;
    public float warpScale = 60f;

    /// <summary>Two sets of joints (cracks) break ridges into blocks: spacing between joints, width and weakening.</summary>
    public FloatRange jointSpacing = new FloatRange(25f, 45f);
    public float jointWidth = 2f;
    public float jointWeakness = 0.35f;

    /// <summary>Random strength change per bed, so beds of one rock type still differ.</summary>
    public float strengthJitter = 0.15f;

    /// <summary>
    /// Chance that a bed is a weak interbed (shale between sandstone, say) and its strength factor. Alternating hard
    /// and soft beds are what erosion turns into ridges and strike valleys.
    /// </summary>
    public float softBedChance = 0.4f;
    public float softBedFactor = 0.45f;

    /// <summary>Share of the joint network that is open (weakened); the rest is closed, so blocks vary in size.</summary>
    public float openJoints = 0.5f;

    /// <summary>
    /// Exaggerates strength differences in the erosion model (erodibility = (mean / strength) ^ contrast), so hard
    /// and soft beds make features that read at map scale.
    /// </summary>
    public float contrast = 2f;
}

/// <summary>
/// Simplified rock model: every rock is a stack of tilted beds (strike, dip, varying thickness, warped), each made of
/// one of the tile's rock types, weakened along two joint sets. Erosion reads <see cref="Strength"/>; vanilla's rock
/// placement reads <see cref="RockAt"/>, so rock colour shows the beds. Igneous and metamorphic formation styles are a
/// later refinement (Docs/GEOLOGY-RULES.md, section 3).
/// Positions are map cells (may lie outside the map), heights are cells above the map's lowest ground.
/// </summary>
public class RockStrata
{
    private readonly RockStrataSettings settings;
    private readonly Vector2 dipDirection;
    private readonly float sinDip, cosDip;
    private readonly Perlin warpX, warpZ;
    private readonly Vector2[] jointNormals = new Vector2[2];
    private readonly float[] jointSpacings = new float[2];
    private readonly float[] jointOffsets = new float[2];
    private readonly Perlin jointWarp, jointOpen;

    private readonly List<float> bedTops = new List<float>();
    private readonly List<ThingDef> bedRocks = new List<ThingDef>();
    private readonly List<float> bedStrengths = new List<float>();

    /// <summary>Average bed strength, the reference for erodibility.</summary>
    public float MeanStrength { get; }

    public float DipDegrees { get; }

    /// <summary>The range bed thicknesses were drawn from on this map (cells).</summary>
    public FloatRange BedThickness { get; }
    public Vector2 DipDirection => dipDirection;
    public int BedCount => bedTops.Count;

    /// <param name="rocks">The tile's rock types; may be empty (beds then have no rock type).</param>
    /// <param name="extent">Largest distance from the map centre that will be queried, in cells.</param>
    /// <param name="relief">Height range that will be queried, in cells.</param>
    /// <param name="hilliness">The tile's hilliness: how steeply the beds lie and how many the relief cuts (<see cref="StrataByHilliness"/>).</param>
    public RockStrata(RockStrataSettings settings, IList<ThingDef> rocks, Vector2 centre, float extent, float relief, Hilliness hilliness)
    {
        this.settings = settings;
        float angle = Rand.Range(0f, 360f);
        dipDirection = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
        StrataByHilliness lie = settings.For(hilliness);
        DipDegrees = lie == null ? settings.dip.RandomInRange : Rand.Chance(lie.steepChance) ? lie.steepDip.RandomInRange : lie.dip.RandomInRange;
        // Bed thickness: so many beds across the relief that the bands mark steps in height, or the fixed range.
        FloatRange thickness = settings.bedThickness;
        if (lie != null && lie.bedsAcrossRelief.max > 0f)
        {
            float mean = relief / Mathf.Max(1f, lie.bedsAcrossRelief.RandomInRange);
            thickness = new FloatRange(Mathf.Max(settings.minBedThickness, mean * 0.5f), Mathf.Max(settings.minBedThickness, mean * 1.5f));
        }
        BedThickness = thickness;
        sinDip = Mathf.Sin(DipDegrees * Mathf.Deg2Rad);
        cosDip = Mathf.Cos(DipDegrees * Mathf.Deg2Rad);
        double warpFrequency = 1.0 / Mathf.Max(1f, settings.warpScale);
        warpX = new Perlin(warpFrequency, 2.0, 0.5, 3, Rand.Int, QualityMode.Medium);
        warpZ = new Perlin(warpFrequency, 2.0, 0.5, 3, Rand.Int, QualityMode.Medium);
        jointWarp = new Perlin(warpFrequency * 2.0, 2.0, 0.5, 2, Rand.Int, QualityMode.Medium);
        jointOpen = new Perlin(warpFrequency * 1.5, 2.0, 0.5, 2, Rand.Int, QualityMode.Medium);

        // Joint sets: one roughly along the strike, one across it, each a little off square.
        Vector2 strike = new Vector2(-dipDirection.y, dipDirection.x);
        jointNormals[0] = Rotate(dipDirection, Rand.Range(-15f, 15f));
        jointNormals[1] = Rotate(strike, Rand.Range(-15f, 15f));
        for (int s = 0; s < 2; s++)
        {
            jointSpacings[s] = Mathf.Max(4f, settings.jointSpacing.RandomInRange);
            jointOffsets[s] = Rand.Range(0f, jointSpacings[s]);
        }

        // Beds cover every bed coordinate the queries can reach.
        float reach = extent * sinDip + relief * cosDip + settings.warpAmplitude * 2f + thickness.max;
        float origin = Vector2.Dot(centre, dipDirection) * sinDip;
        float top = origin - reach;
        int previous = -1;
        float strengthSum = 0f;
        while (top < origin + reach)
        {
            top += Mathf.Max(settings.minBedThickness, thickness.RandomInRange);
            ThingDef rock = null;
            if (rocks.Count > 0)
            {
                // Neighbouring beds differ in rock type where the tile has more than one.
                int index = Rand.Range(0, rocks.Count);
                if (rocks.Count > 1 && index == previous)
                    index = (index + 1 + Rand.Range(0, rocks.Count - 1)) % rocks.Count;
                previous = index;
                rock = rocks[index];
            }
            float strength = RockProperties.Of(rock).strength + Rand.Range(-1f, 1f) * settings.strengthJitter;
            if (Rand.Chance(settings.softBedChance))
                strength *= settings.softBedFactor;
            strength = Mathf.Clamp(strength, 0.05f, 1f);
            bedTops.Add(top);
            bedRocks.Add(rock);
            bedStrengths.Add(strength);
            strengthSum += strength;
        }
        MeanStrength = strengthSum / Mathf.Max(1, bedTops.Count);
    }

    /// <summary>The height-independent part of a position: where it lies across the bedding, and its joint weakening.</summary>
    public readonly struct Site
    {
        public readonly float across;
        public readonly float joint;

        public Site(float across, float joint)
        {
            this.across = across;
            this.joint = joint;
        }
    }

    /// <summary>Precomputes a position (map cells) so repeated queries at changing heights are cheap.</summary>
    public Site At(float x, float z)
    {
        float wx = x + (float)warpX.GetValue(x, 0.0, z) * settings.warpAmplitude;
        float wz = z + (float)warpZ.GetValue(x, 0.0, z) * settings.warpAmplitude;
        return new Site((wx * dipDirection.x + wz * dipDirection.y) * sinDip, Joint(x, z));
    }

    /// <summary>Strength of the rock at a site and height (cells), 0..1, including joint weakening.</summary>
    public float Strength(Site site, float height) =>
        bedStrengths[BedAt(site, height)] * (1f - settings.jointWeakness * site.joint);

    /// <summary>Erosion multiplier for the stream-power model: 1 for average rock, higher for weak rock.</summary>
    public float Erodibility(Site site, float height) =>
        Mathf.Pow(MeanStrength / Mathf.Max(0.05f, Strength(site, height)), settings.contrast);

    /// <summary>The rock type exposed at a site and height (null if the tile has none).</summary>
    public ThingDef RockAt(Site site, float height) => bedRocks[BedAt(site, height)];

    private int BedAt(Site site, float height)
    {
        // Distance across the bedding: the beds are planes tilted down toward the dip direction.
        float s = site.across + height * cosDip;
        int lo = 0, hi = bedTops.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (bedTops[mid] < s)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }

    /// <summary>1 on a joint, falling to 0 a few joint widths away.</summary>
    private float Joint(float x, float z)
    {
        float wobble = (float)jointWarp.GetValue(x, 0.0, z) * settings.jointWidth * 2f;
        float strongest = 0f;
        for (int s = 0; s < 2; s++)
        {
            float d = x * jointNormals[s].x + z * jointNormals[s].y + jointOffsets[s] + wobble;
            float fromJoint = Mathf.Abs(d - Mathf.Round(d / jointSpacings[s]) * jointSpacings[s]);
            float w = fromJoint / Mathf.Max(0.5f, settings.jointWidth);
            strongest = Mathf.Max(strongest, Mathf.Exp(-w * w));
        }
        // Only part of the network is open: noise above a threshold set by the open share.
        float open = Mathf.Clamp01(((float)jointOpen.GetValue(x, 0.0, z) - (0.5f - settings.openJoints)) * 4f);
        return strongest * open;
    }

    private static Vector2 Rotate(Vector2 v, float degrees)
    {
        float r = degrees * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }
}
