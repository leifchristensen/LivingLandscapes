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
    public enum Overlay { None, Moisture, Soil, Elevation }

    /// <summary>Elevation is stored as 16-bit over ElevationMin..ElevationMax; one byte would terrace slopes.
    /// Vanilla values go below zero (water-carving mutators), so the range must start below 0.</summary>
    private const float ElevationMin = -1f;
    private const float ElevationMax = 2f;

    private static float DecodeElevation(ushort v) => ElevationMin + v / 65535f * (ElevationMax - ElevationMin);

    private static ushort EncodeElevation(float v) =>
        (ushort)Mathf.RoundToInt(Mathf.InverseLerp(ElevationMin, ElevationMax, v) * 65535f);

    private byte[] moisture;
    private byte[] soil;
    private ushort[] elevation;

    private Overlay overlay;
    private CellBoolDrawer drawer;
    private ContourLines contours;

    public EnvironmentFields(Map map) : base(map) { }

    public bool HasFields => moisture != null && soil != null;

    public bool HasElevation => elevation != null;

    public float ElevationAt(IntVec3 c)
    {
        return elevation == null ? 0f : DecodeElevation(elevation[map.cellIndices.CellToIndex(c)]);
    }

    /// <summary>Stores a copy of vanilla's generator elevation grid, which is disposed once map generation ends.</summary>
    public void SetElevation(float[] elevationField)
    {
        elevation = new ushort[elevationField.Length];
        for (int i = 0; i < elevationField.Length; i++)
            elevation[i] = EncodeElevation(elevationField[i]);
        contours = null;
    }

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

        byte[] elevationBytes = null;
        if (Scribe.mode == LoadSaveMode.Saving && elevation != null)
        {
            elevationBytes = new byte[elevation.Length * sizeof(ushort)];
            Buffer.BlockCopy(elevation, 0, elevationBytes, 0, elevationBytes.Length);
        }
        DataExposeUtility.LookByteArray(ref elevationBytes, "elevation");
        if (Scribe.mode == LoadSaveMode.LoadingVars && elevationBytes != null)
        {
            elevation = new ushort[elevationBytes.Length / sizeof(ushort)];
            Buffer.BlockCopy(elevationBytes, 0, elevation, 0, elevationBytes.Length);
        }
    }

    public void CycleOverlay()
    {
        int count = Enum.GetValues(typeof(Overlay)).Length;
        do
        {
            overlay = (Overlay)(((int)overlay + 1) % count);
        }
        while (overlay == Overlay.Elevation && !HasElevation);
        drawer ??= new CellBoolDrawer(new OverlayGiver(this), map.Size.x, map.Size.z);
        drawer.SetDirty();
        Messages.Message($"[LivingLandscapes] Overlay: {overlay}", MessageTypeDefOf.SilentInput, historical: false);
    }

    public override void MapComponentUpdate()
    {
        if (overlay == Overlay.None || map != Find.CurrentMap)
            return;
        if (overlay == Overlay.Elevation)
        {
            DrawContours();
            return;
        }
        if (drawer == null || !HasFields)
            return;
        drawer.MarkForDraw();
        drawer.CellBoolDrawerUpdate();
    }

    public override void MapComponentOnGUI()
    {
        if (overlay == Overlay.Elevation && map == Find.CurrentMap && Event.current.type == EventType.Repaint)
            contours?.DrawLabels();
    }

    private void DrawContours()
    {
        if (elevation == null)
            return;
        if (contours == null)
        {
            var heights = new float[elevation.Length];
            for (int i = 0; i < heights.Length; i++)
                heights[i] = DecodeElevation(elevation[i]);
            contours = new ContourLines();
            contours.Build(heights, map.Size.x, map.Size.z);
            Log.Message($"[LivingLandscapes] Built {contours.Count} contour segments and {contours.ExtremaCount} extrema for {map}.");
        }
        contours.Draw();
    }

    private class OverlayGiver : ICellBoolGiver
    {
        private readonly EnvironmentFields owner;

        public OverlayGiver(EnvironmentFields owner) { this.owner = owner; }

        public Color Color => Color.white;

        public bool GetCellBool(int index) => owner.overlay != Overlay.None && owner.overlay != Overlay.Elevation;

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
