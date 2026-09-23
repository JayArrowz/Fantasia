using System;
using Godot;
using Fantasia.Client;

namespace Fantasia.UI;

/// Full options window (display, graphics, audio, controls): Scenes/UI/SettingsWindow.tscn. Lives on
/// its own CanvasLayer so it works from both the login screen and in game. This binds each control
/// in the scene to its setting.
public partial class SettingsWindow : CanvasLayer
{
    Label scaleValue;

    public static SettingsWindow Create() => GD.Load<PackedScene>("res://Scenes/UI/SettingsWindow.tscn").Instantiate<SettingsWindow>();

    public override void _Ready()
    {
        Visible = false;
        GetNode<Control>("%Dim").GuiInput += e => { if (e is InputEventMouseButton { Pressed: true }) Close(); };
        GetNode<Button>("%Close").Pressed += Close;
        GetNode<Button>("%Done").Pressed += Close;

        // Display
        scaleValue = Slider("UiScale", Settings.UiScale,
            x => x <= 0 ? $"Auto ({Settings.AutoUiScale():0.##}×)" : $"{x:0.##}×",
            x => { Settings.UiScale = x < 0.75 ? 0 : (float)x; Apply(); });
        Check("Fullscreen", Settings.Fullscreen, on => { Settings.Fullscreen = on; Apply(); });
        Check("VSync", Settings.VSync, on => { Settings.VSync = on; Apply(); });
        int[] fpsSteps = { 0, 30, 60, 120, 144, 240 };
        Options("MaxFps", Math.Max(0, Array.IndexOf(fpsSteps, Settings.MaxFps)), i => { Settings.MaxFps = fpsSteps[i]; Apply(); });
        Check("ShowFps", Settings.ShowFps, on => { Settings.ShowFps = on; Apply(); });

        // Graphics
        Slider("RenderScale", Settings.RenderScale, x => $"{x * 100:0}%", x => { Settings.RenderScale = (float)x; Apply(); });
        Options("Shadows", Settings.Shadows, i => { Settings.Shadows = i; Apply(); });
        Options("Msaa", Settings.Msaa, i => { Settings.Msaa = i; Apply(); });
        Check("Ssao", Settings.Ssao, on => { Settings.Ssao = on; Apply(); });
        Check("Glow", Settings.Glow, on => { Settings.Glow = on; Apply(); });
        Slider("ViewDistance", Settings.ViewDistance, x => $"{x * 100:0}%", x => { Settings.ViewDistance = (float)x; Apply(); });
        Check("GeneratedCharacters", Settings.UseGeneratedCharacters, on => { Settings.UseGeneratedCharacters = on; Apply(); });

        // Audio
        Slider("MusicVolume", Settings.MusicVolume, x => $"{x * 100:0}%", x => { Settings.MusicVolume = (float)x; Music.I?.ApplyVolume(); });
        Slider("SfxVolume", Settings.SfxVolume, x => $"{x * 100:0}%", x => { Settings.SfxVolume = (float)x; });

        // Controls
        Slider("CameraSpeed", Settings.CameraSpeed, x => $"{x:0.00}×", x => { Settings.CameraSpeed = (float)x; });
        Slider("ZoomSpeed", Settings.ZoomSpeed, x => $"{x:0.00}×", x => { Settings.ZoomSpeed = (float)x; });
        Check("InvertCamera", Settings.InvertCamera, on => { Settings.InvertCamera = on; });
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

    void Apply() => Settings.Apply(GetTree());

    /// Binds slider %<name> and its value label %<name>Value.
    Label Slider(string name, double value, Func<double, string> fmt, Action<double> set)
    {
        var s = GetNode<HSlider>("%" + name);
        var val = GetNode<Label>("%" + name + "Value");
        s.Value = value;
        val.Text = fmt(value);
        s.ValueChanged += x => { val.Text = fmt(x); set(x); };
        return val;
    }

    void Check(string name, bool value, Action<bool> set)
    {
        var c = GetNode<CheckButton>("%" + name);
        c.ButtonPressed = value;
        c.Toggled += on => set(on);
    }

    void Options(string name, int selected, Action<int> set)
    {
        var o = GetNode<OptionButton>("%" + name);
        o.Selected = selected;
        o.ItemSelected += i => set((int)i);
    }
}
