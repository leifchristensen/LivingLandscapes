using System;

namespace Verse;

/// <summary>The subset of Verse.FloatRange the linked source files use.</summary>
public struct FloatRange
{
    public float min, max;

    public FloatRange(float min, float max)
    {
        this.min = min;
        this.max = max;
    }

    public float ClampToRange(float v) => UnityEngine.Mathf.Clamp(v, min, max);
}
