using System;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Fields;

/// <summary>
/// Per-map environment fields (0..1, one byte per cell). Computed once at generation, saved with the map.
/// A missing field reads as "unknown" so callers fall back to neutral scoring (old saves, suppressed plant steps).
/// </summary>
public class EnvironmentFields : MapComponent
{
    public enum Overlay { None, Moisture, Soil }

    private byte[] moisture;
    private byte[] soil;

    private Overlay overlay;
    private CellBoolDrawer drawer;

    public EnvironmentFields(Map map) : base(map) { }

    public bool HasFields => moisture != null && soil != null;

    public float MoistureAt(IntVec3 c) => Read(moisture, c);

    public float SoilAt(IntVec3 c) => Read(soil, c);

    private float Read(byte[] field, IntVec3 c)
    {
        return field == null ? 0.5f : field[map.cellIndices.CellToIndex(c)] / 255f;
    }

    /// <summary>Replaces the stored fields with freshly computed ones.</summary>
    public void Set(float[] moistureField, float[] soilField)
    {
        moisture = Quantize(moistureField);
        soil = Quantize(soilField);
        drawer?.SetDirty();
    }

    private static byte[] Quantize(float[] field)
    {
        var bytes = new byte[field.Length];
        for (int i = 0; i < field.Length; i++)
            bytes[i] = (byte)Mathf.RoundToInt(Mathf.Clamp01(field[i]) * 255f);
        return bytes;
    }

    public override void ExposeData()
    {
        DataExposeUtility.LookByteArray(ref moisture, "moisture");
        DataExposeUtility.LookByteArray(ref soil, "soil");
    }

    public void CycleOverlay()
    {
        overlay = (Overlay)(((int)overlay + 1) % Enum.GetValues(typeof(Overlay)).Length);
        drawer ??= new CellBoolDrawer(new OverlayGiver(this), map.Size.x, map.Size.z);
        drawer.SetDirty();
        Messages.Message($"[LivingLandscapes] Overlay: {overlay}", MessageTypeDefOf.SilentInput, historical: false);
    }

    public override void MapComponentUpdate()
    {
        if (overlay == Overlay.None || drawer == null || !HasFields)
            return;
        drawer.MarkForDraw();
        drawer.CellBoolDrawerUpdate();
    }

    private class OverlayGiver : ICellBoolGiver
    {
        private readonly EnvironmentFields owner;

        public OverlayGiver(EnvironmentFields owner) { this.owner = owner; }

        public Color Color => Color.white;

        public bool GetCellBool(int index) => owner.overlay != Overlay.None;

        public Color GetCellExtraColor(int index)
        {
            switch (owner.overlay)
            {
                case Overlay.Moisture:
                    // dry sand -> wet blue
                    return Color.Lerp(new Color(0.85f, 0.7f, 0.35f), new Color(0.1f, 0.35f, 0.9f), owner.moisture[index] / 255f);
                case Overlay.Soil:
                    // poor grey -> rich green
                    return Color.Lerp(new Color(0.55f, 0.55f, 0.55f), new Color(0.1f, 0.65f, 0.15f), owner.soil[index] / 255f);
                default:
                    return Color.clear;
            }
        }
    }
}

public static class EnvironmentFieldsDebugActions
{
    [DebugAction("Living Landscapes", "Cycle environment overlay", allowedGameStates = AllowedGameStates.IsCurrentlyOnMap)]
    private static void CycleOverlay()
    {
        var fields = Find.CurrentMap?.GetComponent<EnvironmentFields>();
        if (fields == null || !fields.HasFields)
        {
            Messages.Message("[LivingLandscapes] This map has no environment fields (generated without the mod, or plants step skipped).", MessageTypeDefOf.RejectInput, historical: false);
            return;
        }
        fields.CycleOverlay();
    }
}
