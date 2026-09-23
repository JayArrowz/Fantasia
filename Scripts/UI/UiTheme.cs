using Godot;

namespace Fantasia.UI;

public static class UiTheme
{
    public static readonly Color Gold = new(1f, 0.8f, 0.35f);
    public static readonly Color Parchment = new(0.93f, 0.87f, 0.72f);
    public static readonly Color Text = new(0.92f, 0.88f, 0.78f);
    public static readonly Color Dim = new(0.65f, 0.6f, 0.5f);
    public static readonly Color PanelBg = new(0.16f, 0.13f, 0.10f, 0.94f);
    public static readonly Color PanelBorder = new(0.55f, 0.43f, 0.24f);
    public static readonly Color Slot = new(0.24f, 0.2f, 0.15f, 0.9f);

    static Theme theme;
    static Font heading;

    public static Font Heading
    {
        get
        {
            heading ??= new SystemFont
            {
                FontNames = new[] { "Cinzel", "Trajan Pro", "Palatino Linotype", "Book Antiqua", "Palatino", "URW Palladio L", "Noto Serif", "DejaVu Serif", "serif" },
                FontWeight = 600,
                Antialiasing = TextServer.FontAntialiasing.Gray,
            };
            return heading;
        }
    }

    public static StyleBoxFlat Box(Color bg, Color border, int bw = 2, int radius = 4, int margin = 6)
    {
        var s = new StyleBoxFlat
        {
            BgColor = bg, BorderColor = border,
            BorderWidthLeft = bw, BorderWidthRight = bw, BorderWidthTop = bw, BorderWidthBottom = bw,
            CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
            ContentMarginLeft = margin, ContentMarginRight = margin, ContentMarginTop = margin, ContentMarginBottom = margin,
            ShadowColor = new Color(0, 0, 0, 0.45f), ShadowSize = 4,
        };
        return s;
    }

    public static StyleBox PanelStyle()
    {
        var tex = Client.Assets.Tex("parchment");
        var s = Box(PanelBg, PanelBorder, 3, 6, 8);
        return s;
    }

    public static StyleBox ParchmentStyle()
    {
        var tex = Client.Assets.Tex("parchment");
        if (tex == null) return Box(new Color(0.85f, 0.78f, 0.6f, 0.96f), PanelBorder, 3, 4, 10);
        var st = new StyleBoxTexture { Texture = tex, ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 12, ContentMarginBottom = 12, ModulateColor = new Color(1, 1, 1, 0.97f) };
        return st;
    }

    public static Theme Get()
    {
        if (theme != null) return theme;
        theme = new Theme();
        theme.DefaultFontSize = 15;
        theme.SetStylebox("panel", "Panel", PanelStyle());
        theme.SetStylebox("panel", "PanelContainer", PanelStyle());

        theme.SetStylebox("normal", "Button", Box(new Color(0.30f, 0.24f, 0.16f), PanelBorder, 2, 3, 6));
        theme.SetStylebox("hover", "Button", Box(new Color(0.40f, 0.32f, 0.20f), Gold, 2, 3, 6));
        theme.SetStylebox("pressed", "Button", Box(new Color(0.22f, 0.17f, 0.11f), Gold, 2, 3, 6));
        theme.SetStylebox("focus", "Button", Box(new Color(0, 0, 0, 0), new Color(0, 0, 0, 0), 0));
        theme.SetStylebox("disabled", "Button", Box(new Color(0.2f, 0.18f, 0.15f), new Color(0.35f, 0.3f, 0.25f), 2, 3, 6));
        theme.SetColor("font_color", "Button", Gold);
        theme.SetColor("font_hover_color", "Button", new Color(1f, 0.93f, 0.6f));
        theme.SetColor("font_pressed_color", "Button", new Color(1f, 1f, 0.8f));
        theme.SetColor("font_disabled_color", "Button", Dim);
        theme.SetConstant("outline_size", "Button", 3);
        theme.SetColor("font_outline_color", "Button", new Color(0, 0, 0, 0.9f));

        theme.SetColor("font_color", "Label", Text);
        theme.SetConstant("outline_size", "Label", 3);
        theme.SetColor("font_outline_color", "Label", new Color(0, 0, 0, 0.85f));
        theme.SetConstant("outline_size", "RichTextLabel", 2);
        theme.SetColor("font_outline_color", "RichTextLabel", new Color(0, 0, 0, 0.8f));
        theme.SetColor("default_color", "RichTextLabel", Text);

        var le = Box(new Color(0.08f, 0.07f, 0.05f, 0.9f), PanelBorder, 2, 3, 6);
        theme.SetStylebox("normal", "LineEdit", le);
        theme.SetStylebox("focus", "LineEdit", Box(new Color(0.1f, 0.09f, 0.06f, 0.95f), Gold, 2, 3, 6));
        theme.SetColor("font_color", "LineEdit", Parchment);
        theme.SetColor("caret_color", "LineEdit", Gold);

        theme.SetStylebox("panel", "PopupPanel", PanelStyle());
        theme.SetStylebox("grabber_area", "HSlider", Box(Gold, Gold, 0, 2, 2));
        theme.SetStylebox("slider", "HSlider", Box(new Color(0.1f, 0.08f, 0.05f), PanelBorder, 1, 2, 3));
        theme.SetColor("font_color", "CheckBox", Text);
        theme.SetColor("font_hover_color", "CheckBox", Gold);
        theme.SetColor("font_pressed_color", "CheckBox", Text);
        theme.SetColor("font_hover_pressed_color", "CheckBox", Gold);
        return theme;
    }

    /// Anchor a control to a preset and position it by offsets from that anchor.
    public static void Place(Control c, Control.LayoutPreset preset, Vector2 offset, Vector2 size)
    {
        c.SetAnchorsPreset(preset);
        c.OffsetLeft = offset.X;
        c.OffsetTop = offset.Y;
        c.OffsetRight = offset.X + size.X;
        c.OffsetBottom = offset.Y + size.Y;
    }

    public static Label Title(string text, int size = 20)
    {
        var l = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        l.AddThemeFontOverride("font", Heading);
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", Gold);
        return l;
    }

    public static Label Lbl(string text, int size = 14, Color? color = null)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", size);
        if (color.HasValue) l.AddThemeColorOverride("font_color", color.Value);
        return l;
    }
}
