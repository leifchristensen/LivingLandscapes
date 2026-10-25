using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using LivingLandscapes.Environment;
using LivingLandscapes.Geology;
using Verse;

namespace LivingLandscapes.Settings;

/// <summary>
/// Player overrides of the generation tuning (the constants and multipliers in the geology's equations, set in XML on
/// LL_Landform's <see cref="GeologyGenerationExtension"/>). Every number, switch and range in that tuning is found by reflection
/// and named by its path ("terrainChannels.feedback.aggradationShare"); overrides are kept in the mod's settings as text by
/// path, applied to the live tuning at startup and whenever one changes, so the next map generated uses them.
/// </summary>
public static class Tuning
{
    /// <summary>One tunable value: its path, and how to read and write it on the live tuning.</summary>
    public class Entry
    {
        public string Path;
        public string Group;
        public Type Type;
        public Func<object> Get;
        public Action<object> Set;
        /// <summary>The value XML gave, as text (before any override).</summary>
        public string Default;
    }

    private static List<Entry> entries;

    /// <summary>The tunable values, in the order the tuning declares them; empty before the defs are loaded.</summary>
    public static List<Entry> Entries
    {
        get
        {
            if (entries == null)
                Build();
            return entries ?? new List<Entry>();
        }
    }

    private static void Build()
    {
        var root = EnvironmentLayerDefOf.LL_Landform?.GetModExtension<GeologyGenerationExtension>();
        if (root == null)
            return;
        entries = new List<Entry>();
        Collect(root, "", "geology", 0);
        foreach (Entry entry in entries)
            entry.Default = Format(entry.Get());
    }

    private static void Collect(object owner, string prefix, string group, int depth)
    {
        foreach (FieldInfo field in owner.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            string path = prefix + field.Name;
            Type type = field.FieldType;
            if (type == typeof(float) || type == typeof(int) || type == typeof(bool))
            {
                FieldInfo f = field;
                object o = owner;
                entries.Add(new Entry { Path = path, Group = group, Type = type, Get = () => f.GetValue(o), Set = v => f.SetValue(o, v) });
            }
            else if (type == typeof(FloatRange) || type == typeof(IntRange))
            {
                FieldInfo f = field;
                object o = owner;
                bool isFloat = type == typeof(FloatRange);
                foreach (string end in new[] { "min", "max" })
                {
                    FieldInfo part = type.GetField(end);
                    entries.Add(new Entry
                    {
                        Path = path + "." + end, Group = group, Type = isFloat ? typeof(float) : typeof(int),
                        Get = () => part.GetValue(f.GetValue(o)),
                        Set = v =>
                        {
                            object range = f.GetValue(o);
                            part.SetValue(range, v);
                            f.SetValue(o, range);
                        },
                    });
                }
            }
            else if (depth < 4 && type.IsClass && type.Namespace != null && type.Namespace.StartsWith("LivingLandscapes") && field.GetValue(owner) is { } child)
                Collect(child, path + ".", group == "geology" ? field.Name : group, depth + 1);
        }
    }

    public static string Format(object value) => value switch
    {
        float f => f.ToString("0.#####", CultureInfo.InvariantCulture),
        int i => i.ToString(CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        _ => value?.ToString() ?? "",
    };

    /// <summary>Parses <paramref name="text"/> as the entry's type; false if it is not one.</summary>
    public static bool TryParse(Entry entry, string text, out object value)
    {
        value = null;
        if (entry.Type == typeof(float) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
            value = f;
        else if (entry.Type == typeof(int) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
            value = i;
        else if (entry.Type == typeof(bool) && bool.TryParse(text, out bool b))
            value = b;
        return value != null;
    }

    /// <summary>Applies every saved override to the live tuning (values that no longer parse are left at their default).</summary>
    public static int ApplyAll(Dictionary<string, string> overrides)
    {
        int applied = 0;
        foreach (Entry entry in Entries)
        {
            string text = overrides != null && overrides.TryGetValue(entry.Path, out string saved) ? saved : entry.Default;
            if (TryParse(entry, text, out object value))
            {
                entry.Set(value);
                if (text != entry.Default)
                    applied++;
            }
        }
        return applied;
    }
}

/// <summary>Applies the saved tuning overrides once the defs (and their XML tuning) are loaded.</summary>
[StaticConstructorOnStartup]
public static class TuningStartup
{
    static TuningStartup()
    {
        int applied = Tuning.ApplyAll(LivingLandscapesMod.Settings?.tuning);
        Log.Message($"[LivingLandscapes] Tuning: {Tuning.Entries.Count} tunable values, {applied} overridden in the mod settings.");
    }
}
