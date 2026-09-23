using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Godot;

namespace Fantasia;

// Small value types used by the data tables (JSON-friendly replacements for tuples).
public record struct ItemCount(string item, int count) { public static implicit operator ItemCount((string, int) t) => new(t.Item1, t.Item2); }
public record struct RuneCost(string rune, int count) { public static implicit operator RuneCost((string, int) t) => new(t.Item1, t.Item2); }
public record struct SkillLevel(Skill skill, int level) { public static implicit operator SkillLevel((Skill, int) t) => new(t.Item1, t.Item2); }
public record struct SkillXp(Skill skill, int xp) { public static implicit operator SkillXp((Skill, int) t) => new(t.Item1, t.Item2); }
public record struct Yield(string item, int level, float xp) { public static implicit operator Yield((string, int, float) t) => new(t.Item1, t.Item2, t.Item3); }

/// Game content lives in res://data/*.json (items, npcs, shops, spells, recipes, quests, crops,
/// resources, map placements, equipment fitting). This loads and saves those files: enums by name,
/// colours as hex, and each entry lists only the fields that differ from the class defaults.
public static class GameData
{
    public const string Dir = "res://data/";

    public static readonly JsonSerializerOptions Opts = new()
    {
        IncludeFields = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(), new ColorConverter(), new Vector3Converter() },
    };

    /// Reads res://data/<file>. Throws with the file name if it is missing or malformed, since the
    /// game can't run without its content.
    public static T Load<T>(string file)
    {
        string path = Dir + file;
        string text = ReadText(path) ?? throw new InvalidOperationException($"Missing game data file {path}");
        try { return JsonSerializer.Deserialize<T>(text, Opts); }
        catch (JsonException e) { throw new InvalidOperationException($"Bad game data in {path}: {e.Message}", e); }
    }

    static string ReadText(string path)
    {
        if (!Godot.FileAccess.FileExists(path)) return null;
        using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        return f?.GetAsText();
    }

    /// Last-write time of a data file (dev hot reload).
    public static ulong Modified(string file) => Godot.FileAccess.GetModifiedTime(Dir + file);

    /// Serializes `value`, dropping fields that still hold their default (as a fresh instance of the
    /// element type has them), so a hand-edited file only shows what makes each entry special.
    public static string Write(object value)
    {
        var node = JsonSerializer.SerializeToNode(value, value.GetType(), Opts);
        Prune(node, value.GetType());
        return node.ToJsonString(Opts);
    }

    static void Prune(JsonNode node, Type t)
    {
        if (node == null || t == null) return;
        if (node is JsonArray arr)
        {
            var et = t.IsArray ? t.GetElementType() : t.IsGenericType ? t.GetGenericArguments().Last() : null;
            foreach (var n in arr) Prune(n, et);
            return;
        }
        if (node is not JsonObject obj) return;
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            var vt = t.GetGenericArguments()[1];
            foreach (var kv in obj.ToList()) Prune(kv.Value, vt);
            return;
        }
        if (!t.IsClass || t == typeof(string) || t.GetConstructor(Type.EmptyTypes) == null) return;
        var defaults = JsonSerializer.SerializeToNode(Activator.CreateInstance(t), t, Opts) as JsonObject;
        foreach (var kv in obj.ToList())
        {
            var member = (System.Reflection.MemberInfo)t.GetField(kv.Key, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase)
                ?? t.GetProperty(kv.Key, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
            var mt = member is System.Reflection.FieldInfo fi ? fi.FieldType : (member as System.Reflection.PropertyInfo)?.PropertyType;
            JsonNode dv = null;
            if (defaults != null && defaults.TryGetPropertyValue(kv.Key, out dv) && JsonNode.DeepEquals(dv, kv.Value)) { obj.Remove(kv.Key); continue; }
            // An empty list is dropped only when the default is empty too (or unset).
            if (kv.Value is JsonArray a && a.Count == 0 && (dv == null || dv is JsonArray da && da.Count == 0)) { obj.Remove(kv.Key); continue; }
            Prune(kv.Value, mt);
        }
    }

    /// Vectors as [x, y, z].
    sealed class Vector3Converter : JsonConverter<Vector3>
    {
        public override Vector3 Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
        {
            var v = JsonSerializer.Deserialize<float[]>(ref r, o);
            return new Vector3(v[0], v[1], v[2]);
        }

        public override void Write(Utf8JsonWriter w, Vector3 v, JsonSerializerOptions o)
        {
            w.WriteStartArray();
            w.WriteNumberValue(MathF.Round(v.X, 4)); w.WriteNumberValue(MathF.Round(v.Y, 4)); w.WriteNumberValue(MathF.Round(v.Z, 4));
            w.WriteEndArray();
        }
    }

    /// Colours as "#rrggbb" (or "#rrggbbaa" when not opaque). Over-bright tints (a channel above 1,
    /// used to make a model glow) can't be hex, so they are written as [r, g, b(, a)] floats.
    sealed class ColorConverter : JsonConverter<Color>
    {
        public override Color Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
        {
            if (r.TokenType == JsonTokenType.String) return Color.FromHtml(r.GetString());
            // Also accept [r, g, b(, a)] arrays of 0-1 floats.
            var v = JsonSerializer.Deserialize<float[]>(ref r, o);
            return new Color(v[0], v[1], v[2], v.Length > 3 ? v[3] : 1f);
        }

        public override void Write(Utf8JsonWriter w, Color c, JsonSerializerOptions o)
        {
            if (c.R <= 1f && c.G <= 1f && c.B <= 1f && c.R >= 0f && c.G >= 0f && c.B >= 0f) { w.WriteStringValue("#" + c.ToHtml(c.A < 0.999f)); return; }
            w.WriteStartArray();
            w.WriteNumberValue(MathF.Round(c.R, 4)); w.WriteNumberValue(MathF.Round(c.G, 4)); w.WriteNumberValue(MathF.Round(c.B, 4));
            if (c.A < 0.999f) w.WriteNumberValue(MathF.Round(c.A, 4));
            w.WriteEndArray();
        }
    }
}
