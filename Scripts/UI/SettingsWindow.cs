using System;
using Godot;
using Fantasia.Client;

namespace Fantasia.UI;

/// Full options window (display, graphics, audio, controls). Lives on its own CanvasLayer so it
/// works from both the login screen and in game.
public partial class SettingsWindow : CanvasLayer
{
    PanelContainer panel;
    Label scaleValue;

    public override void _Ready()
    {
        Layer = 50;
        Visible = false;
        var root = new Control { Theme = UiTheme.Get(), MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.45f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        dim.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true }) Close(); };
        root.AddChild(dim);

        panel = new PanelContainer();
        UiTheme.Place(panel, Control.LayoutPreset.Center, new Vector2(-300, -250), new Vector2(600, 500));
        root.AddChild(panel);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 8);
        panel.AddChild(v);
        var top = new HBoxContainer();
        v.AddChild(top);
        var title = UiTheme.Title("Settings", 24);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        top.AddChild(title);
        var close = new Button { Text = "✕", CustomMinimumSize = new Vector2(34, 34), FocusMode = Control.FocusModeEnum.None };
        close.Pressed += Close;
        top.AddChild(close);

        var tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        tabs.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.12f, 0.1f, 0.08f, 0.9f), UiTheme.PanelBorder, 1, 3, 12));
        tabs.AddThemeStyleboxOverride("tab_selected", UiTheme.Box(new Color(0.35f, 0.27f, 0.17f), UiTheme.Gold, 1, 3, 8));
        tabs.AddThemeStyleboxOverride("tab_unselected", UiTheme.Box(new Color(0.2f, 0.16f, 0.11f), UiTheme.PanelBorder, 1, 3, 8));
        tabs.AddThemeStyleboxOverride("tab_hovered", UiTheme.Box(new Color(0.3f, 0.24f, 0.15f), UiTheme.Gold, 1, 3, 8));
        tabs.AddThemeColorOverride("font_selected_color", UiTheme.Gold);
        tabs.AddThemeColorOverride("font_unselected_color", UiTheme.Text);
        v.AddChild(tabs);
        tabs.AddChild(Display());
        tabs.AddChild(Graphics());
        tabs.AddChild(Audio());
        tabs.AddChild(Controls());

        var reset = new Button { Text = "Done", CustomMinimumSize = new Vector2(0, 38) };
        reset.Pressed += Close;
        v.AddChild(reset);
    }

    public void Open()
    {
        Visible = true;
        if (Settings.UiScale <= 0) scaleValue.Text = $"Auto ({Settings.AutoUiScale():0.##}×)";
    }
    public void Close() { Visible = false; Settings.Save(); }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (Visible && e is InputEventKey { Pressed: true, Keycode: Key.Escape }) { Close(); GetViewport().SetInputAsHandled(); }
    }

    // ---------- builders ----------
    static VBoxContainer Page(string name)
    {
        var p = new VBoxContainer { Name = name };
        p.AddThemeConstantOverride("separation", 10);
        return p;
    }

    static HBoxContainer Row(VBoxContainer page, string label, string tip = null)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        var l = UiTheme.Lbl(label, 15);
        l.CustomMinimumSize = new Vector2(190, 0);
        l.TooltipText = tip ?? "";
        l.MouseFilter = Control.MouseFilterEnum.Pass;
        row.AddChild(l);
        page.AddChild(row);
        return row;
    }

    void Apply() => Settings.Apply(GetTree());

    Label Slider(VBoxContainer page, string label, double min, double max, double step, double value, Func<double, string> fmt, Action<double> set, string tip = null)
    {
        var row = Row(page, label, tip);
        var s = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = value, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(200, 24) };
        var val = UiTheme.Lbl(fmt(value), 14, UiTheme.Gold);
        val.CustomMinimumSize = new Vector2(70, 0);
        s.ValueChanged += x => { val.Text = fmt(x); set(x); };
        row.AddChild(s);
        row.AddChild(val);
        return val;
    }

    void Check(VBoxContainer page, string label, bool value, Action<bool> set, string tip = null)
    {
        var row = Row(page, label, tip);
        var c = new CheckButton { ButtonPressed = value, FocusMode = Control.FocusModeEnum.None };
        c.Toggled += on => set(on);
        row.AddChild(c);
    }

    void Options(VBoxContainer page, string label, string[] items, int selected, Action<int> set, string tip = null)
    {
        var row = Row(page, label, tip);
        var o = new OptionButton { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(180, 0) };
        foreach (var it in items) o.AddItem(it);
        o.Selected = selected;
        o.ItemSelected += i => set((int)i);
        row.AddChild(o);
    }

    Control Display()
    {
        var p = Page("Display");
        scaleValue = Slider(p, "Interface scale", 0, 3, 0.25, Settings.UiScale,
            x => x <= 0 ? $"Auto ({Settings.AutoUiScale():0.##}×)" : $"{x:0.##}×",
            x => { Settings.UiScale = x < 0.75 ? 0 : (float)x; Apply(); },
            "Size of all menus, icons and text. Auto scales with your window size (2× at 4K).");
        var hint = UiTheme.Lbl("Slide to the far left for automatic scaling.", 12, UiTheme.Dim);
        p.AddChild(hint);
        Check(p, "Fullscreen", Settings.Fullscreen, on => { Settings.Fullscreen = on; Apply(); });
        Check(p, "Vertical sync", Settings.VSync, on => { Settings.VSync = on; Apply(); }, "Prevents screen tearing.");
        Options(p, "Frame rate limit", new[] { "Unlimited", "30", "60", "120", "144", "240" }, Array.IndexOf(new[] { 0, 30, 60, 120, 144, 240 }, Settings.MaxFps) is var i && i >= 0 ? i : 0,
            idx => { Settings.MaxFps = new[] { 0, 30, 60, 120, 144, 240 }[idx]; Apply(); });
        Check(p, "Show FPS counter", Settings.ShowFps, on => { Settings.ShowFps = on; Apply(); });
        return p;
    }

    Control Graphics()
    {
        var p = Page("Graphics");
        Slider(p, "3D resolution", 0.5, 1, 0.05, Settings.RenderScale, x => $"{x * 100:0}%", x => { Settings.RenderScale = (float)x; Apply(); },
            "Renders the world at a lower resolution and upscales it (FSR) for more speed.");
        Options(p, "Shadows", new[] { "Off", "Low", "High" }, Settings.Shadows, i => { Settings.Shadows = i; Apply(); });
        Options(p, "Anti-aliasing", new[] { "Off", "MSAA 2×", "MSAA 4×" }, Settings.Msaa, i => { Settings.Msaa = i; Apply(); });
        Check(p, "Ambient occlusion", Settings.Ssao, on => { Settings.Ssao = on; Apply(); });
        Check(p, "Glow & bloom", Settings.Glow, on => { Settings.Glow = on; Apply(); });
        Slider(p, "View distance", 0.5, 2, 0.1, Settings.ViewDistance, x => $"{x * 100:0}%", x => { Settings.ViewDistance = (float)x; Apply(); }, "How far you can see before the haze.");
        Check(p, "Generated character models", Settings.UseGeneratedCharacters, on => { Settings.UseGeneratedCharacters = on; Apply(); },
            "Use the detailed rigged character models. Applies to characters as they come into view.");
        return p;
    }

    Control Audio()
    {
        var p = Page("Audio");
        Slider(p, "Music volume", 0, 1, 0.05, Settings.MusicVolume, x => $"{x * 100:0}%", x => { Settings.MusicVolume = (float)x; Music.I?.ApplyVolume(); });
        Slider(p, "Effects volume", 0, 1, 0.05, Settings.SfxVolume, x => $"{x * 100:0}%", x => { Settings.SfxVolume = (float)x; });
        p.AddChild(UiTheme.Lbl("Place music_*.ogg and sfx_*.ogg files in assets/audio to add sound.", 12, UiTheme.Dim));
        return p;
    }

    Control Controls()
    {
        var p = Page("Controls");
        Slider(p, "Camera rotate speed", 0.25, 3, 0.05, Settings.CameraSpeed, x => $"{x:0.00}×", x => { Settings.CameraSpeed = (float)x; });
        Slider(p, "Camera zoom speed", 0.25, 3, 0.05, Settings.ZoomSpeed, x => $"{x:0.00}×", x => { Settings.ZoomSpeed = (float)x; });
        Check(p, "Invert camera drag", Settings.InvertCamera, on => { Settings.InvertCamera = on; });
        var help = UiTheme.Lbl(
            "Left click: move / default action\nRight click: all actions\nArrow keys or middle-drag: rotate camera\nMouse wheel: zoom\nEnter: chat    M: world map    Esc: close\nF1–F6: side panel tabs    O: settings",
            13, UiTheme.Text);
        p.AddChild(help);
        return p;
    }
}
