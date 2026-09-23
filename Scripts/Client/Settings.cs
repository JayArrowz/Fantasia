using System;
using System.IO;
using Godot;

namespace Fantasia.Client;

/// Player preferences, persisted to user://settings.json and applied live.
public static class Settings
{
    // Audio
    public static float MusicVolume = 0.45f;
    public static float SfxVolume = 0.7f;
    // Display
    public static float UiScale;               // 0 = automatic
    public static bool Fullscreen;
    public static bool VSync = true;
    public static int MaxFps;                  // 0 = unlimited
    public static float RenderScale = 1f;
    // Graphics
    public static int Shadows = 2;             // 0 off, 1 low, 2 high
    public static bool Ssao = true;
    public static bool Glow = true;
    public static int Msaa = 1;                // 0 off, 1 2x, 2 4x
    public static float ViewDistance = 1f;     // fog/draw multiplier
    // Controls / gameplay
    public static float CameraSpeed = 1f;
    public static float ZoomSpeed = 1f;
    public static bool InvertCamera;
    public static bool ShowFps;
    public static bool UseGeneratedCharacters = true;
    // Login
    public static string LastName = "";
    public static string LastHost = "127.0.0.1";
    public static int LastPort = GameConst.DefaultPort;

    /// Raised after any setting changes so live systems (environment, HUD) can refresh.
    public static event Action Changed;

    sealed class Data
    {
        public float MusicVolume = 0.45f, SfxVolume = 0.7f, UiScale, RenderScale = 1f, ViewDistance = 1f, CameraSpeed = 1f, ZoomSpeed = 1f;
        public bool Fullscreen, VSync = true, Ssao = true, Glow = true, InvertCamera, ShowFps, UseGeneratedCharacters = true;
        public int MaxFps, Shadows = 2, Msaa = 1;
        public string LastName, LastHost;
        public int LastPort;
    }

    static string PathOf => ProjectSettings.GlobalizePath("user://settings.json");

    public static void Load()
    {
        try
        {
            if (!File.Exists(PathOf)) return;
            var d = Json.Read<Data>(File.ReadAllText(PathOf));
            if (d == null) return;
            MusicVolume = d.MusicVolume; SfxVolume = d.SfxVolume; UiScale = d.UiScale; Fullscreen = d.Fullscreen; VSync = d.VSync;
            MaxFps = d.MaxFps; RenderScale = d.RenderScale <= 0 ? 1f : d.RenderScale; Shadows = d.Shadows; Ssao = d.Ssao; Glow = d.Glow;
            Msaa = d.Msaa; ViewDistance = d.ViewDistance <= 0 ? 1f : d.ViewDistance; CameraSpeed = d.CameraSpeed <= 0 ? 1f : d.CameraSpeed;
            ZoomSpeed = d.ZoomSpeed <= 0 ? 1f : d.ZoomSpeed; InvertCamera = d.InvertCamera; ShowFps = d.ShowFps;
            UseGeneratedCharacters = d.UseGeneratedCharacters;
            LastName = d.LastName ?? ""; LastHost = d.LastHost ?? "127.0.0.1"; LastPort = d.LastPort > 0 ? d.LastPort : GameConst.DefaultPort;
        }
        catch (Exception e) { GD.PrintErr($"Settings load failed: {e.Message}"); }
    }

    public static void Save()
    {
        try
        {
            File.WriteAllText(PathOf, Json.Write(new Data
            {
                MusicVolume = MusicVolume, SfxVolume = SfxVolume, UiScale = UiScale, Fullscreen = Fullscreen, VSync = VSync, MaxFps = MaxFps,
                RenderScale = RenderScale, Shadows = Shadows, Ssao = Ssao, Glow = Glow, Msaa = Msaa, ViewDistance = ViewDistance,
                CameraSpeed = CameraSpeed, ZoomSpeed = ZoomSpeed, InvertCamera = InvertCamera, ShowFps = ShowFps,
                UseGeneratedCharacters = UseGeneratedCharacters, LastName = LastName, LastHost = LastHost, LastPort = LastPort,
            }));
        }
        catch (Exception e) { GD.PrintErr($"Settings save failed: {e.Message}"); }
    }

    /// Automatic scale from the current window height (1080p = 1x, 1440p = 1.25x, 2160p = 2x).
    public static float AutoUiScale()
    {
        if (DisplayServer.GetName() == "headless") return 1f;
        var size = DisplayServer.WindowGetSize();
        float s = size.Y / 1080f;
        return Mathf.Clamp(Mathf.Round(s * 4f) / 4f, 1f, 3f);
    }

    public static float EffectiveUiScale => UiScale > 0 ? UiScale : AutoUiScale();

    /// Applies window/display settings. Call after changing Display values.
    public static void Apply(SceneTree tree)
    {
        if (DisplayServer.GetName() != "headless")
        {
            var mode = Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed;
            if (DisplayServer.WindowGetMode() != mode && !(mode == DisplayServer.WindowMode.Windowed && DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Maximized))
                DisplayServer.WindowSetMode(mode);
            DisplayServer.WindowSetVsyncMode(VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        }
        Engine.MaxFps = MaxFps;
        var root = tree.Root;
        float ui = EffectiveUiScale;
        if (!Mathf.IsEqualApprox(root.ContentScaleFactor, ui)) root.ContentScaleFactor = ui;
        root.Scaling3DScale = Mathf.Clamp(RenderScale, 0.5f, 1f);
        root.Scaling3DMode = RenderScale < 0.99f ? Viewport.Scaling3DModeEnum.Fsr : Viewport.Scaling3DModeEnum.Bilinear;
        root.Msaa3D = Msaa switch { 0 => Viewport.Msaa.Disabled, 2 => Viewport.Msaa.Msaa4X, _ => Viewport.Msaa.Msaa2X };
        Changed?.Invoke();
    }
}
