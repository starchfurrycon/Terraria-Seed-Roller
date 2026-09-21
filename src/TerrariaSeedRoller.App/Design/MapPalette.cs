using System.Drawing.Imaging;
using TerrariaSeedRoller.Core;

namespace TerrariaSeedRoller.App.Design;

/// <summary>One biome layer of the world overview map.</summary>
internal readonly record struct MapLayer(byte Index, string Name, Color Color);

/// <summary>
/// The single, deliberately harmonised colour scheme for the world overview.
/// Every layer has a distinct hue and lightness so no two layers can be confused,
/// which the previous ad-hoc colour array did not guarantee.
/// </summary>
internal static class MapPalette
{
    public static readonly MapLayer[] Layers =
    [
        new(1, "腐化", Palette.FromHex(0x9B7BD4)),
        new(2, "猩红", Palette.FromHex(0xE0525E)),
        new(3, "丛林", Palette.FromHex(0x43A860)),
        new(4, "雪原", Palette.FromHex(0xCBE9F7)),
        new(5, "沙漠", Palette.FromHex(0xE4C05C)),
        new(6, "地牢", Palette.FromHex(0x3F6FC4)),
        new(7, "神庙", Palette.FromHex(0xE08A3A)),
        new(8, "蜂巢", Palette.FromHex(0xC9A227)),
        new(9, "蜘蛛洞", Palette.FromHex(0x8C93A0)),
        new(10, "大理石", Palette.FromHex(0xE8EDF2)),
        new(11, "花岗岩", Palette.FromHex(0x6E6478)),
        new(12, "蘑菇地", Palette.FromHex(0x3FBFC9)),
        new(13, "浮空岛", Palette.FromHex(0x7EE0C0)),
        new(14, "生命树", Palette.FromHex(0x8B5E3C)),
        new(15, "微光", Palette.FromHex(0xE86FC0)),
        new(16, "岩浆", Palette.FromHex(0xF2622E)),
        new(17, "轨道", Palette.FromHex(0xB0A08C))
    ];

    public static Color ColorFor(byte index)
    {
        foreach (MapLayer layer in Layers)
        {
            if (layer.Index == index) return layer.Color;
        }
        return Palette.CanvasDeep;
    }

    /// <summary>
    /// Renders the analysis overview. Each pixel represents an 8x8 tile block;
    /// the spawn point is marked with a ring.
    /// </summary>
    public static Bitmap CreateOverviewBitmap(WorldAnalysis analysis)
    {
        Bitmap bitmap = new(Math.Max(1, analysis.OverviewWidth), Math.Max(1, analysis.OverviewHeight), PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Palette.CanvasDeep);

        int width = analysis.OverviewWidth;
        int height = analysis.OverviewHeight;
        for (int y = 0; y < height; y++)
        {
            int x = 0;
            while (x < width)
            {
                byte value = analysis.Overview[x * height + y];
                int start = x++;
                while (x < width && analysis.Overview[x * height + y] == value) x++;
                if (value == 0) continue;
                using SolidBrush brush = new(ColorFor(value));
                graphics.FillRectangle(brush, start, y, x - start, 1);
            }
        }

        float sx = analysis.Metadata.Spawn.X / 8f;
        float sy = analysis.Metadata.Spawn.Y / 8f;
        using SolidBrush halo = new(Color.FromArgb(70, 255, 255, 255));
        graphics.FillEllipse(halo, sx - 5, sy - 5, 11, 11);
        graphics.FillEllipse(Brushes.White, sx - 2.5f, sy - 2.5f, 6, 6);
        using Pen outline = new(Color.FromArgb(200, 10, 14, 20), 1.4f);
        graphics.DrawEllipse(outline, sx - 2.5f, sy - 2.5f, 6, 6);

        return bitmap;
    }
}
