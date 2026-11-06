using System;

namespace LivingLandscapes.Geology;

/// <summary>
/// The sea as base level, in numbers (P1; game-free, unit-tested). The erosion model grades every river and slope to the
/// sea, so the land beside the shore is already the model's own: this class holds the few conversions that keep it so.
/// <list type="bullet">
/// <item>The model's base level (its height 0) has one height in the map, the sea level, the same for the whole coast.</item>
/// <item>The regional slope of a coast map rises from the sea (the land climbs away from its base level), not along a plane
/// that would put some of the shore above the sea and some below it.</item>
/// <item>Only the sea floor is written afterwards (bathymetry, P10 waves' shelf profile); no land height is touched.</item>
/// </list>
/// </summary>
public static class CoastMath
{
    /// <summary>Metres the sea floor lies below the sea level at the waterline (the first sea cell beside the land).</summary>
    public const float FloorBelowShore = 0.3f;

    /// <summary>
    /// A model height (the erosion model's own units, 0 at its base level) as metres in the map: the tile's elevation plus its
    /// relief spread over the model's range [<paramref name="min"/>, <paramref name="max"/>], the tile's typical ground at
    /// <paramref name="level"/> of that range.
    /// </summary>
    public static float ModelToMetres(float modelHeight, float min, float max, float elevation, float level, float relief)
    {
        return elevation + ((modelHeight - min) / Math.Max(max - min, 1e-6f) - level) * relief;
    }

    /// <summary>
    /// Metres the regional slope lifts ground that lies <paramref name="cellsFromSea"/> cells from the nearest sea. On a coast this
    /// replaces the plane tilted toward the sea, which disagrees with a sea of one level wherever the shore does not run square to
    /// the slope. Across the coastal plain (<paramref name="plainWidth"/> cells) the ground climbs at least
    /// <paramref name="plainGradient"/> metres per cell: a graded plain is not dead level, its rivers keep a lowland slope to the
    /// sea (the water has somewhere to go); beyond it the region's own gradient (metres per metre) times the distance carries on.
    /// </summary>
    public static float RegionalRise(float gradient, float cellMetres, float cellsFromSea, float scale = 1f, float plainWidth = 0f,
        float plainGradient = 0f)
    {
        float onPlain = Math.Min(cellsFromSea, Math.Max(plainWidth, 0f));
        float regional = gradient * cellMetres * scale;
        return Math.Max(regional, plainGradient * scale) * onPlain + regional * (cellsFromSea - onPlain);
    }

    /// <summary>
    /// How much of its uplift the ground keeps this many cells from the sea (0..1): none at the shore, all of it at the coastal
    /// plain's width and beyond. Ground at base level has had the time to grade: a rise just above the sea is worn down by the
    /// waves and the rivers that all run through it, and a passive margin's shelf and plain are one low-relief feature, so the
    /// plain is as wide as the shelf (<see cref="CoastlineExtension.PlainWidthFor"/>). Without it the model's uplift would stand
    /// a ridge beside the sea that 120 steps of erosion cannot wear down, and the shore would be a scarp rather than the
    /// graded plain the rivers cross to the sea.
    /// </summary>
    public static float PlainUplift(float cellsFromSea, float plainWidth)
    {
        float t = Math.Min(1f, Math.Max(0f, cellsFromSea / Math.Max(plainWidth, 1f)));
        return t * t * (3f - 2f * t);
    }

    /// <summary>
    /// Writes the sea floor into <paramref name="height"/> and nothing else: every <paramref name="water"/> cell is set to the
    /// shelf profile (<paramref name="depthAt"/> of its distance from the land in cells) below <paramref name="seaLevel"/>, or,
    /// where <paramref name="stack"/> says resistant rock stands in the sea, to <paramref name="stackHeight"/> x
    /// <paramref name="resistant"/> above it. Land cells are left exactly as they are. Returns the number of sea cells.
    /// </summary>
    public static int ShapeSeaFloor(float[] height, bool[] water, float[] cellsFromLand, float seaLevel, Func<float, float> depthAt,
        Func<int, bool> stack, float[] resistant, float stackHeight)
    {
        int sea = 0;
        for (int i = 0; i < height.Length; i++)
        {
            if (!water[i])
                continue;
            if (stack != null && stack(i))
            {
                height[i] = seaLevel + stackHeight * resistant[i];
                continue;
            }
            height[i] = seaLevel - depthAt(cellsFromLand[i]) - FloorBelowShore;
            sea++;
        }
        return sea;
    }
}
