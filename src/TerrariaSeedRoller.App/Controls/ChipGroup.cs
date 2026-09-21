using System.Drawing.Drawing2D;
using TerrariaSeedRoller.App.Design;

namespace TerrariaSeedRoller.App.Controls;

/// <summary>A single toggleable pill. Compact: the glyph only appears when checked.</summary>
internal sealed class Chip : ThemedControl
{
    private bool _checked;
    private bool _hover;

    public Chip(string label, object value)
    {
        Label = label;
        Value = value;
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        Font = Typography.Small;
        Height = Metrics.Scale(this, 26);
        Width = MeasureWidth();
    }

    public string Label { get; }

    public object Value { get; }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Width = MeasureWidth();
            Invalidate();
        }
    }

    private int MeasureWidth()
    {
        Size text = TextRenderer.MeasureText(Label, Font, new Size(int.MaxValue, Height), TextFormatFlags.NoPadding);
        int width = Metrics.Scale(this, 10) + text.Width + Metrics.Scale(this, 10);
        if (_checked) width += Metrics.Scale(this, 13) + Metrics.Scale(this, 5);
        return width;
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location))
        {
            Checked = !Checked;
            (Parent as ChipGroup)?.NotifyChanged();
        }
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode == Keys.Space)
        {
            Checked = !Checked;
            (Parent as ChipGroup)?.NotifyChanged();
            e.Handled = true;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(SurfaceColor);

        RectangleF bounds = Metrics.Hairline(0, 0, Width, Height);
        float radius = bounds.Height / 2f;

        Color fill;
        Color border;
        Color ink;
        if (_checked)
        {
            fill = Palette.WithAlpha(Palette.Accent, _hover ? 56 : 38);
            border = Palette.Accent;
            ink = Palette.AccentHover;
        }
        else
        {
            fill = _hover ? Palette.SurfaceHover : Palette.SurfaceInput;
            border = _hover ? Palette.BorderStrong : Palette.Border;
            ink = _hover ? Palette.TextPrimary : Palette.TextSecondary;
        }

        using (GraphicsPath path = Metrics.RoundedRect(bounds, radius))
        {
            using SolidBrush brush = new(fill);
            graphics.FillPath(brush, path);
            using Pen pen = new(border, 1f);
            graphics.DrawPath(pen, path);
        }

        int left = Metrics.Scale(this, 10);
        if (_checked)
        {
            int icon = Metrics.Scale(this, 13);
            Icons.Draw(graphics, AppIcon.Check,
                new Rectangle(left, (Height - icon) / 2, icon, icon), ink, 3f);
            left += icon + Metrics.Scale(this, 5);
        }

        TextRenderer.DrawText(graphics, Label, Font,
            new Rectangle(left, 0, Math.Max(0, Width - left - Metrics.Scale(this, 8)), Height), ink,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

        if (Focused)
        {
            using GraphicsPath path = Metrics.RoundedRect(
                new RectangleF(bounds.X + 1.5f, bounds.Y + 1.5f, bounds.Width - 3f, bounds.Height - 3f), radius - 1.5f);
            using Pen pen = new(Palette.WithAlpha(Palette.FocusRing, 150), 1.2f);
            graphics.DrawPath(pen, path);
        }
    }
}

/// <summary>
/// A wrapping group of <see cref="Chip"/> toggles. It is deliberately not
/// auto-sized: the owning <see cref="FieldRow"/> asks for the wrapped height at
/// a known width, so the row can never be sized from a stale measurement.
/// </summary>
internal sealed class ChipGroup : FlowLayoutPanel
{
    public ChipGroup()
    {
        AutoSize = false;
        WrapContents = true;
        BackColor = Palette.Canvas;
        Margin = Padding.Empty;
    }

    public event EventHandler? SelectionChanged;

    public IReadOnlyList<Chip> Chips => Controls.OfType<Chip>().ToList();

    protected override void OnPaintBackground(PaintEventArgs e)
        => e.Graphics.Clear(ThemedControl.ResolveSurface(this));

    public void SetItems(IEnumerable<(string Label, object Value)> items, Func<object, bool> isChecked)
    {
        SuspendLayout();
        foreach (Chip existing in Controls.OfType<Chip>().ToList())
        {
            Controls.Remove(existing);
            existing.Dispose();
        }

        int gap = Metrics.Scale(this, 6);
        foreach ((string label, object value) in items)
        {
            Chip chip = new(label, value)
            {
                Checked = isChecked(value),
                Margin = new Padding(0, 0, gap, gap)
            };
            Controls.Add(chip);
        }
        ResumeLayout(true);
    }

    public IReadOnlyList<object> CheckedValues => Controls.OfType<Chip>().Where(c => c.Checked).Select(c => c.Value).ToList();

    public void SetAllChecked(bool value)
    {
        foreach (Chip chip in Controls.OfType<Chip>()) chip.Checked = value;
        NotifyChanged();
    }

    /// <summary>Height needed to wrap every chip inside <paramref name="width"/>.</summary>
    public int WrappedHeight(int width)
    {
        if (width <= 0) return 0;
        int x = 0;
        int y = 0;
        int rowHeight = 0;
        foreach (Chip chip in Controls.OfType<Chip>())
        {
            int outer = chip.Width + chip.Margin.Horizontal;
            if (x > 0 && x + outer > width)
            {
                y += rowHeight;
                x = 0;
                rowHeight = 0;
            }
            x += outer;
            rowHeight = Math.Max(rowHeight, chip.Height + chip.Margin.Vertical);
        }
        return y + rowHeight;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        if (width <= 0) return new Size(0, 0);
        return new Size(width, WrappedHeight(width));
    }

    internal void NotifyChanged() => SelectionChanged?.Invoke(this, EventArgs.Empty);
}
