using System.Collections.Generic;
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
        if (banded && contourInterval <= 0f)
            yield return "overlay is banded but has no contourInterval.";
        if (indexEvery < 1)
            yield return "overlay indexEvery must be at least 1.";
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
