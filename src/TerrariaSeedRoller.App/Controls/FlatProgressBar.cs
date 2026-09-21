using System.Drawing.Drawing2D;
using TerrariaSeedRoller.App.Design;

namespace TerrariaSeedRoller.App.Controls;

/// <summary>A slim rounded progress indicator with an optional moving bar.</summary>
internal sealed class FlatProgressBar : ThemedControl
{
    private readonly System.Windows.Forms.Timer _animation = new() { Interval = 24 };
    private double _minimum;
    private double _maximum = 100;
    private double _value;
    private double _phase;
    private bool _indeterminate;

    public FlatProgressBar()
    {
        Height = Metrics.Scale(this, 8);
        _animation.Tick += (_, _) =>
        {
            _phase += 0.018;
            if (_phase > 1) _phase -= 1;
            Invalidate();
        };
    }

    public double Minimum
    {
        get => _minimum;
        set { _minimum = value; Invalidate(); }
    }

    public double Maximum
    {
        get => _maximum;
        set { _maximum = Math.Max(value, _minimum + 1); Invalidate(); }
    }

    public double Value
    {
        get => _value;
        set { _value = Math.Clamp(value, _minimum, _maximum); Invalidate(); }
    }

    public bool Indeterminate
    {
        get => _indeterminate;
        set
        {
            if (_indeterminate == value) return;
            _indeterminate = value;
            if (value) _animation.Start(); else _animation.Stop();
            Invalidate();
        }
    }

    private double Fraction => _maximum <= _minimum ? 0 : (_value - _minimum) / (_maximum - _minimum);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animation.Stop();
            _animation.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(SurfaceColor);

        float radius = Height / 2f;
        RectangleF track = new(0, 0, Width, Height);
        using (GraphicsPath path = Metrics.RoundedRect(track, radius))
        {
            using SolidBrush brush = new(Palette.SurfaceSunken);
            graphics.FillPath(brush, path);
            using Pen pen = new(Palette.Border, 1f);
            graphics.DrawPath(pen, path);
        }

        graphics.SetClip(Metrics.RoundedRect(new RectangleF(1, 1, Math.Max(0, Width - 2), Math.Max(0, Height - 2)), radius - 1));

        if (_indeterminate)
        {
            float segment = Math.Max(Width * 0.28f, 40f);
            float travel = Width + segment;
            float x = (float)(_phase * travel) - segment;
            using LinearGradientBrush brush = new(
                new RectangleF(x, 0, segment, Height),
                Palette.WithAlpha(Palette.Accent, 40),
                Palette.WithAlpha(Palette.Accent, 40),
                LinearGradientMode.Horizontal);
            ColorBlend blend = new()
            {
                Colors = [Palette.WithAlpha(Palette.Accent, 0), Palette.Accent, Palette.WithAlpha(Palette.Accent, 0)],
                Positions = [0f, 0.5f, 1f]
            };
            brush.InterpolationColors = blend;
            graphics.FillRectangle(brush, x, 0, segment, Height);
        }
        else
        {
            float filled = (float)(Width * Fraction);
            if (filled > 1)
            {
                using LinearGradientBrush brush = new(
                    new RectangleF(0, 0, Math.Max(filled, 1), Height),
                    Palette.AccentPressed,
                    Palette.AccentHover,
                    LinearGradientMode.Horizontal);
                graphics.FillRectangle(brush, 0, 0, filled, Height);
            }
        }

        graphics.ResetClip();
    }
}
