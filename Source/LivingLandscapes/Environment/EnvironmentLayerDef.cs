using System;
using System.Collections.Generic;
using LivingLandscapes.Overlay;
using Verse;

namespace LivingLandscapes.Environment;

/// <summary>Bits used to store one cell of a layer. More bits cost memory and save size, fewer bits terrace slopes.</summary>
public enum LayerPrecision
{
    Byte,
    UShort,
}

/// <summary>
/// One per-cell environment layer (elevation, moisture, soil, ...). The Def declares what the layer is and how it
/// is shown; its worker computes the values once at map generation. Layers are computed in dependency order, so a
/// worker can read every layer listed in <see cref="dependsOn"/>.
/// </summary>
public class EnvironmentLayerDef : Def
{
    public Type workerClass;

    /// <summary>Layers that must be computed before this one.</summary>
    public List<EnvironmentLayerDef> dependsOn = new List<EnvironmentLayerDef>();

    public LayerPrecision precision = LayerPrecision.Byte;

    /// <summary>Unit shown in overlay legends, e.g. "cells". Empty for dimensionless values.</summary>
    public string unit = "";

    /// <summary>How the layer is drawn by the debug overlay. Null hides it from the overlay list.</summary>
    public OverlayProperties overlay;

    [Unsaved]
    private EnvironmentLayerWorker workerInt;

    public EnvironmentLayerWorker Worker
    {
        get
        {
            if (workerInt == null)
            {
                workerInt = (EnvironmentLayerWorker)Activator.CreateInstance(workerClass);
                workerInt.def = this;
            }
            return workerInt;
        }
    }

    public override void ResolveReferences()
    {
        base.ResolveReferences();
        overlay?.ResolveReferences();
    }

    public override IEnumerable<string> ConfigErrors()
    {
        foreach (string error in base.ConfigErrors())
            yield return error;
        if (workerClass == null)
            yield return "workerClass is not set.";
        else if (!typeof(EnvironmentLayerWorker).IsAssignableFrom(workerClass))
            yield return $"workerClass {workerClass} does not derive from {nameof(EnvironmentLayerWorker)}.";
        if (dependsOn.Contains(this))
            yield return "depends on itself.";
        if (overlay != null)
        {
            foreach (string error in overlay.ConfigErrors())
                yield return error;
        }
    }
}
