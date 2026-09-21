using System.Drawing.Drawing2D;
using System.Globalization;
using TerrariaSeedRoller.App.Design;
using TerrariaSeedRoller.Core;

namespace TerrariaSeedRoller.App.Controls;

/// <summary>
/// The criteria editor. Each criterion is a card-like row showing its label,
/// metric key, kind badge and threshold, with an inline enable toggle and a
/// delete affordance. Editing happens in a dedicated dialog, which keeps every
/// field on a control that the application itself paints.
/// </summary>
internal sealed class CriterionList : ThemedControl
{
    private readonly List<CriterionDefinition> _items = [];
    private int _selectedIndex = -1;
    private int _hoverIndex = -1;
    private bool _hoverToggle;
    private bool _hoverDelete;
    private int _scrollOffset;
    private bool _dragging;
    private int _dragStartY;
    private int _dragStartOffset;

    public CriterionList()
    {
        BackColor = Palette.Surface;
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
    }

    public event EventHandler? SelectionChanged;
    public event EventHandler? ItemsChanged;
    public event EventHandler? ItemActivated;

    public IReadOnlyList<CriterionDefinition> Items => _items;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            int clamped = _items.Count == 0 ? -1 : Math.Clamp(value, -1, _items.Count - 1);
            if (clamped == _selectedIndex) return;
            _selectedIndex = clamped;
            EnsureVisible(_selectedIndex);
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public CriterionDefinition? SelectedItem => _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : null;

    public void SetItems(IEnumerable<CriterionDefinition> items, int selectIndex = 0)
    {
        _items.Clear();
        _items.AddRange(items);
        _scrollOffset = 0;
        _selectedIndex = _items.Count == 0 ? -1 : Math.Clamp(selectIndex, -1, _items.Count - 1);
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ReplaceSelected(CriterionDefinition definition)
    {
        if (_selectedIndex < 0) return;
        _items[_selectedIndex] = definition;
        Invalidate();
        ItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Add(CriterionDefinition definition)
    {
        _items.Add(definition);
        _selectedIndex = _items.Count - 1;
        EnsureVisible(_selectedIndex);
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        ItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveSelected()
    {
        if (_selectedIndex < 0) return;
        _items.RemoveAt(_selectedIndex);
        _selectedIndex = _items.Count == 0 ? -1 : Math.Min(_selectedIndex, _items.Count - 1);
        EnsureVisible(_selectedIndex);
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        ItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MoveSelected(int delta)
    {
        if (_selectedIndex < 0) return;
        int target = _selectedIndex + delta;
        if (target < 0 || target >= _items.Count) return;
        (_items[_selectedIndex], _items[target]) = (_items[target], _items[_selectedIndex]);
        _selectedIndex = target;
        EnsureVisible(_selectedIndex);
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        ItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleSelectedEnabled()
    {
        if (_selectedIndex < 0) return;
        CriterionDefinition item = _items[_selectedIndex];
        _items[_selectedIndex] = item with { Enabled = !item.Enabled };
        Invalidate();
        ItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    public int RowHeight => Metrics.Scale(this, 58);

    private int BarWidth => Metrics.Scale(this, Metrics.ScrollBarWidth);

    private bool NeedsBar => _items.Count * RowHeight > ClientSize.Height;

    private int MaxOffset => Math.Max(0, _items.Count * RowHeight - ClientSize.Height);

    private int ContentWidth => Math.Max(0, ClientSize.Width - (NeedsBar ? BarWidth : 0));

    private Rectangle ToggleBounds(int index) => new(Metrics.Scale(this, 12), RowTop(index) + (RowHeight - Metrics.Scale(this, 18)) / 2, Metrics.Scale(this, 18), Metrics.Scale(this, 18));

    private Rectangle DeleteBounds(int index) => new(ContentWidth - Metrics.Scale(this, 34), RowTop(index) + (RowHeight - Metrics.Scale(this, 24)) / 2, Metrics.Scale(this, 24), Metrics.Scale(this, 24));

    private int RowTop(int index) => index * RowHeight - _scrollOffset;

    private int IndexAt(Point location)
    {
        int index = (location.Y + _scrollOffset) / RowHeight;
        return index >= 0 && index < _items.Count ? index : -1;
    }

    private void EnsureVisible(int index)
    {
        if (index < 0) return;
        int top = index * RowHeight;
        if (top < _scrollOffset) _scrollOffset = top;
        else if (top + RowHeight > _scrollOffset + ClientSize.Height) _scrollOffset = top + RowHeight - ClientSize.Height;
        _scrollOffset = Math.Clamp(_scrollOffset, 0, MaxOffset);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        int next = Math.Clamp(_scrollOffset - e.Delta / 120 * RowHeight, 0, MaxOffset);
        if (next == _scrollOffset) return;
        _scrollOffset = next;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            int track = Math.Max(1, ClientSize.Height);
            int thumb = ThumbHeight();
            int travel = Math.Max(1, track - thumb);
            int next = Math.Clamp(_dragStartOffset + (e.Y - _dragStartY) * MaxOffset / travel, 0, MaxOffset);
            if (next != _scrollOffset) { _scrollOffset = next; Invalidate(); }
            return;
        }

        int index = IndexAt(e.Location);
        bool toggle = index >= 0 && ToggleBounds(index).Contains(e.Location);
        bool delete = index >= 0 && DeleteBounds(index).Contains(e.Location);
        if (index != _hoverIndex || toggle != _hoverToggle || delete != _hoverDelete)
        {
            _hoverIndex = index;
            _hoverToggle = toggle;
            _hoverDelete = delete;
            Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverIndex = -1;
        _hoverToggle = _hoverDelete = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (NeedsBar && e.X >= ClientSize.Width - BarWidth)
        {
            int thumb = ThumbHeight();
            int travel = Math.Max(1, ClientSize.Height - thumb);
            int position = MaxOffset == 0 ? 0 : travel * _scrollOffset / MaxOffset;
            if (e.Y >= position && e.Y <= position + thumb)
            {
                _dragging = true;
                _dragStartY = e.Y;
                _dragStartOffset = _scrollOffset;
            }
            else
            {
                int next = Math.Clamp(_scrollOffset + (e.Y < position ? -ClientSize.Height : ClientSize.Height), 0, MaxOffset);
                if (next != _scrollOffset) { _scrollOffset = next; Invalidate(); }
            }
            return;
        }

        int index = IndexAt(e.Location);
        if (index < 0) return;

        if (ToggleBounds(index).Contains(e.Location))
        {
            CriterionDefinition item = _items[index];
            _items[index] = item with { Enabled = !item.Enabled };
            SelectedIndex = index;
            ItemsChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
            return;
        }

        if (DeleteBounds(index).Contains(e.Location))
        {
            SelectedIndex = index;
            RemoveSelected();
            return;
        }

        SelectedIndex = index;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (IndexAt(e.Location) >= 0) ItemActivated?.Invoke(this, EventArgs.Empty);
    }

    protected override bool IsInputKey(Keys keyData)
        => keyData is Keys.Up or Keys.Down or Keys.Enter or Keys.Delete or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Up:
                SelectedIndex = Math.Max(0, _selectedIndex - 1);
                e.Handled = true;
                break;
            case Keys.Down:
                SelectedIndex = Math.Min(_items.Count - 1, _selectedIndex + 1);
                e.Handled = true;
                break;
            case Keys.Space:
                ToggleSelectedEnabled();
                e.Handled = true;
                break;
            case Keys.Delete:
                RemoveSelected();
                e.Handled = true;
                break;
            case Keys.Enter:
                if (_selectedIndex >= 0) ItemActivated?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
        }
    }

    private int ThumbHeight()
        => Math.Max(Metrics.Scale(this, 32), (int)((long)ClientSize.Height * ClientSize.Height / Math.Max(1, _items.Count * RowHeight)));

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(SurfaceColor);

        if (_items.Count == 0)
        {
            Icons.Draw(graphics, AppIcon.Filter, new Rectangle((Width - Metrics.Scale(this, 34)) / 2, Height / 2 - Metrics.Scale(this, 44), Metrics.Scale(this, 34), Metrics.Scale(this, 34)), Palette.WithAlpha(Palette.TextMuted, 140), 1.6f);
            TextRenderer.DrawText(graphics, "这个预设没有任何筛选条件", Typography.Body,
                new Rectangle(0, Height / 2 - Metrics.Scale(this, 4), Width, Metrics.Scale(this, 22)), Palette.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(graphics, "点击“添加指标”创建第一条硬条件或加权条件", Typography.Small,
                new Rectangle(0, Height / 2 + Metrics.Scale(this, 18), Width, Metrics.Scale(this, 20)), Palette.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            return;
        }

        graphics.SetClip(new Rectangle(0, 0, ContentWidth, Height));
        for (int i = 0; i < _items.Count; i++)
        {
            int top = RowTop(i);
            if (top + RowHeight < 0 || top > Height) continue;
            DrawRow(graphics, _items[i], i, top);
        }
        graphics.ResetClip();

        DrawScrollBar(graphics);
    }

    private void DrawRow(Graphics graphics, CriterionDefinition item, int index, int top)
    {
        bool selected = index == _selectedIndex;
        bool hovered = index == _hoverIndex;

        RectangleF bounds = new(1, top + Metrics.ScaleF(this, 2), ContentWidth - Metrics.ScaleF(this, 2), RowHeight - Metrics.ScaleF(this, 5));
        float radius = Metrics.ScaleF(this, Metrics.ControlRadius);

        Color fill = selected ? Palette.Selection : hovered ? Palette.SurfaceHover : Palette.SurfaceRaised;
        Color border = selected ? Palette.WithAlpha(Palette.Accent, 150) : hovered ? Palette.BorderStrong : Palette.Border;

        using (GraphicsPath path = Metrics.RoundedRect(bounds, radius))
        {
            using SolidBrush brush = new(fill);
            graphics.FillPath(brush, path);
            using Pen pen = new(border, 1f);
            graphics.DrawPath(pen, path);
        }

        if (!item.Enabled)
        {
            using SolidBrush veil = new(Palette.WithAlpha(Palette.Canvas, 120));
            using GraphicsPath path = Metrics.RoundedRect(bounds, radius);
            graphics.FillPath(veil, path);
        }

        // Enable toggle
        Rectangle toggle = ToggleBounds(index);
        RectangleF toggleRect = new(toggle.X + 0.5f, toggle.Y + 0.5f, toggle.Width - 1f, toggle.Height - 1f);
        Color toggleFill = item.Enabled ? Palette.Accent : Palette.SurfaceSunken;
        if (item.Enabled && _hoverToggle && hovered) toggleFill = Palette.AccentHover;
        using (GraphicsPath path = Metrics.RoundedRect(toggleRect, Metrics.ScaleF(this, 4)))
        {
            using SolidBrush brush = new(toggleFill);
            graphics.FillPath(brush, path);
            using Pen pen = new(item.Enabled ? toggleFill : Palette.BorderStrong, 1f);
            graphics.DrawPath(pen, path);
        }
        if (item.Enabled)
        {
            int glyph = (int)(toggle.Width * 0.74f);
            Icons.Draw(graphics, AppIcon.Check,
                new Rectangle(toggle.X + (toggle.Width - glyph) / 2, toggle.Y + (toggle.Height - glyph) / 2, glyph, glyph),
                Palette.TextOnAccent, 3.2f);
        }

        int textLeft = toggle.Right + Metrics.Scale(this, 12);
        int pad = Metrics.Scale(this, 8);
        int rightReserve = Metrics.Scale(this, 150);

        string label = string.IsNullOrWhiteSpace(item.Label)
            ? MetricCatalog.ByKey.TryGetValue(item.MetricKey, out MetricDefinition? definition) ? definition.ChineseName : item.MetricKey
            : item.Label!;

        Color primaryInk = item.Enabled ? Palette.TextPrimary : Palette.TextMuted;
        Size labelSize = TextRenderer.MeasureText(label, Typography.BodyStrong, new Size(int.MaxValue, RowHeight), TextFormatFlags.NoPadding);
        int labelWidth = Math.Min(labelSize.Width, Math.Max(Metrics.Scale(this, 40), ContentWidth - textLeft - rightReserve));
        TextRenderer.DrawText(graphics, label, Typography.BodyStrong,
            new Rectangle(textLeft, top + Metrics.Scale(this, 7), labelWidth + Metrics.Scale(this, 4), Metrics.Scale(this, 22)), primaryInk,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        // Kind badge
        int badgeLeft = textLeft + labelWidth + Metrics.Scale(this, 8);
        string badgeText = item.Kind == CriterionKind.Hard ? "硬条件" : "加权";
        Color badgeColor = item.Kind == CriterionKind.Hard ? Palette.Danger : Palette.Accent;
        Size badgeSize = TextRenderer.MeasureText(badgeText, Typography.Small, new Size(int.MaxValue, RowHeight), TextFormatFlags.NoPadding);
        int badgeWidth = badgeSize.Width + Metrics.Scale(this, 14);
        Rectangle badge = new(badgeLeft, top + Metrics.Scale(this, 12), badgeWidth, Metrics.Scale(this, 19));
        if (badge.Right < ContentWidth - rightReserve + Metrics.Scale(this, 20))
        {
            // The badge keeps its colour coding through the tint and the border;
            // the caption itself uses the high-contrast primary ink so it stays
            // readable on both the normal and the selected row.
            using GraphicsPath path = Metrics.RoundedRect(new RectangleF(badge.X, badge.Y, badge.Width, badge.Height), badge.Height / 2f);
            using SolidBrush brush = new(Palette.WithAlpha(badgeColor, 64));
            graphics.FillPath(brush, path);
            using Pen pen = new(Palette.WithAlpha(badgeColor, 170), 1f);
            graphics.DrawPath(pen, path);
            TextRenderer.DrawText(graphics, badgeText, Typography.Small, badge,
                Palette.TextPrimary, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        // Metric key and comparison summary
        string metricLine = item.MetricKey;
        if (MetricCatalog.ByKey.TryGetValue(item.MetricKey, out MetricDefinition? metric) && metric.Unit.Length > 0)
        {
            metricLine += "  ·  " + metric.Unit;
        }
        TextRenderer.DrawText(graphics, metricLine, Typography.MonoSmall,
            new Rectangle(textLeft, top + Metrics.Scale(this, 30), Math.Max(0, ContentWidth - textLeft - rightReserve), Metrics.Scale(this, 20)),
            Palette.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        // Threshold summary, right aligned
        string summary = Describe(item);
        int summaryRight = ContentWidth - Metrics.Scale(this, 40);
        int summaryWidth = Metrics.Scale(this, 130);
        TextRenderer.DrawText(graphics, summary, Typography.BodyStrong,
            new Rectangle(summaryRight - summaryWidth, top, summaryWidth, RowHeight),
            item.Enabled ? Palette.Accent : Palette.TextDisabled,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        if (item.Kind == CriterionKind.Weighted && item.Weight != 1)
        {
            TextRenderer.DrawText(graphics, "权重 " + item.Weight.ToString("0.##", CultureInfo.CurrentCulture), Typography.Small,
                new Rectangle(summaryRight - summaryWidth, top + Metrics.Scale(this, 30), summaryWidth, Metrics.Scale(this, 20)),
                Palette.TextMuted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        if (hovered)
        {
            Rectangle delete = DeleteBounds(index);
            if (_hoverDelete)
            {
                using SolidBrush brush = new(Palette.WithAlpha(Palette.Danger, 40));
                graphics.FillEllipse(brush, delete);
            }
            int glyph = Metrics.Scale(this, 14);
            Icons.Draw(graphics, AppIcon.Trash,
                new Rectangle(delete.X + (delete.Width - glyph) / 2, delete.Y + (delete.Height - glyph) / 2, glyph, glyph),
                _hoverDelete ? Palette.DangerHover : Palette.TextMuted, 2.2f);
        }
    }

    private void DrawScrollBar(Graphics graphics)
    {
        if (!NeedsBar) return;
        int thumbHeight = ThumbHeight();
        int travel = Math.Max(1, ClientSize.Height - thumbHeight);
        int position = MaxOffset == 0 ? 0 : travel * _scrollOffset / MaxOffset;
        RectangleF thumb = new(
            Width - BarWidth + Metrics.ScaleF(this, 3),
            position + Metrics.ScaleF(this, 2),
            BarWidth - Metrics.ScaleF(this, 6),
            thumbHeight - Metrics.ScaleF(this, 4));
        using GraphicsPath path = Metrics.RoundedRect(thumb, thumb.Width / 2f);
        using SolidBrush brush = new(_dragging ? Palette.BorderStrong : Palette.Border);
        graphics.FillPath(brush, path);
    }

    public static string Describe(CriterionDefinition item)
    {
        string value = item.Value.ToString("0.###", CultureInfo.CurrentCulture);
        string second = item.SecondValue.ToString("0.###", CultureInfo.CurrentCulture);
        return item.Comparison switch
        {
            MetricComparison.AtLeast => "≥ " + value,
            MetricComparison.AtMost => "≤ " + value,
            MetricComparison.Equal => "= " + value,
            MetricComparison.NotEqual => "≠ " + value,
            MetricComparison.Between => value + " ~ " + second,
            _ => value
        };
    }
}
