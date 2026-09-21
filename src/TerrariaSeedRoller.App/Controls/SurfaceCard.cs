using System.Drawing.Drawing2D;
using TerrariaSeedRoller.App.Design;

namespace TerrariaSeedRoller.App.Controls;

/// <summary>
/// A rounded surface with an optional icon/title/subtitle header. Docked children
/// are automatically laid out inside the padded content area because
/// <see cref="DisplayRectangle"/> is overridden.
/// </summary>
internal sealed class SurfaceCard : ThemedControl
{
    private string _title = string.Empty;
    private string _subtitle = string.Empty;
    private AppIcon? _icon;
    private int _padding = Metrics.CardPadding;

    public SurfaceCard()
    {
        BackColor = Palette.Surface;
        ForeColor = Palette.TextPrimary;
    }

    public string Title
    {
        get => _title;
        set { _title = value ?? string.Empty; PerformLayout(); Invalidate(); }
    }

    public string Subtitle
    {
        get => _subtitle;
        set { _subtitle = value ?? string.Empty; PerformLayout(); Invalidate(); }
    }

    public AppIcon? HeaderIcon
    {
        get => _icon;
        set { _icon = value; PerformLayout(); Invalidate(); }
    }

    /// <summary>Extra space reserved at the top-right of the header for a toolbar.</summary>
    public int HeaderToolbarWidth { get; set; }

    public int ContentPadding
    {
        get => _padding;
        set { _padding = value; PerformLayout(); Invalidate(); }
    }

    public bool HasHeader => _title.Length > 0 || _subtitle.Length > 0 || _icon is not null;

    public int HeaderHeight
    {
        get
        {
            if (!HasHeader) return 0;
            int baseHeight = Metrics.Scale(this, _subtitle.Length > 0 ? 46 : 34);
            return baseHeight;
        }
    }

    public override Rectangle DisplayRectangle
    {
        get
        {
            int pad = Metrics.Scale(this, _padding);
            Rectangle area = ClientRectangle;
            area = new Rectangle(area.X + pad, area.Y + pad, Math.Max(0, area.Width - pad * 2), Math.Max(0, area.Height - pad * 2));
            if (HasHeader)
            {
                int header = HeaderHeight;
                area = new Rectangle(area.X, area.Y + header, area.Width, Math.Max(0, area.Height - header));
            }
            return area;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(SurfaceColor);

        RectangleF bounds = Metrics.Hairline(0, 0, Width, Height);
        float radius = Metrics.ScaleF(this, Metrics.CardRadius);
        using (GraphicsPath path = Metrics.RoundedRect(bounds, radius))
        {
            using SolidBrush brush = new(Palette.Surface);
            graphics.FillPath(brush, path);
            using Pen pen = new(Palette.Border, 1f);
            graphics.DrawPath(pen, path);
        }

        if (!HasHeader) return;

        int pad = Metrics.Scale(this, _padding);
        int iconSize = Metrics.Scale(this, 20);
        int textLeft = pad;
        int top = pad;

        if (_icon is not null)
        {
            Icons.Draw(graphics, _icon.Value, new Rectangle(pad, top + Metrics.Scale(this, 1), iconSize, iconSize), Palette.Accent);
            textLeft = pad + iconSize + Metrics.Scale(this, 9);
        }

        int available = Math.Max(0, Width - textLeft - pad - Metrics.Scale(this, HeaderToolbarWidth));
        if (_subtitle.Length > 0)
        {
            TextRenderer.DrawText(graphics, _title, Typography.Title,
                new Rectangle(textLeft, top, available, Metrics.Scale(this, 22)), Palette.TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(graphics, _subtitle, Typography.Small,
                new Rectangle(textLeft, top + Metrics.Scale(this, 21), available, Metrics.Scale(this, 18)), Palette.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
        else
        {
            TextRenderer.DrawText(graphics, _title, Typography.Section,
                new Rectangle(textLeft, top, available, Metrics.Scale(this, 22)), Palette.TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }
}
