using System.Drawing.Drawing2D;
using TerrariaSeedRoller.App.Design;

namespace TerrariaSeedRoller.App.Controls;

/// <summary>
/// A deterministic top-down stack. Unlike a <c>TableLayoutPanel</c> it never
/// infers row indices, so rows cannot drift out of alignment.
/// </summary>
internal sealed class VerticalStack : ThemedControl
{
    private readonly List<Control> _ordered = [];

    public VerticalStack()
    {
        BackColor = Palette.Canvas;
    }

    public int Gap { get; set; } = Metrics.RowGap;

    public IReadOnlyList<Control> Ordered => _ordered;

    public void Add(Control child)
    {
        ArgumentNullException.ThrowIfNull(child);
        _ordered.Add(child);
        Controls.Add(child);
        UpdateHeight();
    }

    public void AddRange(params Control[] children)
    {
        foreach (Control child in children) Add(child);
    }

    public void Clear()
    {
        foreach (Control child in _ordered)
        {
            Controls.Remove(child);
            child.Dispose();
        }
        _ordered.Clear();
        UpdateHeight();
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        return new Size(width, MeasureHeight(width));
    }

    private int MeasureHeight(int width)
    {
        int gap = Metrics.Scale(this, Gap);
        int total = 0;
        for (int i = 0; i < _ordered.Count; i++)
        {
            total += HeightOf(_ordered[i], width);
            if (i < _ordered.Count - 1) total += gap;
        }
        return total;
    }

    private int HeightOf(Control child, int width)
    {
        if (child is null) return 0;
        return Math.Max(child.Height, child.GetPreferredSize(new Size(width, 0)).Height);
    }

    private void UpdateHeight() => SetHeight(ClientSize.Width);

    private void SetHeight(int width)
    {
        int target = MeasureHeight(width);
        if (Height != target) Height = target;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        LayoutChildren();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutChildren();
    }

    private void LayoutChildren()
    {
        int gap = Metrics.Scale(this, Gap);
        int width = ClientSize.Width;
        int y = 0;
        foreach (Control child in _ordered)
        {
            int height = HeightOf(child, width);
            if (child.Bounds.X != 0 || child.Bounds.Y != y || child.Width != width || child.Height != height)
            {
                child.SetBounds(0, y, width, height);
            }
            y += height + gap;
        }

        int target = Math.Max(0, y - (_ordered.Count > 0 ? gap : 0));
        if (Height != target) Height = target;
    }
}

/// <summary>A fixed-height gap in a <see cref="VerticalStack"/>.</summary>
internal sealed class Spacer : ThemedControl
{
    public Spacer(int height)
    {
        Height = Metrics.Scale(this, height);
        BackColor = Palette.Canvas;
    }
}

/// <summary>A section caption with an icon, an optional hint and a hairline.</summary>
internal sealed class SectionHeader : ThemedControl
{
    public SectionHeader(string title, AppIcon icon, string hint = "")
    {
        Title = title;
        Icon = icon;
        Hint = hint;
        Height = Metrics.Scale(this, 34);
        BackColor = Palette.Canvas;
    }

    public string Title { get; }

    public string Hint { get; }

    public AppIcon Icon { get; }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(SurfaceColor);

        int icon = Metrics.Scale(this, 16);
        int top = Metrics.Scale(this, 6);
        Icons.Draw(graphics, Icon, new Rectangle(0, top + Metrics.Scale(this, 1), icon, icon), Palette.Accent);

        int left = icon + Metrics.Scale(this, 8);
        TextRenderer.DrawText(graphics, Title, Typography.Section,
            new Rectangle(left, 0, Math.Max(0, Width - left), Height), Palette.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        if (Hint.Length > 0)
        {
            Size titleSize = TextRenderer.MeasureText(Title, Typography.Section, new Size(int.MaxValue, Height), TextFormatFlags.NoPadding);
            int hintLeft = left + titleSize.Width + Metrics.Scale(this, 10);
            TextRenderer.DrawText(graphics, Hint, Typography.Small,
                new Rectangle(hintLeft, 0, Math.Max(0, Width - hintLeft), Height), Palette.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        using Pen pen = new(Palette.Border, 1f);
        graphics.DrawLine(pen, 0, Height - 0.5f, Width, Height - 0.5f);
    }
}

/// <summary>
/// A labelled form row: right-aligned caption, the field itself, an optional
/// trailing button and an optional wrapped hint underneath. The row measures
/// itself from an explicit width so wrapping fields never collapse.
/// </summary>
internal sealed class FieldRow : ThemedControl
{
    private string _hint = string.Empty;

    public FieldRow(string label, Control field, int labelWidth)
    {
        Label = label;
        Field = field;
        LabelWidth = labelWidth;
        BackColor = Palette.Canvas;
        Controls.Add(field);
        SetHeight(Width);
    }

    public string Label { get; }

    public int LabelWidth { get; set; }

    // Assigned in the constructor body, so it can still be null while the base
    // constructor raises a layout pass.
    public Control Field { get; private set; } = null!;

    public Control? Trailing { get; init; }

    public int TrailingWidth { get; init; }

    public int FieldHeight { get; init; } = Metrics.ControlHeight;

    /// <summary>When true the row grows to whatever height the field prefers (used for wrapping fields).</summary>
    public bool AutoHeightFromField { get; init; }

    public string Hint
    {
        get => _hint;
        set { _hint = value ?? string.Empty; SetHeight(Width); Invalidate(); }
    }

    private int ScaledLabelWidth => Metrics.Scale(this, LabelWidth);

    private int ScaledFieldHeight => Metrics.Scale(this, FieldHeight);

    private int ScaledTrailingWidth => Trailing is null ? 0 : Metrics.Scale(this, TrailingWidth) + Metrics.Scale(this, 8);

    private int FieldAreaWidth(int width)
        => Math.Max(Metrics.Scale(this, 80), width - ScaledLabelWidth - ScaledTrailingWidth);

    private int EffectiveFieldHeight(int width)
        => AutoHeightFromField && Field is not null
            ? Math.Max(ScaledFieldHeight, Field.GetPreferredSize(new Size(FieldAreaWidth(width), 0)).Height)
            : ScaledFieldHeight;

    private int HintHeight(int width)
    {
        if (_hint.Length == 0) return 0;
        Size size = TextRenderer.MeasureText(_hint, Typography.Small,
            new Size(Math.Max(Metrics.Scale(this, 80), width - ScaledLabelWidth), int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        return size.Height + Metrics.Scale(this, 4);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        return new Size(width, MeasureRowHeight(width));
    }

    private int MeasureRowHeight(int width)
    {
        int row = Math.Max(EffectiveFieldHeight(width), Metrics.Scale(this, 20));
        if (Trailing is not null) row = Math.Max(row, Trailing.Height);
        return row + HintHeight(width);
    }

    private void SetHeight(int width)
    {
        int target = MeasureRowHeight(width);
        if (Height != target) Height = target;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (Field is null) return;

        int width = ClientSize.Width;
        int fieldHeight = EffectiveFieldHeight(width);
        int row = Math.Max(fieldHeight, Metrics.Scale(this, 20));
        int labelWidth = ScaledLabelWidth;
        int trailing = ScaledTrailingWidth;

        int fieldTop = (row - fieldHeight) / 2;
        Field.SetBounds(labelWidth, fieldTop, Math.Max(0, width - labelWidth - trailing), fieldHeight);

        if (Trailing is not null)
        {
            int trailingWidth = Metrics.Scale(this, TrailingWidth);
            int height = Math.Max(Trailing.Height, Math.Min(fieldHeight, ScaledFieldHeight));
            Trailing.SetBounds(width - trailingWidth, (row - height) / 2, trailingWidth, height);
        }

        SetHeight(width);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.Clear(SurfaceColor);

        int row = Math.Max(EffectiveFieldHeight(Width), Metrics.Scale(this, 20));
        int labelWidth = Math.Max(0, ScaledLabelWidth - Metrics.Scale(this, 10));
        Rectangle labelBounds = AutoHeightFromField
            ? new Rectangle(0, Metrics.Scale(this, 7), labelWidth, row)
            : new Rectangle(0, 0, labelWidth, row);
        TextFormatFlags flags = TextFormatFlags.Right | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding
            | (AutoHeightFromField ? TextFormatFlags.Top : TextFormatFlags.VerticalCenter);

        TextRenderer.DrawText(graphics, Label, Typography.Body, labelBounds, Palette.TextSecondary, flags);

        if (_hint.Length > 0)
        {
            TextRenderer.DrawText(graphics, _hint, Typography.Small,
                new Rectangle(ScaledLabelWidth, row + Metrics.Scale(this, 2), Math.Max(0, Width - ScaledLabelWidth), HintHeight(Width)),
                Palette.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }
    }
}
