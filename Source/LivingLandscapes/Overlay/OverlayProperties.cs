using System.Collections.Generic;
using LivingLandscapes.Environment;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Overlay;

/// <summary>One colour stop of a ramp, in the layer's own units.</summary>
public class ColorStop
{
    public float value;
    public Color color = Color.white;
}

/// <summary>
/// How a layer is drawn, GIS style: cells tinted by a colour ramp (optionally in flat hypsometric bands, one per
/// contour interval) plus thin dark contour lines, with every n-th (index) line thicker and labelled.
/// </summary>
public class OverlayProperties
{
    /// <summary>Colour ramp, ordered by value. Values outside the ramp take the end colour.</summary>
    public List<ColorStop> colors = new List<ColorStop>();

    /// <summary>Step between contour lines in layer units. 0 draws no contours.</summary>
    public float contourInterval;

    /// <summary>Every n-th contour is an index line (thicker, labelled).</summary>
    public int indexEvery = 5;

    /// <summary>Fill each band between contours with one flat colour instead of a smooth gradient.</summary>
    public bool banded;

    /// <summary>Contours are only traced up to this value (e.g. to skip the "no water" plateau).</summary>
    public float contourMax = float.MaxValue;

    public Color contourColor = new Color(0.15f, 0.1f, 0.05f, 0.85f);

    public float fillOpacity = 0.55f;

    /// <summary>Number format for legend and contour labels.</summary>
    public string labelFormat = "0.00";

    /// <summary>Colour stop values are fractions (0..1) of the map's own min..max instead of absolute values.
    /// For layers whose absolute range differs between maps, like height in metres.</summary>
    public bool relativeColors;

    /// <summary>When above 0, the contour interval is chosen per map as a round number (1, 2 or 5 x 10^n) giving
    /// about this many contours; <see cref="contourInterval"/> is then ignored.</summary>
    public int autoContourCount;

    /// <summary>
    /// Draw as a vector field instead of shaded cells: arrows point along <see cref="directionLayer"/> (radians
    /// counter-clockwise from east), coloured by this layer's ramp and longer where its value is higher.
    /// </summary>
    public EnvironmentLayerDef directionLayer;

    /// <summary>One arrow every this many cells in each direction.</summary>
    public int arrowSpacing = 3;

    /// <summary>Longest arrow, as a share of <see cref="arrowSpacing"/>.</summary>
    public float arrowScale = 0.9f;

    /// <summary>Texture path of this layer's map toggle button; the generic overlay icon when empty.</summary>
    public string icon;

    public bool IsVectorField => directionLayer != null;

    /// <summary>
    /// The style for one map's value range: relative colour stops become absolute and an automatic contour interval
    /// is picked. Returns this instance when nothing depends on the range.
    /// </summary>
    public OverlayProperties ResolveFor(float min, float max)
    {
        if (!relativeColors && autoContourCount <= 0)
            return this;
        var resolved = (OverlayProperties)MemberwiseClone();
        if (relativeColors)
        {
            resolved.colors = new List<ColorStop>();
            foreach (ColorStop stop in colors)
                resolved.colors.Add(new ColorStop { value = Mathf.Lerp(min, max, stop.value), color = stop.color });
        }
        if (autoContourCount > 0)
            resolved.contourInterval = NiceInterval((max - min) / autoContourCount);
        return resolved;
    }

    private static float NiceInterval(float raw)
    {
        if (raw <= 0f)
            return 1f;
        float magnitude = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(raw)));
        float fraction = raw / magnitude;
        float nice = fraction < 1.5f ? 1f : fraction < 3.5f ? 2f : fraction < 7.5f ? 5f : 10f;
        return nice * magnitude;
    }

    public void ResolveReferences()
    {
        colors.SortBy(s => s.value);
    }

    public IEnumerable<string> ConfigErrors()
    {
        if (colors.Count == 0)
            yield return "overlay has no colors.";
        if (contourInterval < 0f)
            yield return "overlay contourInterval is negative.";
        if (banded && contourInterval <= 0f && autoContourCount <= 0)
            yield return "overlay is banded but has no contourInterval or autoContourCount.";
        if (indexEvery < 1)
            yield return "overlay indexEvery must be at least 1.";
        if (IsVectorField && arrowSpacing < 1)
            yield return "overlay arrowSpacing must be at least 1.";
    }

    /// <summary>Fill colour for a value, flattened to its band when <see cref="banded"/> is set.</summary>
    public Color FillColor(float value)
    {
        if (banded)
            value = (Mathf.Floor(value / contourInterval) + 0.5f) * contourInterval;
        return Evaluate(value);
    }

    public Color Evaluate(float value)
    {
        if (value <= colors[0].value)
            return colors[0].color;
        for (int i = 1; i < colors.Count; i++)
        {
            if (value <= colors[i].value)
            {
                ColorStop a = colors[i - 1];
                ColorStop b = colors[i];
                return Color.Lerp(a.color, b.color, Mathf.InverseLerp(a.value, b.value, value));
            }
        }
        return colors[colors.Count - 1].color;
    }

    public bool IsIndexLevel(int level) => level % indexEvery == 0;
}
