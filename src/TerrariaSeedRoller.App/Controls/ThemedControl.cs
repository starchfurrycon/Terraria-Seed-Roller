using TerrariaSeedRoller.App.Design;

namespace TerrariaSeedRoller.App.Controls;

/// <summary>
/// Base class for every hand-painted control. It guarantees double buffering,
/// a coherent default font and palette, and correct repainting on resize and
/// on a DPI change.
/// </summary>
internal abstract class ThemedControl : Control
{
    protected ThemedControl()
    {
        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw,
            true);
        DoubleBuffered = true;
        BackColor = Palette.Canvas;
        ForeColor = Palette.TextPrimary;
        Font = Typography.Body;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Never leave the back buffer untouched: an unpainted region renders as
        // opaque black, which showed up as stripes wherever a container had gaps
        // between its children.
        e.Graphics.Clear(SurfaceColor);
    }

    /// <summary>
    /// The colour of the surface this control is drawn on. Inside a
    /// <see cref="SurfaceCard"/> that is the card face; otherwise it is the
    /// parent's own background.
    /// </summary>
    protected Color SurfaceColor => ResolveSurface(this);

    internal static Color ResolveSurface(Control control)
    {
        for (Control? ancestor = control.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is SurfaceCard) return Palette.Surface;
        }
        return control.Parent?.BackColor ?? control.BackColor;
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Invalidate();
    }
}
