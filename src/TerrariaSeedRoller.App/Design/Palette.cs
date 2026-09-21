namespace TerrariaSeedRoller.App.Design;

/// <summary>
/// The single source of colour truth for the whole interface. Every control reads
/// its colours from here, so the palette can never drift apart again.
/// </summary>
internal static class Palette
{
    // ---- surfaces -------------------------------------------------------
    public static readonly Color Canvas = FromHex(0x0D131A);
    public static readonly Color CanvasDeep = FromHex(0x090E14);
    public static readonly Color Surface = FromHex(0x141C25);
    public static readonly Color SurfaceRaised = FromHex(0x1A2430);
    public static readonly Color SurfaceInput = FromHex(0x111A23);
    public static readonly Color SurfaceHover = FromHex(0x223040);
    public static readonly Color SurfaceActive = FromHex(0x2B3B4E);
    public static readonly Color SurfaceSunken = FromHex(0x0B1117);

    // ---- lines ----------------------------------------------------------
    public static readonly Color Border = FromHex(0x26323F);
    public static readonly Color BorderStrong = FromHex(0x38495C);
    public static readonly Color GridLine = FromHex(0x1F2A36);

    // ---- text -----------------------------------------------------------
    public static readonly Color TextPrimary = FromHex(0xE9F0F7);
    public static readonly Color TextSecondary = FromHex(0xAEC0D1);
    public static readonly Color TextMuted = FromHex(0x94A6B8);
    public static readonly Color TextDisabled = FromHex(0x6E8093);
    public static readonly Color TextOnAccent = FromHex(0x171207);

    // ---- accents --------------------------------------------------------
    public static readonly Color Accent = FromHex(0xE3B25C);
    public static readonly Color AccentHover = FromHex(0xF2C878);
    public static readonly Color AccentPressed = FromHex(0xC7953F);
    public static readonly Color Success = FromHex(0x4FC98A);
    public static readonly Color SuccessHover = FromHex(0x63DB9C);
    public static readonly Color SuccessPressed = FromHex(0x3AA96F);
    public static readonly Color Danger = FromHex(0xE5605A);
    public static readonly Color DangerHover = FromHex(0xF0756F);
    public static readonly Color DangerPressed = FromHex(0xC44A45);
    public static readonly Color Info = FromHex(0x5FA8E8);

    // ---- states ---------------------------------------------------------
    public static readonly Color Selection = FromHex(0x24384F);
    public static readonly Color SelectionText = FromHex(0xF2F7FF);
    public static readonly Color FocusRing = FromHex(0xE3B25C);
    public static readonly Color Overlay = FromHex(0x060A0E);

    public static Color FromHex(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    public static Color Mix(Color a, Color b, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            255,
            (int)Math.Round(a.R + (b.R - a.R) * amount),
            (int)Math.Round(a.G + (b.G - a.G) * amount),
            (int)Math.Round(a.B + (b.B - a.B) * amount));
    }

    public static Color WithAlpha(Color color, int alpha) => Color.FromArgb(Math.Clamp(alpha, 0, 255), color);

    public static double RelativeLuminance(Color color)
    {
        static double Channel(int value)
        {
            double s = value / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    /// <summary>WCAG 2.1 contrast ratio between two opaque colours.</summary>
    public static double Contrast(Color foreground, Color background)
    {
        double a = RelativeLuminance(foreground);
        double b = RelativeLuminance(background);
        if (a < b) (a, b) = (b, a);
        return (a + 0.05) / (b + 0.05);
    }

    /// <summary>Picks whichever of two inks reads better on the supplied background.</summary>
    public static Color InkFor(Color background)
        => Contrast(TextOnAccent, background) >= Contrast(TextPrimary, background) ? TextOnAccent : TextPrimary;
}
