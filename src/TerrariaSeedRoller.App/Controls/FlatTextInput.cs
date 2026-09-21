using System.Drawing.Drawing2D;
using TerrariaSeedRoller.App.Design;

namespace TerrariaSeedRoller.App.Controls;

/// <summary>
/// A single-line text field drawn as a rounded, palette-coloured frame with an
/// embedded borderless editor. The editor never inherits a system theme, so its
/// text can never end up light-on-light.
/// </summary>
internal class FlatTextInput : ThemedControl
{
    protected readonly TextBox Editor = new()
    {
        BorderStyle = BorderStyle.None,
        BackColor = Palette.SurfaceInput,
        ForeColor = Palette.TextPrimary
    };

    private string _placeholder = string.Empty;
    private AppIcon? _icon;
    private bool _hover;

    public FlatTextInput()
    {
        SetStyle(ControlStyles.Selectable, true);
        BackColor = Palette.SurfaceInput;
        Editor.Font = Typography.Body;
        Controls.Add(Editor);
        Height = Metrics.Scale(this, Metrics.ControlHeight);
        Editor.TextChanged += (_, _) => { OnTextChanged(EventArgs.Empty); Invalidate(); };
        Editor.GotFocus += (_, _) => Invalidate();
        Editor.LostFocus += (_, _) => Invalidate();
        Editor.KeyDown += (_, e) => OnEditorKeyDown(e);
    }

    public string PlaceholderText
    {
        get => _placeholder;
        set { _placeholder = value ?? string.Empty; Invalidate(); }
    }

    public AppIcon? LeadingIcon
    {
        get => _icon;
        set { _icon = value; PerformLayout(); Invalidate(); }
    }

    public TextBox EditorControl => Editor;

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => Editor.Text;
        set { Editor.Text = value ?? string.Empty; Invalidate(); }
    }

    public bool ReadOnlyEditor
    {
        get => Editor.ReadOnly;
        set { Editor.ReadOnly = value; Invalidate(); }
    }

    public bool UseMonospaceFont
    {
        get => Editor.Font == Typography.Mono;
        set { Editor.Font = value ? Typography.Mono : Typography.Body; PerformLayout(); Invalidate(); }
    }

    protected virtual void OnEditorKeyDown(KeyEventArgs e)
    {
    }

    protected virtual int TrailingInset => Metrics.Scale(this, 10);

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        LayoutEditor();
    }

    protected virtual void LayoutEditor()
    {
        int pad = Metrics.Scale(this, 10);
        int left = pad;
        if (_icon is not null) left += Metrics.Scale(this, 16) + Metrics.Scale(this, 8);
        int right = TrailingInset;
        int height = Editor.PreferredHeight;
        int width = Math.Max(0, ClientSize.Width - left - right);
        Editor.SetBounds(left, Math.Max(0, (ClientSize.Height - height) / 2), width, height);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Editor.Focused) Editor.Focus();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Editor.Focus();
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Editor.Enabled = Enabled;
        Editor.BackColor = Enabled ? Palette.SurfaceInput : Palette.SurfaceSunken;
        Editor.ForeColor = Enabled ? Palette.TextPrimary : Palette.TextDisabled;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(SurfaceColor);

        bool focused = Editor.Focused;
        Color fill = Enabled ? Palette.SurfaceInput : Palette.SurfaceSunken;
        Color border = !Enabled ? Palette.Border
            : focused ? Palette.FocusRing
            : _hover ? Palette.BorderStrong
            : Palette.Border;

        RectangleF bounds = Metrics.Hairline(0, 0, Width, Height);
        float radius = Metrics.ScaleF(this, Metrics.ControlRadius);
        using (GraphicsPath path = Metrics.RoundedRect(bounds, radius))
        {
            using SolidBrush brush = new(fill);
            graphics.FillPath(brush, path);
            using Pen pen = new(border, focused ? 1.4f : 1f);
            graphics.DrawPath(pen, path);
        }

        if (_icon is not null)
        {
            int size = Metrics.Scale(this, 16);
            int pad = Metrics.Scale(this, 10);
            Icons.Draw(graphics, _icon.Value, new Rectangle(pad, (Height - size) / 2, size, size),
                Enabled ? Palette.TextMuted : Palette.TextDisabled);
        }

        if (Editor.TextLength == 0 && _placeholder.Length > 0 && !focused)
        {
            int left = Metrics.Scale(this, 10) + (_icon is not null ? Metrics.Scale(this, 24) : 0);
            TextRenderer.DrawText(graphics, _placeholder, Typography.Body,
                new Rectangle(left, 0, Math.Max(0, Width - left - TrailingInset), Height), Palette.TextDisabled,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }
}
