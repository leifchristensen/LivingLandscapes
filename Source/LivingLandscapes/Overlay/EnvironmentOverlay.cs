using System.Collections.Generic;
using System.Linq;
using LivingLandscapes.Environment;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Overlay;

/// <summary>
/// Draws any number of environment layers at once, each toggled on its own map button. A layer draws as a colour fill
/// with contours, or as a vector field of arrows when its overlay names a direction layer. Display state only;
/// nothing here is saved.
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

    /// <summary>Active layers and what they draw, in the order they were switched on (later ones on top).</summary>
    private readonly List<LayerRender> active = new List<LayerRender>();

    public EnvironmentOverlay(MapComponent_Environment environment)
    {
        this.environment = environment;
    }

    /// <summary>Layers of this map that have overlay settings, in compute order (on-demand dev layers once the map has their inputs).</summary>
    public IEnumerable<EnvironmentLayerDef> Available =>
        EnvironmentLayerOrder.Sorted.Where(l => l.overlay != null && (environment.HasLayer(l) || (l.onDemand && l.dependsOn.All(environment.HasLayer))));

    public bool IsActive(EnvironmentLayerDef layer) => active.Any(r => r.layer == layer);

    public void Toggle(EnvironmentLayerDef layer)
    {
        if (layer == null)
            return;
        if (active.RemoveAll(r => r.layer == layer) > 0)
            return;
        // A dev view shows the map as it is now: computed afresh each time it is switched on.
        if (layer.onDemand && !environment.ComputeOnDemand(layer))
            return;
        LayerRender render = Build(layer);
        if (render != null)
            active.Add(render);
    }

    public void Notify_LayersChanged()
    {
        List<EnvironmentLayerDef> layers = active.Select(r => r.layer).ToList();
        active.Clear();
        foreach (EnvironmentLayerDef layer in layers)
            Toggle(layer);
    }

    private LayerRender Build(EnvironmentLayerDef layer)
    {
        if (!environment.TryGetGrid(layer, out EnvironmentGrid grid))
            return null;
        Map map = environment.Map;
        var render = new LayerRender
        {
            layer = layer,
            style = layer.overlay.ResolveFor(grid.Min, grid.Max),
        };
        float[] values = grid.ToArray();

        if (render.style.IsVectorField)
        {
            if (!environment.TryGetGrid(render.style.directionLayer, out EnvironmentGrid direction))
                return null;
            render.lines = VectorFieldRenderer.Build(values, direction.ToArray(), map.Size.x, map.Size.z, render.style,
                grid.Min, grid.Max, LineRenderQueue);
            return render;
        }

        OverlayProperties style = render.style;
        render.fill = new CellBoolDrawer(_ => true, () => Color.white, i => style.FillColor(values[i]),
            map.Size.x, map.Size.z, FillRenderQueue, style.fillOpacity);

        if (style.contourInterval <= 0f)
            return render;
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
                render.labels.Add(((s.A + s.B) * 0.5f, (s.Level * style.contourInterval).ToString(style.labelFormat)));
            segmentsOnLevel[s.Level] = n + 1;
        }
        Material lineMaterial = SolidColorMaterials.NewSolidColorMaterial(style.contourColor, ShaderDatabase.SolidColor);
        lineMaterial.renderQueue = LineRenderQueue;
        render.lines = new List<(Material, List<Mesh>)>
        {
            (lineMaterial, minor.Finish()),
            (lineMaterial, index.Finish()),
        };
        return render;
    }

    public void Update()
    {
        foreach (LayerRender render in active)
        {
            if (render.fill != null)
            {
                render.fill.MarkForDraw();
                render.fill.CellBoolDrawerUpdate();
            }
            foreach ((Material material, List<Mesh> meshes) in render.lines)
            {
                foreach (Mesh mesh in meshes)
                    Graphics.DrawMesh(mesh, Vector3.zero, Quaternion.identity, material, 0);
            }
        }
    }

    public void OnGUI()
    {
        if (active.Count == 0 || Event.current.type != EventType.Repaint)
            return;
        DrawLabels();
        DrawLegend();
    }

    private void DrawLabels()
    {
        CellRect view = Find.CameraDriver.CurrentViewRect;
        int drawn = 0;
        foreach (LayerRender render in active)
        {
            foreach ((Vector2 pos, string text) in render.labels)
            {
                if (!view.Contains(new IntVec3((int)pos.x, 0, (int)pos.y)))
                    continue;
                GenMapUI.DrawText(pos, text, Color.white);
                if (++drawn >= MaxLabelsOnScreen)
                    return;
            }
        }
    }

    /// <summary>One legend block per active layer, stacked in the top right corner.</summary>
    private void DrawLegend()
    {
        const float Row = 20f;
        const float Width = 170f;
        int rows = active.Sum(r => r.style.colors.Count + 1);
        var rect = new Rect(UI.screenWidth - Width - 10f, 50f, Width, Row * rows + 4f * active.Count + 6f);
        Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.6f));

        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.MiddleLeft;
        float y = rect.y + 4f;
        foreach (LayerRender render in active)
        {
            string unit = render.layer.unit.NullOrEmpty() ? "" : $" ({render.layer.unit})";
            Widgets.Label(new Rect(rect.x + 6f, y, Width - 12f, Row), render.layer.LabelCap + unit);
            y += Row;
            List<ColorStop> colors = render.style.colors;
            for (int i = colors.Count - 1; i >= 0; i--)
            {
                Widgets.DrawBoxSolid(new Rect(rect.x + 6f, y + 3f, 24f, Row - 6f), colors[i].color);
                Widgets.Label(new Rect(rect.x + 38f, y, Width - 44f, Row), colors[i].value.ToString(render.style.labelFormat));
                y += Row;
            }
            y += 4f;
        }
        Text.Anchor = TextAnchor.UpperLeft;
    }

    /// <summary>What one active layer draws: an optional cell fill plus line meshes (contours or arrows).</summary>
    private class LayerRender
    {
        public EnvironmentLayerDef layer;
        public OverlayProperties style;
        public CellBoolDrawer fill;
        public List<(Material material, List<Mesh> meshes)> lines = new List<(Material, List<Mesh>)>();
        public readonly List<(Vector2 pos, string text)> labels = new List<(Vector2, string)>();
    }
}
