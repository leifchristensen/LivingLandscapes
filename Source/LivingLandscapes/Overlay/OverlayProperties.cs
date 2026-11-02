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

/// <summary>One relief class of a contour standard: maps with relief up to <see cref="reliefUpTo"/> use <see cref="interval"/>.</summary>
public class ContourClass
{
    public float reliefUpTo;
    public float interval;
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

    /// <summary>
    /// Contour interval by the map's relief class, as map series fix it (1-2 m on flat ground, 5 m in hills, 10-20 m in
    /// mountains), so line density shows real steepness and maps compare with each other. When set,
    /// <see cref="contourInterval"/> is ignored. Relief is the spread of the middle 96% of cells, so one pit or spike
    /// does not set the class.
    /// </summary>
    public List<ContourClass> contourIntervals = new List<ContourClass>();

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

    /// <summary>
    /// Another layer that sets each cell's opacity: transparent at <see cref="opacityRange"/>'s min, full at its max. For
    /// layers that only mean something where another one is strong, like aspect (only where the ground slopes).
    /// </summary>
    public EnvironmentLayerDef opacityLayer;

    public FloatRange opacityRange = new FloatRange(0f, 1f);

    /// <summary>Opacity (0..1) for a value of <see cref="opacityLayer"/>.</summary>
    public float OpacityAt(float value) => Mathf.InverseLerp(opacityRange.min, opacityRange.max, value);

    public bool IsVectorField => directionLayer != null;

    /// <summary>
    /// The style for one map's value range: relative colour stops become absolute and an automatic contour interval
    /// is picked. Returns this instance when nothing depends on the range.
    /// </summary>
    public OverlayProperties ResolveFor(float[] values, float min, float max)
    {
        if (!relativeColors && contourIntervals.Count == 0)
            return this;
        var resolved = (OverlayProperties)MemberwiseClone();
        if (relativeColors)
        {
            resolved.colors = new List<ColorStop>();
            foreach (ColorStop stop in colors)
                resolved.colors.Add(new ColorStop { value = Mathf.Lerp(min, max, stop.value), color = stop.color });
        }
        if (contourIntervals.Count > 0)
        {
            resolved.contourInterval = IntervalFor(Relief(values, min, max));
            // Index lines sit on round heights; a map with too little relief to cross one labels every line instead.
            float indexStep = resolved.contourInterval * indexEvery;
            if (Mathf.Floor(max / indexStep) * indexStep < min)
                resolved.indexEvery = 1;
        }
        return resolved;
    }

    private float IntervalFor(float relief)
    {
        foreach (ContourClass c in contourIntervals)
        {
            if (relief <= c.reliefUpTo)
                return c.interval;
        }
        return contourIntervals[contourIntervals.Count - 1].interval;
    }

    private static float Relief(float[] values, float min, float max)
    {
        if (values == null || values.Length == 0)
            return max - min;
        var sorted = (float[])values.Clone();
        System.Array.Sort(sorted);
        return sorted[(int)(sorted.Length * 0.98f)] - sorted[(int)(sorted.Length * 0.02f)];
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
        if (banded && contourInterval <= 0f && contourIntervals.Count == 0)
            yield return "overlay is banded but has no contourInterval or contourIntervals.";
        foreach (ContourClass c in contourIntervals)
        {
            if (c.interval <= 0f)
                yield return "overlay contourIntervals has an interval of 0 or less.";
        }
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
