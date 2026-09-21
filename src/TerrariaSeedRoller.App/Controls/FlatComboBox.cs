using System.Drawing.Drawing2D;
using TerrariaSeedRoller.App.Design;

namespace TerrariaSeedRoller.App.Controls;

/// <summary>
/// A drop-down list that is painted entirely by the application. The system
/// combo box cannot be themed, which is exactly why the previous build rendered
/// pale grey text on a white field; this control removes that failure mode.
/// </summary>
internal sealed class FlatComboBox : ThemedControl
{
    private readonly List<object> _items = [];
    private int _selectedIndex = -1;
    private bool _hover;
    private bool _open;
    private ToolStripDropDown? _dropDown;

    public FlatComboBox()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        Height = Metrics.Scale(this, Metrics.ControlHeight);
        Cursor = Cursors.Hand;
    }

    public IReadOnlyList<object> Items => _items;

    /// <summary>Maps an item to the text that should be shown. Defaults to <c>ToString()</c>.</summary>
    public Func<object, string>? DisplaySelector { get; set; }

    public AppIcon? LeadingIcon { get; set; }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            int clamped = _items.Count == 0 ? -1 : Math.Clamp(value, -1, _items.Count - 1);
            if (clamped == _selectedIndex) return;
            _selectedIndex = clamped;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public object? SelectedItem => _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : null;

    public event EventHandler? SelectedIndexChanged;

    public void SetItems(IEnumerable<object> items, int selectedIndex = 0)
    {
        _items.Clear();
        _items.AddRange(items);
        _selectedIndex = _items.Count == 0 ? -1 : Math.Clamp(selectedIndex, 0, _items.Count - 1);
        Invalidate();
    }

    public string DisplayOf(object item) => DisplaySelector?.Invoke(item) ?? item.ToString() ?? string.Empty;

    private string SelectedText => SelectedItem is { } item ? DisplayOf(item) : string.Empty;

    private int ChevronSize => Metrics.Scale(this, 16);

    private int IconSize => Metrics.Scale(this, 16);

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();
        ToggleDropDown();
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override bool IsInputKey(Keys keyData)
        => keyData is Keys.Up or Keys.Down or Keys.Space or Keys.Enter || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Down when !_open:
                SelectedIndex++;
                e.Handled = true;
                break;
            case Keys.Up when !_open:
                SelectedIndex--;
                e.Handled = true;
                break;
            case Keys.Space or Keys.Enter:
                ToggleDropDown();
                e.Handled = true;
                break;
        }
    }

    private void ToggleDropDown()
    {
        if (_open)
        {
            CloseDropDown();
            return;
        }
        OpenDropDown();
    }

    private void OpenDropDown()
    {
        if (_items.Count == 0 || !Enabled) return;

        DropDownList list = new(this);
        ToolStripControlHost host = new(list)
        {
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            AutoSize = false,
            Size = list.Size
        };

        ToolStripDropDown dropDown = new()
        {
            AutoSize = false,
            AutoClose = true,
            DropShadowEnabled = true,
            Padding = Padding.Empty,
            Margin = Padding.Empty,
            BackColor = Palette.SurfaceRaised,
            Renderer = new PlainDropDownRenderer()
        };
        dropDown.Items.Add(host);
        dropDown.Size = list.Size;
        dropDown.Closed += (_, _) =>
        {
            _open = false;
            _dropDown = null;
            Invalidate();
        };

        _dropDown = dropDown;
        _open = true;
        dropDown.Show(this, new Point(0, Height + Metrics.Scale(this, 2)));
        list.Focus();
        Invalidate();
    }

    private void CloseDropDown()
    {
        _dropDown?.Close();
        _dropDown = null;
        _open = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(SurfaceColor);

        Color fill = Enabled ? Palette.SurfaceInput : Palette.SurfaceSunken;
        Color border = !Enabled ? Palette.Border
            : _open || Focused ? Palette.FocusRing
            : _hover ? Palette.BorderStrong
            : Palette.Border;

        RectangleF bounds = Metrics.Hairline(0, 0, Width, Height);
        float radius = Metrics.ScaleF(this, Metrics.ControlRadius);
        using (GraphicsPath path = Metrics.RoundedRect(bounds, radius))
        {
            using SolidBrush brush = new(fill);
            graphics.FillPath(brush, path);
            using Pen pen = new(border, _open || Focused ? 1.4f : 1f);
            graphics.DrawPath(pen, path);
        }

        int left = Metrics.Scale(this, 10);
        if (LeadingIcon is { } icon)
        {
            Icons.Draw(graphics, icon, new Rectangle(left, (Height - IconSize) / 2, IconSize, IconSize),
                Enabled ? Palette.TextMuted : Palette.TextDisabled);
            left += IconSize + Metrics.Scale(this, 8);
        }

        int right = ChevronSize + Metrics.Scale(this, 12);
        Rectangle textBounds = new(left, 0, Math.Max(0, Width - left - right), Height);
        TextRenderer.DrawText(graphics, SelectedText, Font, textBounds,
            Enabled ? Palette.TextPrimary : Palette.TextDisabled,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        Icons.Draw(graphics, AppIcon.ChevronDown,
            new Rectangle(Width - ChevronSize - Metrics.Scale(this, 10), (Height - ChevronSize) / 2, ChevronSize, ChevronSize),
            Enabled ? (_hover || _open ? Palette.Accent : Palette.TextSecondary) : Palette.TextDisabled);
    }

    private sealed class PlainDropDownRenderer : ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using SolidBrush brush = new(Palette.SurfaceRaised);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            // The hosted list paints its own border so the corners stay square with the shadow.
        }
    }

    private sealed class DropDownList : ThemedControl
    {
        private const int MaxVisible = 12;

        private readonly FlatComboBox _owner;
        private int _hoverIndex = -1;
        private int _firstVisible;

        public DropDownList(FlatComboBox owner)
        {
            _owner = owner;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            BackColor = Palette.SurfaceRaised;
            int visible = Math.Min(owner._items.Count, MaxVisible);
            Width = Math.Max(owner.Width, Metrics.Scale(this, 160));
            Height = visible * ItemHeight + Metrics.Scale(this, 8);
        }

        public int ItemHeight => Metrics.Scale(this, 28);

        private int VisibleCount => Math.Max(1, (Height - Metrics.Scale(this, 8)) / ItemHeight);

        private int MaxFirst => Math.Max(0, _owner._items.Count - VisibleCount);

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = IndexAt(e.Y);
            if (index != _hoverIndex)
            {
                _hoverIndex = index;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverIndex = -1;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            _firstVisible = Math.Clamp(_firstVisible - e.Delta / 120, 0, MaxFirst);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int index = IndexAt(e.Y);
            if (index >= 0)
            {
                _owner.SelectedIndex = index;
                _owner.CloseDropDown();
            }
        }

        private int IndexAt(int y)
        {
            int top = Metrics.Scale(this, 4);
            if (y < top) return -1;
            int index = _firstVisible + (y - top) / ItemHeight;
            return index >= 0 && index < _owner._items.Count ? index : -1;
        }

        protected override bool IsInputKey(Keys keyData)
            => keyData is Keys.Up or Keys.Down or Keys.Enter or Keys.Escape || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Down:
                    MoveSelection(1);
                    e.Handled = true;
                    break;
                case Keys.Up:
                    MoveSelection(-1);
                    e.Handled = true;
                    break;
                case Keys.Enter:
                    _owner.CloseDropDown();
                    e.Handled = true;
                    break;
                case Keys.Escape:
                    _owner.CloseDropDown();
                    e.Handled = true;
                    break;
            }
        }

        private void MoveSelection(int direction)
        {
            int next = Math.Clamp(_owner._selectedIndex + direction, 0, _owner._items.Count - 1);
            _owner.SelectedIndex = next;
            if (next < _firstVisible) _firstVisible = next;
            if (next >= _firstVisible + VisibleCount) _firstVisible = next - VisibleCount + 1;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Palette.SurfaceRaised);

            RectangleF bounds = Metrics.Hairline(0, 0, Width, Height);
            float radius = Metrics.ScaleF(this, Metrics.ControlRadius);
            using (GraphicsPath path = Metrics.RoundedRect(bounds, radius))
            {
                using SolidBrush brush = new(Palette.SurfaceRaised);
                graphics.FillPath(brush, path);
                using Pen pen = new(Palette.BorderStrong, 1f);
                graphics.DrawPath(pen, path);
            }

            int top = Metrics.Scale(this, 4);
            int pad = Metrics.Scale(this, 10);
            int checkSize = Metrics.Scale(this, 14);

            for (int slot = 0; slot < VisibleCount; slot++)
            {
                int index = _firstVisible + slot;
                if (index >= _owner._items.Count) break;

                Rectangle row = new(pad / 2, top + slot * ItemHeight, Width - pad, ItemHeight);
                bool selected = index == _owner._selectedIndex;
                bool hovered = index == _hoverIndex;

                if (hovered || selected)
                {
                    using GraphicsPath rowPath = Metrics.RoundedRect(
                        new RectangleF(row.X, row.Y + 1, row.Width, row.Height - 2), Metrics.ScaleF(this, 5));
                    using SolidBrush brush = new(hovered ? Palette.SurfaceHover : Palette.Selection);
                    graphics.FillPath(brush, rowPath);
                }

                string text = _owner.DisplayOf(_owner._items[index]);
                Rectangle textBounds = new(row.X + Metrics.Scale(this, 8), row.Y, Math.Max(0, row.Width - checkSize - Metrics.Scale(this, 18)), row.Height);
                TextRenderer.DrawText(graphics, text, Typography.Body, textBounds,
                    selected ? Palette.SelectionText : Palette.TextPrimary,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

                if (selected)
                {
                    Icons.Draw(graphics, AppIcon.Check,
                        new Rectangle(row.Right - checkSize - Metrics.Scale(this, 8), row.Y + (row.Height - checkSize) / 2, checkSize, checkSize),
                        Palette.Accent, 2.6f);
                }
            }

            if (_owner._items.Count > VisibleCount)
            {
                int trackTop = top;
                int trackHeight = Height - Metrics.Scale(this, 8);
                int thumbHeight = Math.Max(Metrics.Scale(this, 24), trackHeight * VisibleCount / _owner._items.Count);
                int travel = trackHeight - thumbHeight;
                int position = MaxFirst == 0 ? 0 : travel * _firstVisible / MaxFirst;
                RectangleF thumb = new(Width - Metrics.Scale(this, 7), trackTop + position, Metrics.Scale(this, 4), thumbHeight);
                using GraphicsPath thumbPath = Metrics.RoundedRect(thumb, thumb.Width / 2f);
                using SolidBrush brush = new(Palette.BorderStrong);
                graphics.FillPath(brush, thumbPath);
            }
        }
    }
}
