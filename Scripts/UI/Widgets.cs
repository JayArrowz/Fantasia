using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Fantasia.Client;

namespace Fantasia.UI;

/// 2D UI icons from assets/icons/<name>.png (generated art), cached.
public static class UiIcons
{
    static readonly Dictionary<string, Texture2D> Cache = new();
    public static Texture2D Get(string name)
    {
        if (Cache.TryGetValue(name, out var t)) return t;
        string p = $"res://assets/icons/{name}.png";
        t = ResourceLoader.Exists(p) ? ResourceLoader.Load<Texture2D>(p) : null;
        Cache[name] = t;
        return t;
    }
}

/// A styled rich tooltip panel used by spells, items and skills.
public static class RichTooltip
{
    static readonly PackedScene Scene = GD.Load<PackedScene>("res://Scenes/UI/Tooltip.tscn");

    /// Scenes/UI/Tooltip.tscn filled with bbcode, at the given width.
    public static Control Make(string bbcode, float width = 280)
    {
        var panel = Scene.Instantiate<Control>();
        var rt = panel.GetNode<RichTextLabel>("%Text");
        rt.CustomMinimumSize = new Vector2(width, 0);
        rt.Text = bbcode;
        return panel;
    }
}
