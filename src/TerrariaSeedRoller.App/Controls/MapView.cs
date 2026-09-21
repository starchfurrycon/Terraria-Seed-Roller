using System.Drawing.Drawing2D;
using TerrariaSeedRoller.App.Design;

namespace TerrariaSeedRoller.App.Controls;

/// <summary>
/// Displays a world overview with zoom-to-fit, wheel zoom and drag panning,
/// so large worlds can actually be inspected instead of being squeezed into
/// a fixed picture box.
/// </summary>
internal sealed class MapView : ThemedControl
{
    private Image? _image;
    private float _zoom = 1f;
    private bool _fitted = true;
    private PointF _pan;
    private bool _dragging;
    private Point _dragOrigin;
    private PointF _dragStartPan;

    public MapView()
    {
        BackColor = Palette.SurfaceSunken;
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
    }

    public Image? Image
    {
        get => _image;
        set
        {
            if (ReferenceEquals(_image, value)) return;
            _image?.Dispose();
            _image = value;
            _fitted = true;
            _pan = PointF.Empty;
            Invalidate();
        }
    }

    public float Zoom => _zoom;

    private float FitScale
    {
        get
        {
            if (_image is null || _image.Width <= 0 || _image.Height <= 0) return 1f;
            int pad = Metrics.Scale(this, 16);
            float available = Math.Max(1, Width - pad * 2);
            float availableHeight = Math.Max(1, Height - pad * 2);
            return Math.Min(available / _image.Width, availableHeight / _image.Height);
        }
    }

    public void FitToWindow()
    {
        _fitted = true;
        _pan = PointF.Empty;
        Invalidate();
    }

    public void ZoomBy(float factor)
    {
        if (_image is null) return;
        float baseline = _fitted ? FitScale : _zoom;
        _zoom = Math.Clamp(baseline * factor, FitScale * 0.5f, 12f);
        _fitted = false;
        ClampPan();
        Invalidate();
    }

    private float EffectiveScale => _image is null ? 1f : _fitted ? FitScale : _zoom;

    private void ClampPan()
    {
        if (_image is null) return;
        float scale = EffectiveScale;
        float contentWidth = _image.Width * scale;
        float contentHeight = _image.Height * scale;
        float maxX = Math.Max(0, (contentWidth - Width) / 2f);
        float maxY = Math.Max(0, (contentHeight - Height) / 2f);
        _pan = new PointF(Math.Clamp(_pan.X, -maxX, maxX), Math.Clamp(_pan.Y, -maxY, maxY));
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_fitted) Invalidate(); else { ClampPan(); Invalidate(); }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ZoomBy(e.Delta > 0 ? 1.15f : 1f / 1.15f);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || _image is null) return;
        _dragging = true;
        _dragOrigin = e.Location;
        _dragStartPan = _pan;
        Cursor = Cursors.SizeAll;
        Focus();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        _pan = new PointF(_dragStartPan.X + (e.X - _dragOrigin.X), _dragStartPan.Y + (e.Y - _dragOrigin.Y));
        ClampPan();
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
        Cursor = Cursors.Default;
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        FitToWindow();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(SurfaceColor);

        RectangleF frame = Metrics.Hairline(0, 0, Width, Height);
        using (GraphicsPath path = Metrics.RoundedRect(frame, Metrics.ScaleF(this, Metrics.ControlRadius)))
        {
            using SolidBrush brush = new(Palette.SurfaceSunken);
            graphics.FillPath(brush, path);
            using Pen pen = new(Palette.Border, 1f);
            graphics.DrawPath(pen, path);
        }

        if (_image is null) return;

        float scale = EffectiveScale;
        float drawWidth = _image.Width * scale;
        float drawHeight = _image.Height * scale;
        float x = (Width - drawWidth) / 2f + _pan.X;
        float y = (Height - drawHeight) / 2f + _pan.Y;

        graphics.SetClip(Metrics.RoundedRect(new RectangleF(1, 1, Math.Max(0, Width - 2), Math.Max(0, Height - 2)), Metrics.ScaleF(this, Metrics.ControlRadius - 1)));
        graphics.InterpolationMode = scale >= 1f ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.DrawImage(_image, x, y, drawWidth, drawHeight);
        graphics.ResetClip();

        // A subtle crosshair-free border around the world keeps the map readable on the dark frame.
        using Pen edge = new(Palette.WithAlpha(Palette.BorderStrong, 160), 1f);
        graphics.DrawRectangle(edge, x, y, drawWidth, drawHeight);
    }
}
