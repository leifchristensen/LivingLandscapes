using System.Collections.Generic;
using System.Linq;
using LivingLandscapes.Environment;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Overlay;

/// <summary>
/// Draws one environment layer of a map at a time: a colour-ramp fill (vanilla CellBoolDrawer), baked contour
/// meshes, labels on index contours, and a legend. Display state only; nothing here is saved.
/// </summary>
public class EnvironmentOverlay
{
    private const float MinorWidth = 0.08f;
    private const float IndexWidth = 0.18f;

    /// <summary>One label every this many segments of an index contour.</summary>
    private const int SegmentsPerLabel = 60;

    private const int MaxLabelsOnScreen = 60;

    private const int FillRenderQueue = 3600;
    private const int LineRenderQueue = 3610;

    private readonly MapComponent_Environment environment;

    private EnvironmentLayerDef selected;
    private float[] values;
    private CellBoolDrawer fill;
    private List<Mesh> minorLines;
    private List<Mesh> indexLines;
    private Material lineMaterial;
    private readonly List<(Vector2 pos, string text)> labels = new List<(Vector2, string)>();

    public EnvironmentOverlay(MapComponent_Environment environment)
    {
        this.environment = environment;
    }

    public EnvironmentLayerDef Selected => selected;

    /// <summary>Layers of this map that have overlay settings, in compute order.</summary>
    public IEnumerable<EnvironmentLayerDef> Available =>
        EnvironmentLayerOrder.Sorted.Where(l => l.overlay != null && environment.HasLayer(l));

    public void Select(EnvironmentLayerDef layer)
    {
        selected = layer;
        Rebuild();
        string name = layer?.LabelCap.ToString() ?? "None";
        Messages.Message($"[LivingLandscapes] Overlay: {name}", MessageTypeDefOf.SilentInput, historical: false);
    }

    /// <summary>Steps through None and every available layer.</summary>
    public void Cycle()
    {
        List<EnvironmentLayerDef> options = Available.ToList();
        int index = selected == null ? -1 : options.IndexOf(selected);
        Select(index + 1 < options.Count ? options[index + 1] : null);
    }

    public void Notify_LayersChanged()
    {
        if (selected != null)
            Rebuild();
    }

    private void Rebuild()
    {
        values = null;
        fill = null;
        minorLines = null;
        indexLines = null;
        labels.Clear();
        if (selected == null || !environment.TryGetGrid(selected, out EnvironmentGrid grid))
        {
            selected = null;
            return;
        }

        Map map = environment.Map;
        OverlayProperties style = selected.overlay;
        values = grid.ToArray();
        fill = new CellBoolDrawer(_ => true, () => Color.white, i => style.FillColor(values[i]),
            map.Size.x, map.Size.z, FillRenderQueue, style.fillOpacity);

        List<ContourSegment> segments = ContourTracer.Trace(values, map.Size.x, map.Size.z, style.contourInterval, style.contourMax);
        float altitude = AltitudeLayer.MetaOverlays.AltitudeFor();
        var minor = new LineMeshBuilder(altitude);
        var index = new LineMeshBuilder(altitude);
        var segmentsOnLevel = new Dictionary<int, int>();
        foreach (ContourSegment s in segments)
        {
            if (!style.IsIndexLevel(s.Level))
            {
                minor.Add(s.A, s.B, MinorWidth);
                continue;
            }
            index.Add(s.A, s.B, IndexWidth);
            segmentsOnLevel.TryGetValue(s.Level, out int n);
            if (n % SegmentsPerLabel == SegmentsPerLabel / 2)
                labels.Add(((s.A + s.B) * 0.5f, (s.Level * style.contourInterval).ToString(style.labelFormat)));
            segmentsOnLevel[s.Level] = n + 1;
        }
        minorLines = minor.Finish();
        indexLines = index.Finish();
        lineMaterial = SolidColorMaterials.NewSolidColorMaterial(style.contourColor, ShaderDatabase.SolidColor);
        lineMaterial.renderQueue = LineRenderQueue;
    }

    public void Update()
    {
        if (selected == null || fill == null)
            return;
        fill.MarkForDraw();
        fill.CellBoolDrawerUpdate();
        foreach (Mesh mesh in minorLines)
            Graphics.DrawMesh(mesh, Vector3.zero, Quaternion.identity, lineMaterial, 0);
        foreach (Mesh mesh in indexLines)
            Graphics.DrawMesh(mesh, Vector3.zero, Quaternion.identity, lineMaterial, 0);
    }

    public void OnGUI()
    {
        if (selected == null || Event.current.type != EventType.Repaint)
            return;
        DrawLabels();
        DrawLegend();
    }

    private void DrawLabels()
    {
        CellRect view = Find.CameraDriver.CurrentViewRect;
        int drawn = 0;
        foreach ((Vector2 pos, string text) in labels)
        {
            if (!view.Contains(new IntVec3((int)pos.x, 0, (int)pos.y)))
                continue;
            GenMapUI.DrawText(pos, text, Color.white);
            if (++drawn >= MaxLabelsOnScreen)
                break;
        }
    }

    private void DrawLegend()
    {
        OverlayProperties style = selected.overlay;
        const float Row = 20f;
        const float Width = 170f;
        var rect = new Rect(UI.screenWidth - Width - 10f, 50f, Width, Row * (style.colors.Count + 1) + 10f);
        Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.6f));

        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.MiddleLeft;
        string unit = selected.unit.NullOrEmpty() ? "" : $" ({selected.unit})";
        Widgets.Label(new Rect(rect.x + 6f, rect.y + 4f, Width - 12f, Row), selected.LabelCap + unit);
        for (int i = 0; i < style.colors.Count; i++)
        {
            // Highest value on top, as on a map legend.
            ColorStop stop = style.colors[style.colors.Count - 1 - i];
            float y = rect.y + 4f + Row * (i + 1);
            Widgets.DrawBoxSolid(new Rect(rect.x + 6f, y + 3f, 24f, Row - 6f), stop.color);
            Widgets.Label(new Rect(rect.x + 38f, y, Width - 44f, Row), stop.value.ToString(style.labelFormat));
        }
        Text.Anchor = TextAnchor.UpperLeft;
    }
}
