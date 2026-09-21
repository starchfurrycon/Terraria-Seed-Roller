using System.Drawing.Drawing2D;
using TerrariaSeedRoller.App.Design;

namespace TerrariaSeedRoller.App.Controls;

internal enum ButtonVariant
{
    /// <summary>Filled accent button for the single most important action.</summary>
    Primary,
    /// <summary>Quiet filled button for ordinary actions.</summary>
    Secondary,
    /// <summary>Text and icon only, for tertiary actions.</summary>
    Ghost,
    /// <summary>Outlined destructive action.</summary>
    Danger,
    /// <summary>Filled positive action.</summary>
    Success
}

/// <summary>
/// A flat, fully owner-drawn button. Every visual state (rest, hover, pressed,
/// focused, disabled) is painted from the shared palette, so no system theme can
/// ever paint an unreadable combination.
/// </summary>
internal sealed class FlatButton : ThemedControl
{
    private bool _hover;
    private bool _pressed;
    private AppIcon? _icon;
    private ButtonVariant _variant = ButtonVariant.Secondary;
    private ContentAlignment _textAlign = ContentAlignment.MiddleCenter;

    public FlatButton()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        Size = new Size(Metrics.Scale(this, 96), Metrics.Scale(this, Metrics.ControlHeight));
    }

    public AppIcon? Icon
    {
        get => _icon;
        set { _icon = value; Invalidate(); }
    }

    public ButtonVariant Variant
    {
        get => _variant;
        set { _variant = value; Invalidate(); }
    }

    public ContentAlignment TextAlign
    {
        get => _textAlign;
        set { _textAlign = value; Invalidate(); }
    }

    public DialogResult DialogResult { get; set; } = DialogResult.None;

    /// <summary>When true the button sizes itself to its icon and text.</summary>
    public bool AutoSizeToContent { get; set; }

    public void PerformClick()
    {
        if (!Enabled) return;
        OnClick(EventArgs.Empty);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        if (AutoSizeToContent) ResizeToContent();
        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        if (AutoSizeToContent) ResizeToContent();
        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (AutoSizeToContent) ResizeToContent();
    }

    private void ResizeToContent()
    {
        Size measured = MeasureContent();
        Size = measured;
    }

    private Size MeasureContent()
    {
        int height = Metrics.Scale(this, Metrics.ControlHeight);
        int padding = Metrics.Scale(this, 14);
        int gap = Metrics.Scale(this, 7);
        int icon = IconSize;
        Size text = TextRenderer.MeasureText(string.IsNullOrEmpty(Text) ? " " : Text, Font, new Size(int.MaxValue, height), TextFormatFlags.NoPadding);
        int width = padding * 2 + text.Width;
        if (_icon is not null) width += icon + (text.Width > 0 ? gap : 0);
        return new Size(Math.Max(width, Metrics.Scale(this, 40)), height);
    }

    private int IconSize => Metrics.Scale(this, 16);

    public override Size GetPreferredSize(Size proposedSize) => MeasureContent();

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        _pressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            _pressed = true;
            Focus();
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _pressed = false;
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        if (!Enabled) { _hover = false; _pressed = false; }
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override bool IsInputKey(Keys keyData)
        => keyData is Keys.Space or Keys.Enter || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            _pressed = true;
            Invalidate();
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            _pressed = false;
            Invalidate();
            if (Enabled) OnClick(EventArgs.Empty);
            e.Handled = true;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(SurfaceColor);

        RectangleF bounds = Metrics.Hairline(0, 0, Width, Height);
        float radius = Metrics.ScaleF(this, Metrics.ControlRadius);

        (Color fill, Color border, Color ink) = ResolveColors();

        using (GraphicsPath path = Metrics.RoundedRect(bounds, radius))
        {
            using SolidBrush brush = new(fill);
            graphics.FillPath(brush, path);
            if (border.A > 0)
            {
                using Pen pen = new(border, 1f);
                graphics.DrawPath(pen, path);
            }
        }

        DrawContent(graphics, ink);

        if (Focused && Enabled)
        {
            using GraphicsPath path = Metrics.RoundedRect(
                new RectangleF(bounds.X + 1.5f, bounds.Y + 1.5f, bounds.Width - 3f, bounds.Height - 3f), radius - 1f);
            using Pen pen = new(Palette.WithAlpha(Palette.FocusRing, 170), 1.4f);
            graphics.DrawPath(pen, path);
        }
    }

    private (Color Fill, Color Border, Color Ink) ResolveColors()
    {
        if (!Enabled)
        {
            // A darker face keeps the disabled ink legible instead of fading it
            // into the surface behind the button.
            return _variant switch
            {
                ButtonVariant.Primary or ButtonVariant.Success => (Palette.SurfaceSunken, Color.Empty, Palette.TextDisabled),
                ButtonVariant.Ghost => (Color.Empty, Color.Empty, Palette.TextDisabled),
                _ => (Palette.SurfaceSunken, Palette.Border, Palette.TextDisabled)
            };
        }

        return _variant switch
        {
            ButtonVariant.Primary => (
                _pressed ? Palette.AccentPressed : _hover ? Palette.AccentHover : Palette.Accent,
                Color.Empty,
                Palette.TextOnAccent),

            ButtonVariant.Success => (
                _pressed ? Palette.SuccessPressed : _hover ? Palette.SuccessHover : Palette.Success,
                Color.Empty,
                Palette.TextOnAccent),

            ButtonVariant.Danger => (
                _pressed ? Palette.WithAlpha(Palette.Danger, 46) : _hover ? Palette.WithAlpha(Palette.Danger, 34) : Palette.WithAlpha(Palette.Danger, 16),
                Palette.WithAlpha(Palette.Danger, _hover ? 220 : 160),
                _hover || _pressed ? Palette.DangerHover : Palette.Danger),

            ButtonVariant.Ghost => (
                _pressed ? Palette.SurfaceActive : _hover ? Palette.SurfaceHover : Color.Empty,
                Color.Empty,
                _hover || _pressed ? Palette.TextPrimary : Palette.TextSecondary),

            _ => (
                _pressed ? Palette.SurfaceActive : _hover ? Palette.SurfaceHover : Palette.SurfaceRaised,
                _hover || _pressed ? Palette.BorderStrong : Palette.Border,
                Palette.TextPrimary)
        };
    }

    private void DrawContent(Graphics graphics, Color ink)
    {
        int iconSize = IconSize;
        int gap = Metrics.Scale(this, 7);
        bool hasIcon = _icon is not null;
        Size textSize = TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, Height), TextFormatFlags.NoPadding);

        int contentWidth = textSize.Width + (hasIcon ? iconSize + (textSize.Width > 0 ? gap : 0) : 0);
        int startX = _textAlign switch
        {
            ContentAlignment.MiddleLeft or ContentAlignment.TopLeft or ContentAlignment.BottomLeft => Metrics.Scale(this, 12),
            ContentAlignment.MiddleRight or ContentAlignment.TopRight or ContentAlignment.BottomRight => Width - contentWidth - Metrics.Scale(this, 12),
            _ => (Width - contentWidth) / 2
        };

        int centreY = Height / 2;
        if (hasIcon)
        {
            Rectangle iconBounds = new(startX, centreY - iconSize / 2, iconSize, iconSize);
            Icons.Draw(graphics, _icon!.Value, iconBounds, ink);
            startX += iconSize + (textSize.Width > 0 ? gap : 0);
        }

        if (textSize.Width > 0)
        {
            Rectangle textBounds = new(startX, 0, textSize.Width + 2, Height);
            TextRenderer.DrawText(graphics, Text, Font, textBounds, ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }
    }
}
