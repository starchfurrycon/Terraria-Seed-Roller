using System.Drawing.Drawing2D;
using TerrariaSeedRoller.App.Design;

namespace TerrariaSeedRoller.App.Controls;

/// <summary>
/// A workspace switcher drawn as underline tabs. The system TabControl is
/// avoided because its page area is painted by the OS theme and cannot follow
/// the application palette.
/// </summary>
internal sealed class SegmentedTabs : ThemedControl
{
    public sealed record TabPage(string Title, AppIcon Icon, Control Content);

    private readonly List<TabPage> _pages = [];
    private int _selectedIndex = -1;
    private int _hoverIndex = -1;

    public SegmentedTabs()
    {
        BackColor = Palette.Canvas;
    }

    public event EventHandler? SelectedIndexChanged;

    public IReadOnlyList<TabPage> Pages => _pages;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_pages.Count == 0) return;
            int clamped = Math.Clamp(value, 0, _pages.Count - 1);
            if (clamped == _selectedIndex) return;
            _selectedIndex = clamped;
            ApplyVisibility();
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public TabPage? SelectedPage => _selectedIndex >= 0 && _selectedIndex < _pages.Count ? _pages[_selectedIndex] : null;

    public int StripHeight => Metrics.Scale(this, 44);

    public void AddPage(TabPage page)
    {
        _pages.Add(page);
        Controls.Add(page.Content);
        if (_selectedIndex < 0) _selectedIndex = 0;
        ApplyVisibility();
        PerformLayout();
        Invalidate();
    }

    private void ApplyVisibility()
    {
        for (int i = 0; i < _pages.Count; i++) _pages[i].Content.Visible = i == _selectedIndex;
    }

    public override Rectangle DisplayRectangle
    {
        get
        {
            Rectangle area = ClientRectangle;
            int strip = StripHeight;
            return new Rectangle(area.X, area.Y + strip, area.Width, Math.Max(0, area.Height - strip));
        }
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        Rectangle content = DisplayRectangle;
        foreach (TabPage page in _pages)
        {
            if (!page.Content.Visible) continue;
            page.Content.Bounds = content;
        }
    }

    private List<(Rectangle Bounds, TabPage Page)> TabBounds()
    {
        List<(Rectangle, TabPage)> result = [];
        int x = 0;
        int height = StripHeight;
        foreach (TabPage page in _pages)
        {
            Size text = TextRenderer.MeasureText(page.Title, Typography.BodyStrong, new Size(int.MaxValue, height), TextFormatFlags.NoPadding);
            int icon = Metrics.Scale(this, 16);
            int width = Metrics.Scale(this, 14) + icon + Metrics.Scale(this, 7) + text.Width + Metrics.Scale(this, 14);
            result.Add((new Rectangle(x, 0, width, height), page));
            x += width + Metrics.Scale(this, 4);
        }
        return result;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = HitTest(e.Location);
        if (index != _hoverIndex)
        {
            _hoverIndex = index;
            Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverIndex = -1;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        int index = HitTest(e.Location);
        if (index >= 0) SelectedIndex = index;
    }

    private int HitTest(Point location)
    {
        List<(Rectangle Bounds, TabPage Page)> bounds = TabBounds();
        for (int i = 0; i < bounds.Count; i++)
        {
            if (bounds[i].Bounds.Contains(location)) return i;
        }
        return -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(SurfaceColor);

        List<(Rectangle Bounds, TabPage Page)> tabs = TabBounds();
        for (int i = 0; i < tabs.Count; i++)
        {
            (Rectangle bounds, TabPage page) = tabs[i];
            bool selected = i == _selectedIndex;
            bool hovered = i == _hoverIndex;

            if (selected)
            {
                using GraphicsPath path = Metrics.RoundedRect(
                    new RectangleF(bounds.X, bounds.Y + Metrics.Scale(this, 4), bounds.Width, bounds.Height - Metrics.Scale(this, 8)),
                    Metrics.ScaleF(this, 7));
                using SolidBrush brush = new(Palette.SurfaceRaised);
                graphics.FillPath(brush, path);
            }
            else if (hovered)
            {
                using GraphicsPath path = Metrics.RoundedRect(
                    new RectangleF(bounds.X, bounds.Y + Metrics.Scale(this, 4), bounds.Width, bounds.Height - Metrics.Scale(this, 8)),
                    Metrics.ScaleF(this, 7));
                using SolidBrush brush = new(Palette.WithAlpha(Palette.SurfaceHover, 120));
                graphics.FillPath(brush, path);
            }

            Color ink = selected ? Palette.Accent : hovered ? Palette.TextPrimary : Palette.TextMuted;
            int icon = Metrics.Scale(this, 16);
            int left = bounds.X + Metrics.Scale(this, 14);
            Icons.Draw(graphics, page.Icon, new Rectangle(left, (bounds.Height - icon) / 2, icon, icon), ink);
            left += icon + Metrics.Scale(this, 7);

            TextRenderer.DrawText(graphics, page.Title, selected ? Typography.BodyStrong : Typography.Body,
                new Rectangle(left, 0, bounds.Width - (left - bounds.X) - Metrics.Scale(this, 8), bounds.Height), ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

            if (selected)
            {
                int underlineWidth = Math.Max(Metrics.Scale(this, 24), bounds.Width - Metrics.Scale(this, 28));
                RectangleF underline = new(
                    bounds.X + (bounds.Width - underlineWidth) / 2f,
                    bounds.Bottom - Metrics.ScaleF(this, 3),
                    underlineWidth,
                    Metrics.ScaleF(this, 2.5f));
                using GraphicsPath path = Metrics.RoundedRect(underline, underline.Height / 2f);
                using SolidBrush brush = new(Palette.Accent);
                graphics.FillPath(brush, path);
            }
        }

        // Hairline under the whole strip so the content area reads as a separate surface.
        using Pen separator = new(Palette.Border, 1f);
        float y = StripHeight - 0.5f;
        graphics.DrawLine(separator, 0, y, Width, y);
    }
}
