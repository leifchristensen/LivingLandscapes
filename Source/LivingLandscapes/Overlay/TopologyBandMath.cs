using System;

namespace LivingLandscapes.Overlay;

/// <summary>
/// Fixed-interval hypsometric bands (#190). Every band is the same height (5 m in the topology layer), so the number
/// of bands and the colour of a band read directly as relief and steepness, and maps compare with each other. Bands
/// count up from the map's own lowest point, so a map always starts on the lowest colour. Pure arithmetic, no game types.
/// </summary>
public static class TopologyBandMath
{
    /// <summary>Colours in the base palette; maps with this many bands or fewer use it as is.</summary>
    public const int PaletteSize = 7;

    /// <summary>Lowest band edge: the multiple of the interval at or below the map minimum, so band edges are the same
    /// heights on every map and coincide with the thick (index) contour lines.</summary>
    public static float Origin(float min, float interval)
    {
        return interval <= 0f ? min : (float)Math.Floor(min / interval) * interval;
    }

    /// <summary>Bands needed to cover min..max: ceil((max - origin) / interval), at least 1 (a flat map is one band).</summary>
    public static int BandCount(float min, float max, float interval)
    {
        if (interval <= 0f || max <= min)
            return 1;
        return Math.Max(1, (int)Math.Ceiling((max - Origin(min, interval)) / interval - 1e-4));
    }

    /// <summary>Band a value falls in, 0 = lowest. <paramref name="min"/> is the band origin. The top edge belongs to the top band.</summary>
    public static int BandIndex(float value, float min, float interval, int count)
    {
        if (interval <= 0f)
            return 0;
        int i = (int)Math.Floor((value - min) / interval);
        return Math.Max(0, Math.Min(count - 1, i));
    }

    /// <summary>
    /// Where band <paramref name="band"/> of <paramref name="count"/> sits on the base palette, as a position
    /// 0..<see cref="PaletteSize"/>-1. Up to 7 bands take palette entries 0, 1, 2 ... in order; more bands keep entry 0 and
    /// entry 6 at the ends and space the rest evenly between, so intermediate colours are interpolated.
    /// </summary>
    public static float PalettePosition(int band, int count)
    {
        if (count <= PaletteSize)
            return band;
        return band * (PaletteSize - 1f) / (count - 1f);
    }
}
