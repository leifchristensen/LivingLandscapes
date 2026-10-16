using System;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Environment;

/// <summary>
/// Stored values of one layer on one map, quantised over the map's own min..max so no fixed range can clip them
/// (vanilla elevation, for example, goes below 0 at lakes and above 2 on mountainous tiles).
/// </summary>
public class EnvironmentGrid : IExposable
{
    private LayerPrecision precision;
    private float min;
    private float max;
    private byte[] data;

    /// <summary>For the save loader only.</summary>
    public EnvironmentGrid() { }

    public EnvironmentGrid(float[] values, LayerPrecision precision)
    {
        this.precision = precision;
        min = float.MaxValue;
        max = float.MinValue;
        foreach (float v in values)
        {
            min = Mathf.Min(min, v);
            max = Mathf.Max(max, v);
        }

        int steps = Steps;
        int bytesPerCell = precision == LayerPrecision.UShort ? 2 : 1;
        data = new byte[values.Length * bytesPerCell];
        for (int i = 0; i < values.Length; i++)
        {
            int q = Mathf.RoundToInt(Mathf.InverseLerp(min, max, values[i]) * steps);
            if (bytesPerCell == 2)
            {
                data[i * 2] = (byte)(q & 0xFF);
                data[i * 2 + 1] = (byte)(q >> 8);
            }
            else
            {
                data[i] = (byte)q;
            }
        }
    }

    public float Min => min;

    public float Max => max;

    public int Count => precision == LayerPrecision.UShort ? data.Length / 2 : data.Length;

    private int Steps => precision == LayerPrecision.UShort ? ushort.MaxValue : byte.MaxValue;

    public float this[int index]
    {
        get
        {
            int q = precision == LayerPrecision.UShort ? data[index * 2] | (data[index * 2 + 1] << 8) : data[index];
            return min + (max - min) * q / Steps;
        }
    }

    public float[] ToArray()
    {
        var values = new float[Count];
        for (int i = 0; i < values.Length; i++)
            values[i] = this[i];
        return values;
    }

    public void ExposeData()
    {
        Scribe_Values.Look(ref precision, "precision");
        Scribe_Values.Look(ref min, "min");
        Scribe_Values.Look(ref max, "max");
        DataExposeUtility.LookByteArray(ref data, "data");
        if (Scribe.mode == LoadSaveMode.PostLoadInit && data == null)
            data = Array.Empty<byte>();
    }
}
