using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace LivingLandscapes.Geology;

public enum LandformShape
{
    /// <summary>A band along a line through the map centre (valleys, ridges). <see cref="LandformExtension.size"/> is its half-width.</summary>
    Axis,
    /// <summary>A transition across a line: "inside" is the side the orientation points to (mountain fronts, escarpments).</summary>
    Ramp,
    /// <summary>A round area (plateaus, domes, craters). <see cref="LandformExtension.size"/> is its radius.</summary>
    Disk,
    /// <summary>A band along a half-line from a point (a river valley from where rivers meet to the map edge). <see cref="LandformExtension.size"/> is its half-width.</summary>
    Ray,
}

public enum LandformOrientation
{
    /// <summary>Along or towards the direction the region slopes down (lower neighbours, the sea).</summary>
    Downhill,
    /// <summary>Away from the downhill direction.</summary>
    Upslope,
    /// <summary>Across the downhill direction.</summary>
    Across,
    Random,
}

/// <summary>
/// What a tile mutator means geologically, attached to its TileMutatorDef in XML. With geological terrain on, the
/// landscape model includes the landform (as uplift and rock hardness) and the mutator's own elevation rewrite is
/// skipped, so the landform is eroded and drained like the rest of the terrain. Mutators without this extension run
/// as in vanilla. Positions and sizes are fractions of the map width, measured from the map centre.
/// </summary>
public class LandformExtension : DefModExtension
{
    public LandformShape shape = LandformShape.Axis;
    public LandformOrientation orientation = LandformOrientation.Downhill;

    /// <summary>Shift of the shape from the map centre along its orientation.</summary>
    public float offset;

    /// <summary>Half-width (axis), transition width (ramp) or radius (disk).</summary>
    public float size = 0.15f;

    /// <summary>Softness of the shape's edge, as a fraction of the map width.</summary>
    public float edge = 0.05f;

    /// <summary>Change in rock uplift inside and outside the shape (1 = the strongest background uplift).</summary>
    public float upliftInside;
    public float upliftOutside;

    /// <summary>Rock hardness inside the shape: below 1 resists erosion (caprock), above 1 erodes easily.</summary>
    public float erodibilityInside = 1f;

    /// <summary>Irregularity of the shape's outline, as a fraction of the map width.</summary>
    public float warp = 0.05f;

    /// <summary>Skip the mutator's own elevation rewrite when geological terrain is on.</summary>
    public bool replacesVanillaElevation = true;
}

/// <summary>A tile's landforms, resolved to directions for one map.</summary>
public class Landforms
{
    private readonly List<(LandformExtension form, Vector2 direction, Vector2 origin)> forms = new List<(LandformExtension, Vector2, Vector2)>();
    private readonly Perlin warpX;
    private readonly Perlin warpZ;

    /// <summary>The plains feature this map got (<see cref="PlainsFeatureDef"/>), or null.</summary>
    public PlainsFeatureDef PlainsFeature { get; }

    /// <summary>Metres the plains feature adds to the map's relief (0 without one).</summary>
    public float ExtraRelief { get; }

    /// <param name="riverValley">The valley world rivers run in: laid as rays from the river centre (see <see cref="WorldContext.RiverCentre"/>).</param>
    public Landforms(Map map, WorldContext world, LandformExtension riverValley = null)
    {
        warpX = new Perlin(4.0, 2.0, 0.5, 3, Rand.Int, QualityMode.Medium);
        warpZ = new Perlin(4.0, 2.0, 0.5, 3, Rand.Int, QualityMode.Medium);
        foreach (TileMutatorDef mutator in map.TileInfo.Mutators)
        {
            var form = mutator.GetModExtension<LandformExtension>();
            if (form != null)
                forms.Add((form, Direction(form.orientation, world), Vector2.zero));
        }
        // World rivers: a valley from where the rivers meet toward each river's side of the map, and one toward the exit.
        if (riverValley != null && world.HasRiver)
        {
            var ray = new LandformExtension
            {
                shape = LandformShape.Ray, size = riverValley.size, edge = riverValley.edge, upliftInside = riverValley.upliftInside,
                upliftOutside = riverValley.upliftOutside, erodibilityInside = riverValley.erodibilityInside, warp = riverValley.warp,
            };
            foreach (WorldContext.RiverInflow inflow in world.RiverInflows)
                forms.Add((ray, inflow.From, world.RiverCentre));
            forms.Add((ray, world.RiverExit, world.RiverCentre));
        }
        // Low country gets its rock from one plains feature: remnant hills, an escarpment or river bluffs.
        PlainsFeature = PlainsFeatureDef.For(map, world);
        if (PlainsFeature != null)
        {
            PlainsFeatureScale scale = PlainsFeature.ScaleFor(world.Hilliness);
            ExtraRelief = (scale?.extraRelief ?? PlainsFeature.extraRelief).RandomInRange;
            if (PlainsFeature.alongRivers)
            {
                LandformExtension bluff = PlainsFeature.Draw(world.Hilliness);
                bluff.shape = LandformShape.Ray;
                foreach (WorldContext.RiverInflow inflow in world.RiverInflows)
                    forms.Add((bluff, inflow.From, world.RiverCentre));
                forms.Add((bluff, world.RiverExit, world.RiverCentre));
            }
            else
            {
                int n = (scale?.count ?? PlainsFeature.count).RandomInRange;
                for (int k = 0; k < n; k++)
                {
                    LandformExtension form = PlainsFeature.Draw(world.Hilliness);
                    forms.Add((form, Direction(form.orientation, world), Vector2.zero));
                }
            }
        }
    }

    public bool Any => forms.Count > 0;

    private static Vector2 Direction(LandformOrientation orientation, WorldContext world)
    {
        Vector2 downhill = world.Downhill != Vector2.zero ? world.Downhill : WorldContext.Heading(Rand.Range(0f, 360f));
        switch (orientation)
        {
            case LandformOrientation.Upslope: return -downhill;
            case LandformOrientation.Across: return new Vector2(-downhill.y, downhill.x);
            case LandformOrientation.Random: return WorldContext.Heading(Rand.Range(0f, 360f));
            default: return downhill;
        }
    }

    /// <summary>Adjusts uplift and erodibility at a point given in map-width fractions from the map centre.</summary>
    public void Apply(Vector2 p, ref float uplift, ref float erodibility)
    {
        foreach ((LandformExtension form, Vector2 dir, Vector2 origin) in forms)
        {
            Vector2 q = p + form.warp * new Vector2((float)warpX.GetValue(p.x, 0.0, p.y), (float)warpZ.GetValue(p.x, 0.0, p.y)) - origin;
            float inside = Inside(form, dir, q);
            uplift += form.upliftInside * inside + form.upliftOutside * (1f - inside);
            erodibility *= Mathf.Lerp(1f, form.erodibilityInside, inside);
        }
    }

    /// <summary>0 outside the shape, 1 inside, smooth across its edge.</summary>
    private static float Inside(LandformExtension form, Vector2 dir, Vector2 q)
    {
        float edge = Mathf.Max(form.edge, 1e-4f);
        switch (form.shape)
        {
            case LandformShape.Axis:
            {
                // Axis runs along dir, shifted sideways by offset.
                var normal = new Vector2(-dir.y, dir.x);
                float distance = Mathf.Abs(Vector2.Dot(q, normal) - form.offset);
                return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(form.size - edge, form.size + edge, distance));
            }
            case LandformShape.Ray:
            {
                // Along the half-line from the origin, with a round end at the origin.
                float along = Vector2.Dot(q, dir);
                float distance = along < 0f ? q.magnitude : Mathf.Abs(Vector2.Dot(q, new Vector2(-dir.y, dir.x)));
                return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(form.size - edge, form.size + edge, distance));
            }
            case LandformShape.Ramp:
            {
                float along = Vector2.Dot(q, dir) - form.offset;
                return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-form.size - edge, form.size + edge, along));
            }
            default:
            {
                float distance = (q - dir * form.offset).magnitude;
                return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(form.size - edge, form.size + edge, distance));
            }
        }
    }
}
