using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace LivingLandscapes.Settings;

/// <summary>
/// The tuning editor in the mod settings: every value of the generation tuning by group, filterable, each with its XML default and
/// a reset. Edits apply to the live tuning at once and are saved with the mod's settings; the next map generated uses them.
/// </summary>
public static class TuningPanel
{
    private const float RowHeight = 26f;
    private static Vector2 scroll;
    private static string filter = "";
    private static readonly HashSet<string> open = new();
    private static readonly Dictionary<string, string> buffers = new();

    public static void Draw(Rect rect, LivingLandscapesSettings settings)
    {
        settings.tuning ??= new Dictionary<string, string>();
        List<Tuning.Entry> entries = Tuning.Entries;
        float y = rect.y;
        Widgets.Label(new Rect(rect.x, y, rect.width, 48f), "LL_TuningDesc".Translate());
        y += 50f;
        Widgets.Label(new Rect(rect.x, y + 3f, 60f, 24f), "LL_TuningFilter".Translate());
        filter = Widgets.TextField(new Rect(rect.x + 62f, y, 260f, 24f), filter ?? "");
        if (Widgets.ButtonText(new Rect(rect.xMax - 160f, y, 160f, 24f), "LL_TuningResetAll".Translate()))
        {
            settings.tuning.Clear();
            buffers.Clear();
            Tuning.ApplyAll(settings.tuning);
        }
        Widgets.Label(new Rect(rect.x + 340f, y + 3f, 300f, 24f), "LL_TuningChanged".Translate(settings.tuning.Count));
        y += 30f;

        string needle = filter?.Trim().ToLowerInvariant() ?? "";
        var rows = new List<(string group, Tuning.Entry entry)>();
        foreach (IGrouping<string, Tuning.Entry> group in entries.GroupBy(e => e.Group))
        {
            List<Tuning.Entry> shown = group.Where(e => needle.Length == 0 || e.Path.ToLowerInvariant().Contains(needle)).ToList();
            if (shown.Count == 0)
                continue;
            rows.Add((group.Key, null));
            if (needle.Length > 0 || open.Contains(group.Key))
                rows.AddRange(shown.Select(e => (group.Key, e)));
        }

        var outer = new Rect(rect.x, y, rect.width, rect.yMax - y);
        var inner = new Rect(0f, 0f, outer.width - 16f, rows.Count * RowHeight);
        Widgets.BeginScrollView(outer, ref scroll, inner);
        float rowY = 0f;
        foreach ((string group, Tuning.Entry entry) in rows)
        {
            var row = new Rect(0f, rowY, inner.width, RowHeight);
            rowY += RowHeight;
            if (entry == null)
            {
                int changed = entries.Count(e => e.Group == group && settings.tuning.ContainsKey(e.Path));
                bool isOpen = needle.Length > 0 || open.Contains(group);
                string label = $"{(isOpen ? "-" : "+")} {group} ({entries.Count(e => e.Group == group)}{(changed > 0 ? $", {changed} changed" : "")})";
                if (Widgets.ButtonText(new Rect(row.x, row.y + 1f, 360f, RowHeight - 2f), label, drawBackground: false) && needle.Length == 0)
                {
                    if (!open.Remove(group))
                        open.Add(group);
                }
                continue;
            }
            DrawEntry(row, entry, settings);
        }
        Widgets.EndScrollView();
    }

    private static void DrawEntry(Rect row, Tuning.Entry entry, LivingLandscapesSettings settings)
    {
        bool changed = settings.tuning.ContainsKey(entry.Path);
        string shortPath = entry.Path.StartsWith(entry.Group + ".") ? entry.Path.Substring(entry.Group.Length + 1) : entry.Path;
        float labelWidth = row.width * 0.5f;
        if (changed)
            GUI.color = new Color(1f, 0.85f, 0.4f);
        Widgets.Label(new Rect(row.x + 16f, row.y + 3f, labelWidth - 16f, RowHeight), shortPath);
        GUI.color = Color.gray;
        Widgets.Label(new Rect(row.x + labelWidth, row.y + 3f, 90f, RowHeight), entry.Default);
        GUI.color = Color.white;
        TooltipHandler.TipRegion(new Rect(row.x, row.y, labelWidth, RowHeight), $"{entry.Path}\n{"LL_TuningDefault".Translate(entry.Default)}");

        var field = new Rect(row.x + labelWidth + 95f, row.y + 1f, 120f, RowHeight - 2f);
        if (entry.Type == typeof(bool))
        {
            bool value = (bool)entry.Get();
            bool before = value;
            Widgets.Checkbox(field.x, field.y, ref value);
            if (value != before)
                Store(entry, Tuning.Format(value), settings);
        }
        else
        {
            if (!buffers.TryGetValue(entry.Path, out string text))
                text = Tuning.Format(entry.Get());
            string edited = Widgets.TextField(field, text);
            buffers[entry.Path] = edited;
            if (edited != text && Tuning.TryParse(entry, edited, out _))
                Store(entry, edited, settings);
        }
        if (changed && Widgets.ButtonText(new Rect(field.xMax + 6f, row.y + 1f, 70f, RowHeight - 2f), "LL_TuningReset".Translate()))
        {
            settings.tuning.Remove(entry.Path);
            buffers.Remove(entry.Path);
            if (Tuning.TryParse(entry, entry.Default, out object value))
                entry.Set(value);
        }
    }

    private static void Store(Tuning.Entry entry, string text, LivingLandscapesSettings settings)
    {
        if (!Tuning.TryParse(entry, text, out object value))
            return;
        entry.Set(value);
        if (text == entry.Default)
            settings.tuning.Remove(entry.Path);
        else
            settings.tuning[entry.Path] = text;
    }
}
