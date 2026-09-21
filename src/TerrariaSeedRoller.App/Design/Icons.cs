using System.Drawing.Drawing2D;

namespace TerrariaSeedRoller.App.Design;

/// <summary>Every icon the interface can draw, all from one coherent family.</summary>
internal enum AppIcon
{
    Play,
    Pause,
    Stop,
    Search,
    FolderOpen,
    Folder,
    Sliders,
    Earth,
    ListChecks,
    Map,
    ScrollText,
    Plus,
    Trash,
    Check,
    Close,
    ChevronDown,
    Info,
    Dice,
    Copy,
    Warning,
    Clock,
    Reset,
    ExternalLink,
    Sparkles,
    Chart,
    Layers,
    Filter,
    Eye,
    FileText,
    Success,
    Error,
    Spinner,
    PanelLeft,
    Terminal,
    Download,
    Rocket,
    Shield,
    Wand,
    Mountain,
    Gem,
    ChevronUp,
    ChevronRight,
    ChevronLeft,
    Fit,
    ZoomIn,
    ZoomOut,
    Refresh,
    Save,
    Target
}

/// <summary>
/// Draws icons as real vector geometry, so they stay sharp at any display scale
/// and always share one stroke weight, cap style and join style.
/// </summary>
internal static class Icons
{
    private const float ViewBox = 24f;
    private const float DefaultStroke = 2f;

    private static readonly Dictionary<AppIcon, string> Names = new()
    {
        [AppIcon.Play] = "play",
        [AppIcon.Pause] = "pause",
        [AppIcon.Stop] = "square",
        [AppIcon.Search] = "search",
        [AppIcon.FolderOpen] = "folder-open",
        [AppIcon.Folder] = "folder",
        [AppIcon.Sliders] = "sliders-horizontal",
        [AppIcon.Earth] = "earth",
        [AppIcon.ListChecks] = "list-checks",
        [AppIcon.Map] = "map",
        [AppIcon.ScrollText] = "scroll-text",
        [AppIcon.Plus] = "plus",
        [AppIcon.Trash] = "trash-2",
        [AppIcon.Check] = "check",
        [AppIcon.Close] = "x",
        [AppIcon.ChevronDown] = "chevron-down",
        [AppIcon.Info] = "info",
        [AppIcon.Dice] = "dices",
        [AppIcon.Copy] = "copy",
        [AppIcon.Warning] = "triangle-alert",
        [AppIcon.Clock] = "clock",
        [AppIcon.Reset] = "rotate-ccw",
        [AppIcon.ExternalLink] = "external-link",
        [AppIcon.Sparkles] = "sparkles",
        [AppIcon.Chart] = "chart-column",
        [AppIcon.Layers] = "layers",
        [AppIcon.Filter] = "filter",
        [AppIcon.Eye] = "eye",
        [AppIcon.FileText] = "file-text",
        [AppIcon.Success] = "circle-check",
        [AppIcon.Error] = "circle-alert",
        [AppIcon.Spinner] = "loader-circle",
        [AppIcon.PanelLeft] = "panel-left",
        [AppIcon.Terminal] = "terminal",
        [AppIcon.Download] = "download",
        [AppIcon.Rocket] = "rocket",
        [AppIcon.Shield] = "shield-check",
        [AppIcon.Wand] = "wand-sparkles",
        [AppIcon.Mountain] = "mountain",
        [AppIcon.Gem] = "gem",
        [AppIcon.ChevronUp] = "chevron-up",
        [AppIcon.ChevronRight] = "chevron-right",
        [AppIcon.ChevronLeft] = "chevron-left",
        [AppIcon.Fit] = "maximize-2",
        [AppIcon.ZoomIn] = "zoom-in",
        [AppIcon.ZoomOut] = "zoom-out",
        [AppIcon.Refresh] = "refresh-cw",
        [AppIcon.Save] = "save",
        [AppIcon.Target] = "circle-dot"
    };

    private static readonly Dictionary<AppIcon, GraphicsPath> Cache = [];
    private static readonly object Gate = new();

    public static GraphicsPath Get(AppIcon icon)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(icon, out GraphicsPath? cached)) return cached;
            if (!Names.TryGetValue(icon, out string? name) || !IconData.Artwork.TryGetValue(name, out string[]? artwork))
            {
                throw new ArgumentOutOfRangeException(nameof(icon), icon, "No artwork is registered for this icon.");
            }

            GraphicsPath path = new() { FillMode = FillMode.Winding };
            foreach (string segment in artwork)
            {
                using GraphicsPath part = SvgPath.Parse(segment);
                path.AddPath(part, false);
            }
            Cache[icon] = path;
            return path;
        }
    }

    /// <summary>Draws the icon centred inside <paramref name="bounds"/> using the artwork's own stroke weight.</summary>
    public static void Draw(Graphics graphics, AppIcon icon, RectangleF bounds, Color color, float strokeWidth = DefaultStroke)
    {
        float extent = Math.Min(bounds.Width, bounds.Height);
        if (extent <= 0) return;

        GraphicsState state = graphics.Save();
        try
        {
            float scale = extent / ViewBox;
            graphics.TranslateTransform(
                bounds.X + (bounds.Width - extent) / 2f,
                bounds.Y + (bounds.Height - extent) / 2f);
            graphics.ScaleTransform(scale, scale);
            using Pen pen = new(color, strokeWidth)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
                MiterLimit = 2f
            };
            graphics.DrawPath(pen, Get(icon));
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    public static void Draw(Graphics graphics, AppIcon icon, Rectangle bounds, Color color, float strokeWidth = DefaultStroke)
        => Draw(graphics, icon, new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height), color, strokeWidth);

    /// <summary>Renders an icon to a fresh bitmap, used for window and taskbar imagery.</summary>
    public static Bitmap Render(AppIcon icon, int size, Color color, float strokeWidth = DefaultStroke)
    {
        Bitmap bitmap = new(size, size);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        Draw(graphics, icon, new RectangleF(0, 0, size, size), color, strokeWidth);
        return bitmap;
    }
}
