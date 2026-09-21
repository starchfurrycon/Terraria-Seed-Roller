using System.Drawing.Drawing2D;
using TerrariaSeedRoller.App.Design;

namespace TerrariaSeedRoller.App.Controls;

/// <summary>
/// A read-only, fully owner-drawn table with a themed scrollbar. Using this
/// instead of a system grid guarantees that no cell can ever fall back to the
/// OS light theme and hide its own text.
/// </summary>
internal sealed class TableView : ThemedControl
{
    internal sealed class Column
    {
        public required string Header { get; init; }
        public float Weight { get; init; } = 1f;
        public int MinWidth { get; init; } = 56;
        public ContentAlignment Alignment { get; init; } = ContentAlignment.MiddleLeft;
        public string? Suffix { get; init; }
        public Func<object?, string>? Format { get; init; }
        public bool Emphasised { get; init; }
    }

    internal sealed class Row
    {
        public required object?[] Cells { get; init; }
        public object? Tag { get; init; }
        public Color? Accent { get; init; }
    }

    private readonly List<Column> _columns = [];
    private readonly List<Row> _rows = [];
    private int _selectedIndex = -1;
    private int _hoverIndex = -1;
    private int _scrollOffset;
    private bool _thumbHover;
    private bool _dragging;
    private int _dragStartY;
    private int _dragStartOffset;

    public TableView()
    {
        BackColor = Palette.Surface;
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
    }

    public event EventHandler? SelectionChanged;

    public event EventHandler? RowActivated;

    public IReadOnlyList<Column> Columns => _columns;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_rows.Count == 0) { _selectedIndex = -1; return; }
            int clamped = Math.Clamp(value, -1, _rows.Count - 1);
            if (clamped == _selectedIndex) return;
            _selectedIndex = clamped;
            EnsureVisible(_selectedIndex);
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Row? SelectedRow => _selectedIndex >= 0 && _selectedIndex < _rows.Count ? _rows[_selectedIndex] : null;

    public void AddColumn(Column column) => _columns.Add(column);

    public void SetRows(IEnumerable<Row> rows, int selectIndex = 0)
    {
        _rows.Clear();
        _rows.AddRange(rows);
        _scrollOffset = 0;
        _selectedIndex = _rows.Count == 0 ? -1 : Math.Clamp(selectIndex, -1, _rows.Count - 1);
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear() => SetRows([], -1);

    public int HeaderHeight => Metrics.Scale(this, 36);

    public int RowHeight => Metrics.Scale(this, 32);

    private int BarWidth => Metrics.Scale(this, Metrics.ScrollBarWidth);

    private int ViewportHeight => Math.Max(0, ClientSize.Height - HeaderHeight);

    private bool NeedsBar => _rows.Count * RowHeight > ViewportHeight;

    private int MaxOffset => Math.Max(0, _rows.Count * RowHeight - ViewportHeight);

    private int ContentWidth => Math.Max(0, ClientSize.Width - (NeedsBar ? BarWidth : 0));

    private int[] ColumnWidths()
    {
        int available = ContentWidth;
        float totalWeight = _columns.Sum(c => c.Weight);
        int[] widths = new int[_columns.Count];
        int used = 0;
        for (int i = 0; i < _columns.Count; i++)
        {
            int width = (int)Math.Round(available * (_columns[i].Weight / totalWeight));
            width = Math.Max(width, Metrics.Scale(this, _columns[i].MinWidth));
            widths[i] = width;
            used += width;
        }

        // Shrink the widest column if minimum widths overflowed the viewport.
        int overflow = used - available;
        if (overflow > 0)
        {
            int widest = Array.IndexOf(widths, widths.Max());
            widths[widest] = Math.Max(Metrics.Scale(this, 40), widths[widest] - overflow);
        }
        return widths;
    }

    private void EnsureVisible(int index)
    {
        if (index < 0) return;
        int top = index * RowHeight;
        if (top < _scrollOffset) _scrollOffset = top;
        else if (top + RowHeight > _scrollOffset + ViewportHeight) _scrollOffset = top + RowHeight - ViewportHeight;
        _scrollOffset = Math.Clamp(_scrollOffset, 0, MaxOffset);
    }

    private void ScrollBy(int delta)
    {
        int next = Math.Clamp(_scrollOffset + delta, 0, MaxOffset);
        if (next == _scrollOffset) return;
        _scrollOffset = next;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ScrollBy(-e.Delta / 120 * RowHeight * 3);
    }

    private int IndexAt(Point location)
    {
        if (location.Y < HeaderHeight) return -1;
        int index = (location.Y - HeaderHeight + _scrollOffset) / RowHeight;
        return index >= 0 && index < _rows.Count ? index : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            int travel = Math.Max(1, ViewportHeight - ThumbHeight());
            int delta = e.Y - _dragStartY;
            int next = Math.Clamp(_dragStartOffset + (int)((long)delta * MaxOffset / travel), 0, MaxOffset);
            if (next != _scrollOffset) { _scrollOffset = next; Invalidate(); }
            return;
        }

        int index = IndexAt(e.Location);
        bool thumb = NeedsBar && e.X >= ClientSize.Width - BarWidth;
        if (index != _hoverIndex || thumb != _thumbHover)
        {
            _hoverIndex = index;
            _thumbHover = thumb;
            Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverIndex = -1;
        _thumbHover = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (NeedsBar && e.X >= ClientSize.Width - BarWidth)
        {
            int thumbHeight = ThumbHeight();
            int travel = Math.Max(1, ViewportHeight - thumbHeight);
            int position = MaxOffset == 0 ? 0 : travel * _scrollOffset / MaxOffset;
            if (e.Y >= position && e.Y <= position + thumbHeight)
            {
                _dragging = true;
                _dragStartY = e.Y;
                _dragStartOffset = _scrollOffset;
            }
            else
            {
                ScrollBy(e.Y < position ? -ViewportHeight : ViewportHeight);
            }
            return;
        }

        int index = IndexAt(e.Location);
        if (index >= 0) SelectedIndex = index;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (IndexAt(e.Location) >= 0) RowActivated?.Invoke(this, EventArgs.Empty);
    }

    protected override bool IsInputKey(Keys keyData)
        => keyData is Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.Enter || base.IsInputKey(keyData);

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
                SelectedIndex = Math.Min(_rows.Count - 1, _selectedIndex + 1);
                e.Handled = true;
                break;
            case Keys.Home:
                SelectedIndex = 0;
                e.Handled = true;
                break;
            case Keys.End:
                SelectedIndex = _rows.Count - 1;
                e.Handled = true;
                break;
            case Keys.Enter:
                if (_selectedIndex >= 0) RowActivated?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
        }
    }

    private int ThumbHeight()
        => Math.Max(Metrics.Scale(this, 32), (int)((long)ViewportHeight * ViewportHeight / Math.Max(1, _rows.Count * RowHeight)));

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(SurfaceColor);

        RectangleF frame = Metrics.Hairline(0, 0, Width, Height);
        using (GraphicsPath path = Metrics.RoundedRect(frame, Metrics.ScaleF(this, Metrics.ControlRadius)))
        {
            using SolidBrush brush = new(Palette.Surface);
            graphics.FillPath(brush, path);
            using Pen pen = new(Palette.Border, 1f);
            graphics.DrawPath(pen, path);
        }

        if (_columns.Count == 0) return;
        int[] widths = ColumnWidths();

        DrawHeader(graphics, widths);

        graphics.SetClip(new Rectangle(1, HeaderHeight, ContentWidth, ViewportHeight));
        for (int i = 0; i < _rows.Count; i++)
        {
            int y = HeaderHeight + i * RowHeight - _scrollOffset;
            if (y + RowHeight < HeaderHeight || y > Height) continue;
            DrawRow(graphics, _rows[i], i, widths, y);
        }
        graphics.ResetClip();

        DrawScrollBar(graphics);

        if (_rows.Count == 0)
        {
            TextRenderer.DrawText(graphics, "暂无数据", Typography.Body,
                new Rectangle(0, HeaderHeight, Width, Math.Max(0, ViewportHeight)), Palette.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    private void DrawHeader(Graphics graphics, int[] widths)
    {
        int height = HeaderHeight;
        using (SolidBrush brush = new(Palette.SurfaceRaised))
        {
            graphics.FillRectangle(brush, 1, 1, Math.Max(0, Width - 2), height - 1);
        }

        int x = 0;
        int pad = Metrics.Scale(this, 10);
        for (int i = 0; i < _columns.Count; i++)
        {
            Column column = _columns[i];
            Rectangle cell = new(x + pad, 0, Math.Max(0, widths[i] - pad * 2), height);
            TextRenderer.DrawText(graphics, column.Header, Typography.SmallStrong, cell, Palette.TextSecondary,
                Flags(column.Alignment) | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            x += widths[i];
        }

        using Pen pen = new(Palette.BorderStrong, 1f);
        graphics.DrawLine(pen, 0, height - 0.5f, ContentWidth, height - 0.5f);
    }

    private void DrawRow(Graphics graphics, Row row, int index, int[] widths, int y)
    {
        bool selected = index == _selectedIndex;
        bool hovered = index == _hoverIndex;

        Rectangle bounds = new(1, y, Math.Max(0, ContentWidth - 1), RowHeight);
        Color background = selected
            ? Palette.Selection
            : hovered ? Palette.SurfaceHover
            : index % 2 == 1 ? Palette.SurfaceRaised
            : Palette.Surface;

        using (SolidBrush brush = new(background))
        {
            graphics.FillRectangle(brush, bounds);
        }

        if (selected || row.Accent is not null)
        {
            using SolidBrush accent = new(row.Accent ?? Palette.Accent);
            graphics.FillRectangle(accent, 1, y, Metrics.Scale(this, 3), RowHeight);
        }

        int x = 0;
        int pad = Metrics.Scale(this, 10);
        for (int i = 0; i < _columns.Count && i < row.Cells.Length; i++)
        {
            Column column = _columns[i];
            string text = column.Format is not null ? column.Format(row.Cells[i]) : row.Cells[i]?.ToString() ?? string.Empty;
            if (column.Suffix is { Length: > 0 } && text.Length > 0) text += column.Suffix;

            Rectangle cell = new(x + pad, y, Math.Max(0, widths[i] - pad * 2), RowHeight);
            Color ink = selected ? Palette.SelectionText : column.Emphasised ? Palette.Accent : Palette.TextPrimary;
            Font font = column.Emphasised ? Typography.BodyStrong : Typography.Body;
            TextRenderer.DrawText(graphics, text, font, cell, ink,
                Flags(column.Alignment) | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            x += widths[i];
        }

        using Pen separator = new(Palette.GridLine, 1f);
        graphics.DrawLine(separator, 0, y + RowHeight - 0.5f, ContentWidth, y + RowHeight - 0.5f);
    }

    private void DrawScrollBar(Graphics graphics)
    {
        if (!NeedsBar) return;
        int thumbHeight = ThumbHeight();
        int travel = Math.Max(1, ViewportHeight - thumbHeight);
        int position = MaxOffset == 0 ? 0 : travel * _scrollOffset / MaxOffset;
        RectangleF thumb = new(
            Width - BarWidth + Metrics.ScaleF(this, 3),
            HeaderHeight + position + Metrics.ScaleF(this, 2),
            BarWidth - Metrics.ScaleF(this, 6),
            thumbHeight - Metrics.ScaleF(this, 4));

        Color color = _dragging ? Palette.BorderStrong : _thumbHover ? Palette.Mix(Palette.Border, Palette.BorderStrong, 0.6) : Palette.Border;
        using GraphicsPath path = Metrics.RoundedRect(thumb, thumb.Width / 2f);
        using SolidBrush brush = new(color);
        graphics.FillPath(brush, path);
    }

    private static TextFormatFlags Flags(ContentAlignment alignment) => alignment switch
    {
        ContentAlignment.MiddleRight or ContentAlignment.TopRight or ContentAlignment.BottomRight =>
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine,
        ContentAlignment.MiddleCenter or ContentAlignment.TopCenter or ContentAlignment.BottomCenter =>
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine,
        _ => TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
    };
}
