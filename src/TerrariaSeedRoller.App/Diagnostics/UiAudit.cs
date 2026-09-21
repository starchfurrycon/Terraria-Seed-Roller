using System.Reflection;
using System.Text;
using TerrariaSeedRoller.App.Controls;
using TerrariaSeedRoller.App.Design;

namespace TerrariaSeedRoller.App.Diagnostics;

/// <summary>
/// Headless layout audit. It walks the live control tree and reports geometry
/// and colour problems that a screenshot cannot prove: clipped children,
/// overlapping siblings, zero-sized surfaces and unreadable colour pairs.
/// </summary>
internal static class UiAudit
{
    // STATE_VISIBLE: the control's own flag, independent of its ancestors, so
    // the audit works on a form that was never shown.
    private const int StateVisible = 0x02;

    private static readonly MethodInfo? GetStateMethod =
        typeof(Control).GetMethod("GetState", BindingFlags.Instance | BindingFlags.NonPublic);

    private static bool OwnVisible(Control control)
    {
        if (GetStateMethod is null) return control.Visible;
        try
        {
            return GetStateMethod.Invoke(control, [StateVisible]) is true;
        }
        catch (Exception)
        {
            return control.Visible;
        }
    }

    public static int Run(Form form, string outputPath, bool append = false)
    {
        form.CreateControl();
        RevealHiddenPages(form);
        form.PerformLayout();
        ApplyLayoutRecursive(form);

        StringBuilder report = new();
        List<string> issues = [];

        report.AppendLine($"UI audit for {form.GetType().Name}");
        report.AppendLine($"device dpi      : {form.DeviceDpi}");
        report.AppendLine($"form size       : {form.Width} x {form.Height}");        report.AppendLine($"client size     : {form.ClientSize.Width} x {form.ClientSize.Height}");
        report.AppendLine($"minimum size    : {form.MinimumSize.Width} x {form.MinimumSize.Height}");
        report.AppendLine($"auto scale mode : {form.AutoScaleMode}");
        report.AppendLine($"primary font    : {Typography.FamilyName}");
        report.AppendLine($"virtual screen  : {SystemInformation.VirtualScreen.Width} x {SystemInformation.VirtualScreen.Height}");
        foreach (Screen screen in Screen.AllScreens)
        {
            report.AppendLine($"screen          : {screen.DeviceName} bounds={screen.Bounds} working={screen.WorkingArea} primary={screen.Primary}");
        }
        report.AppendLine();
        report.AppendLine("contrast of the core palette:");
        foreach ((string name, Color ink, Color fill) in PalettePairs())
        {
            double ratio = Palette.Contrast(ink, fill);
            string verdict = ratio >= 4.5 ? "OK" : ratio >= 3.0 ? "LARGE-TEXT-ONLY" : "FAIL";
            report.AppendLine($"  {name,-34} {ratio,6:0.00}:1  {verdict}");
            if (ratio < 4.5) issues.Add($"palette: {name} contrast {ratio:0.00}:1 is below 4.5:1");
        }

        report.AppendLine();
        report.AppendLine("control tree:");

        HashSet<Control> zeroSized = [];
        List<(Control A, Control B)> overlaps = [];
        List<(Control Child, Control Parent)> clipped = [];

        Walk(form, 0, report, form, zeroSized, overlaps, clipped);

        report.AppendLine();
        report.AppendLine("findings:");
        foreach (Control control in zeroSized)
        {
            if (!OwnVisible(control) || control is Spacer or FlatProgressBar) continue;
            if (control.Width <= 0) issues.Add($"{Describe(control)} has zero width");
            if (control.Height <= 0) issues.Add($"{Describe(control)} has zero height");
        }

        foreach ((Control child, Control parent) in clipped)
        {
            issues.Add($"{Describe(child)} bounds {child.Bounds} exceed parent {Describe(parent)} client {parent.ClientSize}");
        }

        foreach ((Control a, Control b) in overlaps)
        {
            issues.Add($"{Describe(a)} {a.Bounds} overlaps sibling {Describe(b)} {b.Bounds}");
        }

        if (issues.Count == 0) report.AppendLine("  none - layout and palette checks passed");
        else foreach (string issue in issues) report.AppendLine("  ! " + issue);

        report.AppendLine();
        report.AppendLine($"total issues: {issues.Count}");

        File.AppendAllText(outputPath, report.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"UI audit of {form.GetType().Name}: {issues.Count} issues");
        return issues.Count == 0 ? 0 : 2;
    }

    private static IEnumerable<(string Name, Color Ink, Color Fill)> PalettePairs()
    {
        yield return ("TextPrimary on Surface", Palette.TextPrimary, Palette.Surface);
        yield return ("TextPrimary on SurfaceRaised", Palette.TextPrimary, Palette.SurfaceRaised);
        yield return ("TextPrimary on SurfaceInput", Palette.TextPrimary, Palette.SurfaceInput);
        yield return ("TextSecondary on Canvas", Palette.TextSecondary, Palette.Canvas);
        yield return ("TextSecondary on Surface", Palette.TextSecondary, Palette.Surface);
        yield return ("TextMuted on Surface", Palette.TextMuted, Palette.Surface);
        yield return ("TextMuted on Canvas", Palette.TextMuted, Palette.Canvas);
        yield return ("TextDisabled on SurfaceSunken", Palette.TextDisabled, Palette.SurfaceSunken);
        yield return ("Accent on Canvas", Palette.Accent, Palette.Canvas);
        yield return ("Accent on Surface", Palette.Accent, Palette.Surface);
        yield return ("TextOnAccent on Accent", Palette.TextOnAccent, Palette.Accent);
        yield return ("SelectionText on Selection", Palette.SelectionText, Palette.Selection);
        yield return ("Success on Surface", Palette.Success, Palette.Surface);
        yield return ("Danger on Surface", Palette.Danger, Palette.Surface);
        yield return ("Info on Canvas", Palette.Info, Palette.Canvas);
        yield return ("TextSecondary on SurfaceSunken", Palette.TextSecondary, Palette.SurfaceSunken);
    }

    private static void RevealHiddenPages(Control root)
    {
        foreach (Control control in Descendants(root))
        {
            if (control is SegmentedTabs tabs)
            {
                foreach (SegmentedTabs.TabPage page in tabs.Pages) page.Content.Visible = true;
            }
        }
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control grand in Descendants(child)) yield return grand;
        }
    }

    private static void ApplyLayoutRecursive(Control control)
    {
        control.PerformLayout();
        foreach (Control child in control.Controls) ApplyLayoutRecursive(child);
    }

    private static void Walk(
        Control parent, int depth, StringBuilder report, Form form,
        HashSet<Control> zeroSized, List<(Control, Control)> overlaps, List<(Control, Control)> clipped)
    {
        List<(Control Control, Rectangle Bounds)> placed = [];
        Rectangle parentClientInForm = form.RectangleToClient(parent.RectangleToScreen(parent.ClientRectangle));
        bool scrolls = parent is ScrollHost;

        foreach (Control child in parent.Controls)
        {
            if (child.Width <= 0 || child.Height <= 0) zeroSized.Add(child);

            Rectangle screen = child.RectangleToScreen(child.ClientRectangle);
            Rectangle local = form.RectangleToClient(screen);

            string text = child.Text.Replace("\r", " ").Replace("\n", " ");
            if (text.Length > 40) text = text[..40] + "...";
            bool visible = OwnVisible(child);

            report.AppendLine(
                $"{new string(' ', depth * 2)}[{depth}] {child.GetType().Name,-22} " +
                $"x={local.X,5} y={local.Y,5} w={local.Width,5} h={local.Height,5} " +
                $"vis={(visible ? 1 : 0)} en={(child.Enabled ? 1 : 0)}" +
                (text.Length > 0 ? $"  \"{text}\"" : string.Empty));

            if (visible && child.Width > 0 && child.Height > 0)
            {
                // A scroller legitimately moves its content outside its own client area.
                if (!scrolls && !parentClientInForm.Contains(local))
                {
                    clipped.Add((child, parent));
                }

                foreach ((Control other, Rectangle bounds) in placed)
                {
                    if (bounds.IntersectsWith(local) && !IsIntentionalOverlap(child, other))
                    {
                        overlaps.Add((child, other));
                    }
                }
                placed.Add((child, local));
            }

            if (depth < 12) Walk(child, depth + 1, report, form, zeroSized, overlaps, clipped);
        }
    }

    private static bool IsIntentionalOverlap(Control a, Control b)
    {
        // Placeholder surfaces are deliberately stacked on top of their content host.
        if (a is EmptyState || b is EmptyState) return true;
        if (a is FlatProgressBar || b is FlatProgressBar) return true;
        if (a is Spacer || b is Spacer) return true;

        // Every tab page occupies the same rectangle inside the tab control; only
        // the selected one is painted.
        if (a.Parent is SegmentedTabs && b.Parent is SegmentedTabs) return true;

        // A scroller's content deliberately extends past its own client area.
        if (a.Parent is ScrollHost || b.Parent is ScrollHost) return true;

        return false;
    }

    private static string Describe(Control control)
        => $"{control.GetType().Name}(\"{control.Text}\")";
}
