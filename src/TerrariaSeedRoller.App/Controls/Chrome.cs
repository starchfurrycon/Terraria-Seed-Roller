using System.Drawing.Drawing2D;
using TerrariaSeedRoller.App.Design;

namespace TerrariaSeedRoller.App.Controls;

/// <summary>The application title bar strip.</summary>
internal sealed class HeaderBar : ThemedControl
{
    public HeaderBar(string title, string subtitle, string badge)
    {
        Title = title;
        Subtitle = subtitle;
        Badge = badge;
        BackColor = Palette.Canvas;
    }

    public string Title { get; }

    public string Subtitle { get; }

    public string Badge { get; }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(SurfaceColor);

        int tile = Metrics.Scale(this, 40);
        int pad = Metrics.Scale(this, 18);
        int top = (Height - tile) / 2;
        RectangleF tileRect = new(pad + 0.5f, top + 0.5f, tile - 1f, tile - 1f);

        using (GraphicsPath path = Metrics.RoundedRect(tileRect, Metrics.ScaleF(this, 10)))
        {
            using LinearGradientBrush brush = new(tileRect, Palette.SurfaceRaised, Palette.SurfaceInput, LinearGradientMode.Vertical);
            graphics.FillPath(brush, path);
            using Pen pen = new(Palette.BorderStrong, 1f);
            graphics.DrawPath(pen, path);
        }

        int glyph = Metrics.Scale(this, 22);
        Icons.Draw(graphics, AppIcon.Dice,
            new Rectangle(pad + (tile - glyph) / 2, top + (tile - glyph) / 2, glyph, glyph), Palette.Accent);

        int textLeft = pad + tile + Metrics.Scale(this, 12);
        TextRenderer.DrawText(graphics, Title, Typography.Display,
            new Rectangle(textLeft, top - Metrics.Scale(this, 2), Math.Max(0, Width - textLeft - pad), Metrics.Scale(this, 26)),
            Palette.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        TextRenderer.DrawText(graphics, Subtitle, Typography.Small,
            new Rectangle(textLeft, top + Metrics.Scale(this, 22), Math.Max(0, Width - textLeft - pad), Metrics.Scale(this, 18)),
            Palette.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

        if (Badge.Length > 0)
        {
            Size size = TextRenderer.MeasureText(Badge, Typography.Small, new Size(int.MaxValue, Height), TextFormatFlags.NoPadding);
            int width = size.Width + Metrics.Scale(this, 20);
            int height = Metrics.Scale(this, 26);
            RectangleF badgeRect = new(Width - pad - width, (Height - height) / 2f, width, height);
            using (GraphicsPath path = Metrics.RoundedRect(badgeRect, height / 2f))
            {
                using SolidBrush brush = new(Palette.WithAlpha(Palette.Accent, 26));
                graphics.FillPath(brush, path);
                using Pen pen = new(Palette.WithAlpha(Palette.Accent, 110), 1f);
                graphics.DrawPath(pen, path);
            }
            TextRenderer.DrawText(graphics, Badge, Typography.Small,
                new Rectangle((int)badgeRect.X, (int)badgeRect.Y, width, height), Palette.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        using Pen separator = new(Palette.Border, 1f);
        graphics.DrawLine(separator, 0, Height - 0.5f, Width, Height - 0.5f);
    }
}

/// <summary>The bottom bar holding the status readout, progress and primary actions.</summary>
internal sealed class FooterBar : ThemedControl
{
    private readonly FlowLayoutPanel _buttons = new()
    {
        FlowDirection = FlowDirection.RightToLeft,
        WrapContents = false,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        BackColor = Palette.Canvas
    };

    private string _statusText = "就绪";
    private AppIcon _statusIcon = AppIcon.Info;
    private Color _statusColor = Palette.TextSecondary;

    public FooterBar()
    {
        BackColor = Palette.Canvas;
        Controls.Add(Progress);
        Controls.Add(_buttons);
    }

    public FlatProgressBar Progress { get; } = new();

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value ?? string.Empty; Invalidate(); }
    }

    public AppIcon StatusIcon
    {
        get => _statusIcon;
        set { _statusIcon = value; Invalidate(); }
    }

    public Color StatusColor
    {
        get => _statusColor;
        set { _statusColor = value; Invalidate(); }
    }

    public void AddButton(Control button, int gap = 8)
    {
        button.Margin = new Padding(Metrics.Scale(this, gap), 0, 0, 0);
        _buttons.Controls.Add(button);
    }

    public int ButtonStripWidth => _buttons.PreferredSize.Width;

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int pad = Metrics.Scale(this, 18);
        Size preferred = _buttons.PreferredSize;
        int buttonHeight = Metrics.Scale(this, 36);
        _buttons.SetBounds(
            Math.Max(pad, ClientSize.Width - pad - preferred.Width),
            (ClientSize.Height - buttonHeight) / 2,
            preferred.Width,
            buttonHeight);

        int progressLeft = pad + Metrics.Scale(this, 22);
        int progressRight = Math.Max(progressLeft + Metrics.Scale(this, 80), _buttons.Left - Metrics.Scale(this, 24));
        Progress.SetBounds(progressLeft, ClientSize.Height / 2 + Metrics.Scale(this, 8), Math.Max(Metrics.Scale(this, 60), progressRight - progressLeft), Metrics.Scale(this, 8));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(SurfaceColor);

        using (Pen separator = new(Palette.Border, 1f))
        {
            graphics.DrawLine(separator, 0, 0.5f, Width, 0.5f);
        }

        int pad = Metrics.Scale(this, 18);
        int icon = Metrics.Scale(this, 15);
        int textTop = ClientSize.Height / 2 - Metrics.Scale(this, 20);
        Icons.Draw(graphics, _statusIcon, new Rectangle(pad, textTop + Metrics.Scale(this, 3), icon, icon), _statusColor, 2.2f);

        int textLeft = pad + icon + Metrics.Scale(this, 7);
        int textRight = Math.Max(textLeft + Metrics.Scale(this, 60), _buttons.Left - Metrics.Scale(this, 24));
        TextRenderer.DrawText(graphics, _statusText, Typography.Body,
            new Rectangle(textLeft, textTop, textRight - textLeft, Metrics.Scale(this, 22)), _statusColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }
}
