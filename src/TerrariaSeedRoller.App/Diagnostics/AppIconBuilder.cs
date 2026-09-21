using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using TerrariaSeedRoller.App.Design;

namespace TerrariaSeedRoller.App.Diagnostics;

/// <summary>
/// Renders the application icon from the same vector artwork the UI uses, so the
/// executable, the installer and the in-app header can never drift apart. The
/// result is a PNG-compressed multi-resolution .ico.
/// </summary>
internal static class AppIconBuilder
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];

    public static int Run(string outputPath)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        List<(int Size, byte[] Png)> frames = [];
        foreach (int size in Sizes)
        {
            using Bitmap bitmap = Render(size);
            using MemoryStream stream = new();
            bitmap.Save(stream, ImageFormat.Png);
            frames.Add((size, stream.ToArray()));
        }

        using FileStream file = File.Create(outputPath);
        using BinaryWriter writer = new(file);

        writer.Write((ushort)0);                 // reserved
        writer.Write((ushort)1);                 // type: icon
        writer.Write((ushort)frames.Count);

        int offset = 6 + frames.Count * 16;
        foreach ((int size, byte[] png) in frames)
        {
            writer.Write((byte)(size >= 256 ? 0 : size));   // width, 0 means 256
            writer.Write((byte)(size >= 256 ? 0 : size));   // height
            writer.Write((byte)0);                          // palette count
            writer.Write((byte)0);                          // reserved
            writer.Write((ushort)1);                        // colour planes
            writer.Write((ushort)32);                       // bits per pixel
            writer.Write(png.Length);
            writer.Write(offset);
            offset += png.Length;
        }

        foreach ((int _, byte[] png) in frames) writer.Write(png);

        Console.WriteLine($"wrote {outputPath} with {frames.Count} frames ({new FileInfo(outputPath).Length} bytes)");
        return 0;
    }

    private static Bitmap Render(int size)
    {
        Bitmap bitmap = new(size, size, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.Clear(Color.Transparent);

        float inset = Math.Max(0.5f, size * 0.035f);
        RectangleF plate = new(inset, inset, size - inset * 2f, size - inset * 2f);
        float radius = size * 0.22f;

        using (GraphicsPath path = RoundedRect(plate, radius))
        {
            using (LinearGradientBrush brush = new(plate, Palette.SurfaceRaised, Palette.CanvasDeep, LinearGradientMode.Vertical))
            {
                graphics.FillPath(brush, path);
            }
            if (size >= 32)
            {
                using Pen pen = new(Palette.BorderStrong, Math.Max(1f, size / 48f));
                graphics.DrawPath(pen, path);
            }
        }

        float glyphInset = size * 0.21f;
        Rectangle glyph = new(
            (int)Math.Round(glyphInset),
            (int)Math.Round(glyphInset),
            Math.Max(1, (int)Math.Round(size - glyphInset * 2f)),
            Math.Max(1, (int)Math.Round(size - glyphInset * 2f)));
        Icons.Draw(graphics, AppIcon.Dice, glyph, Palette.Accent, size <= 24 ? 2.9f : 2.4f);

        return bitmap;
    }

    private static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        GraphicsPath path = new();
        float diameter = Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height));
        if (diameter <= 0.5f)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180f, 90f);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270f, 90f);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0f, 90f);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90f, 90f);
        path.CloseFigure();
        return path;
    }
}
