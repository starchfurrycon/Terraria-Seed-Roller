using System.Drawing.Drawing2D;
using TerrariaSeedRoller.App.Design;

namespace TerrariaSeedRoller.App.Controls;

/// <summary>A centred placeholder shown while a surface has nothing to display.</summary>
internal sealed class EmptyState : ThemedControl
{
    private string _title = string.Empty;
    private string _description = string.Empty;
    private AppIcon _icon = AppIcon.Sparkles;

    public EmptyState()
    {
        BackColor = Palette.Surface;
    }

    public string Title
    {
        get => _title;
        set { _title = value ?? string.Empty; Invalidate(); }
    }

    public string Description
    {
        get => _description;
        set { _description = value ?? string.Empty; Invalidate(); }
    }

    public AppIcon Icon
    {
        get => _icon;
        set { _icon = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(SurfaceColor);

        int icon = Metrics.Scale(this, 40);
        int titleHeight = Metrics.Scale(this, 26);
        int descriptionHeight = Metrics.Scale(this, 22);

        Size description = TextRenderer.MeasureText(_description, Typography.Body,
            new Size(Math.Max(Metrics.Scale(this, 120), Width - Metrics.Scale(this, 48)), int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        int descriptionLines = Math.Max(1, description.Height / descriptionHeight);

        int block = icon + Metrics.Scale(this, 12) + titleHeight + descriptionLines * descriptionHeight;
        int top = Math.Max(Metrics.Scale(this, 12), (Height - block) / 2);

        // A soft disc behind the glyph keeps the placeholder from looking empty.
        using (SolidBrush halo = new(Palette.WithAlpha(Palette.Accent, 16)))
        {
            graphics.FillEllipse(halo, (Width - icon * 2) / 2f, top - icon / 2f, icon * 2f, icon * 2f);
        }

        Icons.Draw(graphics, _icon, new Rectangle((Width - icon) / 2, top, icon, icon), Palette.WithAlpha(Palette.Accent, 190), 1.6f);

        int y = top + icon + Metrics.Scale(this, 12);
        TextRenderer.DrawText(graphics, _title, Typography.Section,
            new Rectangle(0, y, Width, titleHeight), Palette.TextSecondary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        y += titleHeight;
        TextRenderer.DrawText(graphics, _description, Typography.Body,
            new Rectangle(Metrics.Scale(this, 24), y, Math.Max(0, Width - Metrics.Scale(this, 48)), descriptionLines * descriptionHeight),
            Palette.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
    }
}
