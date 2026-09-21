using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TerrariaSeedRoller.App.Diagnostics;

/// <summary>
/// Renders the live window to PNG files, one per workspace tab. It uses
/// <c>PrintWindow</c> so the capture is independent of window z-order, which
/// screen-grabbing is not.
/// </summary>
internal static class UiShot
{
    private const uint RenderFullContent = 2;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public static int Run(MainForm form, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.Manual;
        Rectangle work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        form.Location = new Point(
            Math.Max(work.Left, work.Right - form.Width - 8),
            Math.Max(work.Top, work.Bottom - form.Height - 8));
        form.Show();
        Pump(700);

        List<string> written = [];
        for (int index = 0; index < form.WorkspaceTabCount; index++)
        {
            form.SelectWorkspaceTab(index);
            Pump(320);
            string path = Path.Combine(outputDirectory, $"{index:00}-tab{index}.png");
            if (Capture(form, path)) written.Add(path);
        }

        form.Hide();
        Console.WriteLine($"UI shots written: {string.Join(", ", written)}");
        return written.Count == form.WorkspaceTabCount ? 0 : 3;
    }

    private static void Pump(int milliseconds)
    {
        Application.DoEvents();
        Thread.Sleep(milliseconds);
        Application.DoEvents();
    }

    private static bool Capture(Form form, string path)
    {
        if (form.Width <= 0 || form.Height <= 0) return false;
        using Bitmap bitmap = new(form.Width, form.Height);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            IntPtr hdc = graphics.GetHdc();
            try
            {
                if (!PrintWindow(form.Handle, hdc, RenderFullContent)) return false;
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
        }
        bitmap.Save(path, ImageFormat.Png);
        return true;
    }
}
