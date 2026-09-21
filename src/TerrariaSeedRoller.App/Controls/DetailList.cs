using System.Drawing.Drawing2D;
using TerrariaSeedRoller.App.Design;

namespace TerrariaSeedRoller.App.Controls;

/// <summary>
/// A stacked key/value readout used for world details. It renders section
/// headings, aligned label/value rows and wrapped notes, which reads far better
/// than a single block of concatenated text.
/// </summary>
internal sealed class DetailList : ThemedControl
{
    internal abstract record Entry;

    internal sealed record Section(string Title) : Entry;

    internal sealed record Field(string Label, string Value, Color? ValueColor = null, AppIcon? Icon = null) : Entry;

    internal sealed record Note(string Text) : Entry;

    internal sealed record Divider : Entry;

    internal sealed record Spacer(int Height = 6) : Entry;

    private readonly List<Entry> _entries = [];
    private readonly List<(Entry Entry, RectangleF Bounds)> _layout = [];
    private int _layoutWidth = -1;
    private int _layoutVersion = -1;
    private int _version;

    public DetailList()
    {
        BackColor = Palette.Surface;
    }

    public void SetEntries(IEnumerable<Entry> entries)
    {
        _entries.Clear();
        _entries.AddRange(entries);
        _version++;
        _layoutWidth = -1;
        UpdateHeight();
        Invalidate();
    }

    private int LabelWidth => Metrics.Scale(this, 108);

    private void BuildLayout()
    {
        int width = Math.Max(Metrics.Scale(this, 120), Width);
        if (_layoutWidth == width && _layoutVersion == _version) return;

        _layout.Clear();
        float y = 0;
        foreach (Entry entry in _entries)
        {
            float height = entry switch
            {
                Section => Metrics.Scale(this, 34),
                Field field => FieldHeight(field, width),
                Note note => NoteHeight(note, width),
                Divider => Metrics.Scale(this, 11),
                Spacer spacer => Metrics.Scale(this, spacer.Height),
                _ => Metrics.Scale(this, 24)
            };
            _layout.Add((entry, new RectangleF(0, y, width, height)));
            y += height;
        }

        _layoutWidth = width;
        _layoutVersion = _version;
    }

    private int FieldHeight(Field field, int width)
    {
        int valueWidth = Math.Max(Metrics.Scale(this, 60), width - LabelWidth - Metrics.Scale(this, 8));
        Size size = TextRenderer.MeasureText(field.Value, Typography.Body,
            new Size(valueWidth, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        return Math.Max(Metrics.Scale(this, 26), size.Height + Metrics.Scale(this, 8));
    }

    private int NoteHeight(Note note, int width)
    {
        Size size = TextRenderer.MeasureText(note.Text, Typography.Body,
            new Size(Math.Max(Metrics.Scale(this, 80), width), int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        return size.Height + Metrics.Scale(this, 10);
    }

    public int PreferredContentHeight
    {
        get
        {
            BuildLayout();
            float bottom = _layout.Count == 0 ? 0 : _layout[^1].Bounds.Bottom;
            return (int)Math.Ceiling(bottom) + Metrics.Scale(this, 4);
        }
    }

    public override Size GetPreferredSize(Size proposedSize) => new(proposedSize.Width, PreferredContentHeight);

    private void UpdateHeight()
    {
        int target = PreferredContentHeight;
        if (Height != target) Height = target;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_layoutWidth != Math.Max(Metrics.Scale(this, 120), Width)) Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(SurfaceColor);
        BuildLayout();

        int pad = Metrics.Scale(this, 4);
        foreach ((Entry entry, RectangleF bounds) in _layout)
        {
            switch (entry)
            {
                case Section section:
                    TextRenderer.DrawText(graphics, section.Title, Typography.Section,
                        new Rectangle((int)bounds.X, (int)bounds.Y + Metrics.Scale(this, 8), (int)bounds.Width, Metrics.Scale(this, 22)),
                        Palette.Accent,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    using (Pen pen = new(Palette.Border, 1f))
                    {
                        graphics.DrawLine(pen, bounds.X, bounds.Bottom - 0.5f, bounds.Right, bounds.Bottom - 0.5f);
                    }
                    break;

                case Divider:
                    using (Pen pen = new(Palette.GridLine, 1f))
                    {
                        graphics.DrawLine(pen, bounds.X, bounds.Y + bounds.Height / 2, bounds.Right, bounds.Y + bounds.Height / 2);
                    }
                    break;

                case Field field:
                    int iconSize = Metrics.Scale(this, 14);
                    int labelLeft = (int)bounds.X + pad;
                    if (field.Icon is { } icon)
                    {
                        Icons.Draw(graphics, icon,
                            new Rectangle(labelLeft, (int)bounds.Y + Metrics.Scale(this, 6), iconSize, iconSize), Palette.TextMuted, 2.2f);
                        labelLeft += iconSize + Metrics.Scale(this, 6);
                    }

                    TextRenderer.DrawText(graphics, field.Label, Typography.Body,
                        new Rectangle(labelLeft, (int)bounds.Y, LabelWidth - (labelLeft - (int)bounds.X), (int)bounds.Height),
                        Palette.TextMuted,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

                    int valueLeft = (int)bounds.X + LabelWidth + Metrics.Scale(this, 8);
                    TextRenderer.DrawText(graphics, field.Value, Typography.Body,
                        new Rectangle(valueLeft, (int)bounds.Y, Math.Max(0, (int)bounds.Width - valueLeft), (int)bounds.Height),
                        field.ValueColor ?? Palette.TextPrimary,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                    break;

                case Note note:
                    TextRenderer.DrawText(graphics, note.Text, Typography.Body,
                        new Rectangle((int)bounds.X + pad, (int)bounds.Y + Metrics.Scale(this, 4), (int)bounds.Width - pad * 2, (int)bounds.Height),
                        Palette.TextMuted,
                        TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                    break;
            }
        }
    }
}
