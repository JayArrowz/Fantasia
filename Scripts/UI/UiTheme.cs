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

    /// The interface theme (UI/Theme.tres, edited in Godot). Named styles such as TitleLabel,
    /// GoldLabel, DimLabel, ParchmentPanel and InkText are applied in the scenes with
    /// theme_type_variation.
    public static Theme Get() => theme ??= GD.Load<Theme>("res://UI/Theme.tres");

    /// The heading font (the TitleLabel style's), for text drawn in code.
    public static Font Heading => Get().GetFont("font", "TitleLabel");

    /// A flat box style, for colours that come from game data (e.g. each skill's bar colour).
    public static StyleBoxFlat Box(Color bg, Color border, int bw = 2, int radius = 4, int margin = 6) => new()
    {
        BgColor = bg, BorderColor = border,
        BorderWidthLeft = bw, BorderWidthRight = bw, BorderWidthTop = bw, BorderWidthBottom = bw,
        CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
        ContentMarginLeft = margin, ContentMarginRight = margin, ContentMarginTop = margin, ContentMarginBottom = margin,
        ShadowColor = new Color(0, 0, 0, 0.45f), ShadowSize = 4,
    };

    /// A heading label for lists built at runtime (TitleLabel style).
    public static Label Title(string text, int size = 20)
    {
        var l = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, ThemeTypeVariation = "TitleLabel" };
        l.AddThemeFontSizeOverride("font_size", size);
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
