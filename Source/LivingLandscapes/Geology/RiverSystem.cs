using System.Collections.Generic;
using UnityEngine;

namespace LivingLandscapes.Geology;

/// <summary>One channel across the map: its centre line and width, and where it ends.</summary>
public class RiverPlan
{
    /// <summary>Centre line of the main channel, upstream to downstream (map cells; ends may lie beyond the map).</summary>
    public List<Vector2> Main = new List<Vector2>();

    /// <summary>Channel width at each main point (narrower where a side branch takes part of the flow).</summary>
    public List<float> MainWidth = new List<float>();

    public float NominalWidth;

    /// <summary>Index of the channel this one joins at its last point (-1: it leaves the map).</summary>
    public int Parent = -1;

    /// <summary>Drainage area (map cells) the channel brings in from beyond the map; 0 if it starts inside.</summary>
    public float EntryArea;

    public bool WorldRiver;

    /// <summary>Vanilla's width for a world river along this channel (0 for the map's own channels).</summary>
    public float VanillaWidth;
}

/// <summary>The map's channels, larger first, and what carving them left in each cell.</summary>
public class RiverSystem
{
    public WaterRegimeDef Regime;

    public List<RiverPlan> Channels = new List<RiverPlan>();

    /// <summary>Water surface height in each cell of a live channel (NaN elsewhere), set when the channels are carved.</summary>
    public float[] WaterLevel;

    /// <summary>Downstream direction (unit) in each cell of a live channel, set when the channels are carved.</summary>
    public Vector2[] Flow;

    /// <summary>Channel index in each cell of a live channel (-1 elsewhere).</summary>
    public int[] ChannelOf;

    /// <summary>Cells of a channel's still water (a source lake): no current.</summary>
    public bool[] Still;

    /// <summary>
    /// Drainage lines whose water the ground absorbs before it can run as a channel at least two cells wide: seeps,
    /// saturated ground (marsh, mud) rather than moving water.
    /// </summary>
    public bool[] Seep;

    /// <summary>How close each seep comes to running water, 0..1 (its width as a share of moving water's); 0 elsewhere.</summary>
    public float[] SeepWater;

    /// <summary>Alluvium (metres) the channels deposited, per cell (<see cref="Sediment"/>).</summary>
    public float[] Alluvium;

    /// <summary>Grain size of that alluvium, 0 clay and silt to 1 gravel (0 where there is none).</summary>
    public float[] GrainSize;


    public RiverPlan WorldRiver => Channels.Find(c => c.WorldRiver);
}

/// <summary>Cell index of a map position (clamped to the map).</summary>
public static class RiverGrid
{
    public static int Index(Vector2 v, int sizeX, int sizeZ) =>
        Mathf.Clamp((int)v.y, 0, sizeZ - 1) * sizeX + Mathf.Clamp((int)v.x, 0, sizeX - 1);
}
