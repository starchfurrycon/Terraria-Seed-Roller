using System.Drawing.Drawing2D;
using TerrariaSeedRoller.App.Design;

namespace TerrariaSeedRoller.App.Controls;

/// <summary>The colour key for the world overview map.</summary>
internal sealed class MapLegend : ThemedControl
{
    private const int SwatchSize = 10;
    private const int RowHeight = 20;
    private const int GapX = 14;
    private const int GapY = 2;

    public MapLegend()
    {
        BackColor = Palette.Surface;
    }

    private static IEnumerable<MapLayer> VisibleLayers => MapPalette.Layers;

    public int MeasureHeight(int width)
    {
        int columns = Math.Max(1, width / Math.Max(1, Metrics.Scale(this, 96)));
        int rows = (int)Math.Ceiling(MapPalette.Layers.Length / (double)columns);
        return rows * Metrics.Scale(this, RowHeight + GapY) + Metrics.Scale(this, 4);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(SurfaceColor);

        int columnWidth = Math.Max(Metrics.Scale(this, 96), Width / Math.Max(1, Width / Math.Max(1, Metrics.Scale(this, 96))));
        int columns = Math.Max(1, Width / columnWidth);
        int rowHeight = Metrics.Scale(this, RowHeight);
        int rowPitch = rowHeight + Metrics.Scale(this, GapY);
        int swatch = Metrics.Scale(this, SwatchSize);
        int pad = Metrics.Scale(this, 3);

        int index = 0;
        foreach (MapLayer layer in VisibleLayers)
        {
            int column = index % columns;
            int row = index / columns;
            index++;

            int x = pad + column * columnWidth;
            int y = pad + row * rowPitch + (rowHeight - swatch) / 2;

            RectangleF box = new(x, y, swatch, swatch);
            using (GraphicsPath path = Metrics.RoundedRect(box, Metrics.ScaleF(this, 2.5f)))
            {
                using SolidBrush brush = new(layer.Color);
                graphics.FillPath(brush, path);
            }

            int textLeft = x + swatch + Metrics.Scale(this, 6);
            TextRenderer.DrawText(graphics, layer.Name, Typography.Small,
                new Rectangle(textLeft, pad + row * rowPitch, Math.Max(0, columnWidth - (textLeft - x) - Metrics.Scale(this, 6)), rowHeight),
                Palette.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }
}
