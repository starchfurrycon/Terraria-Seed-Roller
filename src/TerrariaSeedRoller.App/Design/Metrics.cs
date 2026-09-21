namespace TerrariaSeedRoller.App.Design;

/// <summary>
/// Layout constants expressed at 96 DPI. Controls scale them through
/// <see cref="Scale(Control, int)"/> so the geometry stays correct on high-DPI displays.
/// </summary>
internal static class Metrics
{
    public const int ControlHeight = 32;
    public const int ControlRadius = 6;
    public const int CardRadius = 10;
    public const int CardPadding = 16;
    public const int RowGap = 8;
    public const int SectionGap = 18;
    public const int LabelColumn = 94;
    public const int ScrollBarWidth = 10;

    public static int Scale(Control control, int value)
        => (int)Math.Round(value * control.DeviceDpi / 96.0, MidpointRounding.AwayFromZero);

    public static float ScaleF(Control control, float value) => value * control.DeviceDpi / 96f;

    public static Padding Scale(Control control, Padding padding) => new(
        Scale(control, padding.Left),
        Scale(control, padding.Top),
        Scale(control, padding.Right),
        Scale(control, padding.Bottom));

    /// <summary>Builds a rounded-rectangle path used by every card, button and input.</summary>
    public static System.Drawing.Drawing2D.GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        System.Drawing.Drawing2D.GraphicsPath path = new();
        if (radius <= 0.5f)
        {
            path.AddRectangle(bounds);
            return path;
        }

        float d = Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height));
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>A 1px hairline that stays crisp instead of blurring across two pixels.</summary>
    public static RectangleF Hairline(float x, float y, float width, float height)
        => new(x + 0.5f, y + 0.5f, Math.Max(0, width - 1f), Math.Max(0, height - 1f));
}
