using System.Drawing.Drawing2D;
using TerrariaSeedRoller.App.Design;

namespace TerrariaSeedRoller.App.Controls;

/// <summary>
/// A vertical scroller with a thin, palette-matched scrollbar. The system
/// scrollbar is deliberately avoided because it cannot be themed consistently
/// with the rest of the window.
/// </summary>
internal sealed class ScrollHost : ThemedControl
{
    private readonly Control _content = null!;
    private int _offset;
    private bool _thumbHover;
    private bool _dragging;
    private int _dragStartY;
    private int _dragStartOffset;

    public ScrollHost(Control content)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        BackColor = Palette.Canvas;
        _content.Dock = DockStyle.None;
        _content.Left = 0;
        _content.Top = 0;
        Controls.Add(_content);
    }

    public Control Content => _content;

    public int BarWidth => Metrics.Scale(this, Metrics.ScrollBarWidth);

    private int ContentHeight => Math.Max(_content.Height, MeasuredContentHeight(ContentWidth));

    private int ViewportHeight => ClientSize.Height;

    private int ContentWidth => Math.Max(0, ClientSize.Width - (NeedsBar ? BarWidth : 0));

    // Decided from the full client width so it never re-enters ContentWidth.
    private bool NeedsBar => Math.Max(_content.Height, MeasuredContentHeight(ClientSize.Width)) > ViewportHeight;

    private int MaxOffset => Math.Max(0, ContentHeight - ViewportHeight);

    /// <summary>
    /// Asks the content for the height it needs at a known width. The cached
    /// <c>PreferredSize</c> property is never used here: it is computed once and
    /// can be captured while the content is still empty.
    /// </summary>
    private int MeasuredContentHeight(int width)
        => width <= 0 ? _content.Height : _content.GetPreferredSize(new Size(width, 0)).Height;

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        ApplyLayout();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ApplyLayout();
    }

    private void ApplyLayout()
    {
        if (_content is null) return;

        int bar = NeedsBar ? BarWidth : 0;
        int width = Math.Max(0, ClientSize.Width - bar);
        int height = Math.Max(MeasuredContentHeight(width), ViewportHeight);
        _offset = Math.Clamp(_offset, 0, MaxOffset);
        _content.SetBounds(0, -_offset, width, height);
        Invalidate();
    }

    public void ScrollTo(int value)
    {
        int clamped = Math.Clamp(value, 0, MaxOffset);
        if (clamped == _offset) return;
        _offset = clamped;
        _content.Top = -_offset;
        Invalidate();
    }

    public void ScrollBy(int delta) => ScrollTo(_offset + delta);

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ScrollBy(-e.Delta / 120 * Metrics.Scale(this, 64));
    }

    private Rectangle ThumbBounds()
    {
        if (!NeedsBar) return Rectangle.Empty;
        int track = ViewportHeight;
        int thumbHeight = Math.Max(Metrics.Scale(this, 36), (int)((long)track * track / ContentHeight));
        int travel = track - thumbHeight;
        int position = MaxOffset == 0 ? 0 : (int)((long)travel * _offset / MaxOffset);
        return new Rectangle(ClientSize.Width - BarWidth, position, BarWidth, thumbHeight);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            Rectangle thumb = ThumbBounds();
            int travel = Math.Max(1, ViewportHeight - thumb.Height);
            int delta = e.Y - _dragStartY;
            ScrollTo(_dragStartOffset + (int)((long)delta * MaxOffset / travel));
            return;
        }

        bool hover = NeedsBar && e.X >= ClientSize.Width - BarWidth;
        if (hover != _thumbHover)
        {
            _thumbHover = hover;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _thumbHover = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Rectangle thumb = ThumbBounds();
        if (thumb.IsEmpty) return;

        if (thumb.Contains(e.X, e.Y))
        {
            _dragging = true;
            _dragStartY = e.Y;
            _dragStartOffset = _offset;
        }
        else if (e.X >= ClientSize.Width - BarWidth)
        {
            ScrollBy(e.Y < thumb.Y ? -ViewportHeight : ViewportHeight);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(SurfaceColor);

        Rectangle thumb = ThumbBounds();
        if (thumb.IsEmpty) return;

        RectangleF track = new(thumb.X + Metrics.ScaleF(this, 3), Metrics.ScaleF(this, 4),
            thumb.Width - Metrics.ScaleF(this, 6), ClientSize.Height - Metrics.ScaleF(this, 8));
        float radius = track.Width / 2f;

        Color color = _dragging ? Palette.BorderStrong : _thumbHover ? Palette.Mix(Palette.Border, Palette.BorderStrong, 0.6) : Palette.Border;
        using GraphicsPath path = Metrics.RoundedRect(new RectangleF(track.X, thumb.Y, track.Width, thumb.Height), radius);
        using SolidBrush brush = new(color);
        graphics.FillPath(brush, path);
    }
}
