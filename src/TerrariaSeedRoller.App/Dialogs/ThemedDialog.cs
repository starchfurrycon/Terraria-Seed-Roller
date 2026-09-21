using TerrariaSeedRoller.App.Design;

namespace TerrariaSeedRoller.App.Dialogs;

/// <summary>
/// Base class for modal dialogs. Sizing is explicit rather than autoscaled so a
/// high-DPI display cannot shrink or stretch the window unexpectedly.
/// </summary>
internal abstract class ThemedDialog : Form
{
    protected ThemedDialog()
    {
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Palette.Canvas;
        ForeColor = Palette.TextPrimary;
        Font = Typography.Body;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        DarkMode.ApplyTitleBar(this);
    }

    /// <summary>Sets the client area using 96-DPI logical units.</summary>
    protected void SetLogicalClientSize(int width, int height)
    {
        ClientSize = new Size(Metrics.Scale(this, width), Metrics.Scale(this, height));
    }

    /// <summary>Builds a right-aligned dialog button bar.</summary>
    protected static FlowLayoutPanel CreateButtonBar(params Control[] buttons)
    {
        FlowLayoutPanel bar = new()
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Palette.Canvas,
            Padding = new Padding(0, 6, 0, 0)
        };
        foreach (Control button in buttons)
        {
            button.Margin = new Padding(8, 0, 0, 0);
            bar.Controls.Add(button);
        }
        return bar;
    }
}
