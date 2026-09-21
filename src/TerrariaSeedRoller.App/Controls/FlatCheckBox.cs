using System.Drawing.Drawing2D;
using TerrariaSeedRoller.App.Design;

namespace TerrariaSeedRoller.App.Controls;

/// <summary>A themed checkbox with a rounded box and a vector tick.</summary>
internal sealed class FlatCheckBox : ThemedControl
{
    private bool _checked;
    private bool _hover;

    public FlatCheckBox()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        BackColor = Palette.Canvas;
        Height = Metrics.Scale(this, 24);
        Cursor = Cursors.Hand;
    }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            OnCheckedChanged(EventArgs.Empty);
            Invalidate();
        }
    }

    public event EventHandler? CheckedChanged;

    private void OnCheckedChanged(EventArgs e) => CheckedChanged?.Invoke(this, e);

    private int BoxSize => Metrics.Scale(this, 17);

    private Rectangle BoxBounds() => new(0, (Height - BoxSize) / 2, BoxSize, BoxSize);

    public int ContentWidth
    {
        get
        {
            Size text = TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, Height), TextFormatFlags.NoPadding);
            return BoxSize + (text.Width > 0 ? Metrics.Scale(this, 8) + text.Width : 0);
        }
    }

    public override Size GetPreferredSize(Size proposedSize) => new(ContentWidth, Metrics.Scale(this, 24));

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
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
        }
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode == Keys.Space)
        {
            Checked = !Checked;
            e.Handled = true;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(SurfaceColor);

        Rectangle box = BoxBounds();
        float radius = Metrics.ScaleF(this, 4);
        RectangleF boxRect = new(box.X + 0.5f, box.Y + 0.5f, box.Width - 1f, box.Height - 1f);

        Color fill;
        Color border;
        if (!Enabled)
        {
            fill = _checked ? Palette.Mix(Palette.SurfaceRaised, Palette.TextDisabled, 0.3) : Palette.SurfaceSunken;
            border = Palette.Border;
        }
        else if (_checked)
        {
            fill = _hover ? Palette.AccentHover : Palette.Accent;
            border = fill;
        }
        else
        {
            fill = Palette.SurfaceInput;
            border = _hover ? Palette.BorderStrong : Palette.Border;
        }

        using (GraphicsPath path = Metrics.RoundedRect(boxRect, radius))
        {
            using SolidBrush brush = new(fill);
            graphics.FillPath(brush, path);
            using Pen pen = new(border, Focused ? 1.6f : 1f);
            graphics.DrawPath(pen, path);
        }

        if (_checked)
        {
            int glyph = (int)(box.Width * 0.78f);
            Icons.Draw(graphics, AppIcon.Check,
                new Rectangle(box.X + (box.Width - glyph) / 2, box.Y + (box.Height - glyph) / 2, glyph, glyph),
                Enabled ? Palette.TextOnAccent : Palette.TextMuted, 3.2f);
        }

        int textLeft = box.Right + Metrics.Scale(this, 8);
        TextRenderer.DrawText(graphics, Text, Font,
            new Rectangle(textLeft, 0, Math.Max(0, Width - textLeft), Height),
            Enabled ? Palette.TextPrimary : Palette.TextDisabled,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        if (Focused && Enabled)
        {
            using Pen pen = new(Palette.WithAlpha(Palette.FocusRing, 150), 1.3f);
            using GraphicsPath path = Metrics.RoundedRect(
                new RectangleF(boxRect.X - 2f, boxRect.Y - 2f, boxRect.Width + 4f, boxRect.Height + 4f), radius + 2f);
            graphics.DrawPath(pen, path);
        }
    }
}
