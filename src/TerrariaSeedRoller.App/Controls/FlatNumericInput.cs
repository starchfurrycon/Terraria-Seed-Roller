using System.Globalization;
using TerrariaSeedRoller.App.Design;

namespace TerrariaSeedRoller.App.Controls;

/// <summary>
/// A numeric field with its own stepper. The system spin button is replaced so
/// the digits, the arrows and the frame all share the application palette.
/// </summary>
internal sealed class FlatNumericInput : FlatTextInput
{
    private readonly System.Windows.Forms.Timer _repeat = new() { Interval = 70 };
    private decimal _value;
    private decimal _minimum;
    private decimal _maximum = 100;
    private decimal _increment = 1;
    private int _stepDirection;
    private bool _hoverUp;
    private bool _hoverDown;

    public FlatNumericInput()
    {
        _repeat.Tick += (_, _) => ApplyStep(_stepDirection);
        Editor.TextAlign = HorizontalAlignment.Left;
    }

    public decimal Minimum
    {
        get => _minimum;
        set { _minimum = value; Value = _value; Invalidate(); }
    }

    public decimal Maximum
    {
        get => _maximum;
        set { _maximum = value; Value = _value; Invalidate(); }
    }

    public decimal Increment
    {
        get => _increment;
        set { _increment = value <= 0 ? 1 : value; Invalidate(); }
    }

    public bool ThousandsSeparator { get; set; } = true;

    /// <summary>Number of digits shown after the decimal separator; 0 renders whole numbers.</summary>
    public int DecimalPlaces { get; set; }

    public string Suffix { get; set; } = string.Empty;

    public decimal Value
    {
        get => _value;
        set
        {
            decimal clamped = Math.Clamp(value, _minimum, _maximum);
            if (clamped == _value && Editor.TextLength > 0) return;
            _value = clamped;
            Editor.Text = Format(_value);
            Invalidate();
        }
    }

    public event EventHandler? ValueChanged;

    private string Format(decimal value)
    {
        if (DecimalPlaces <= 0)
        {
            return ThousandsSeparator
                ? value.ToString("N0", CultureInfo.CurrentCulture)
                : value.ToString("0", CultureInfo.CurrentCulture);
        }

        string pattern = ThousandsSeparator ? "N" + DecimalPlaces : "0." + new string('0', DecimalPlaces);
        return value.ToString(pattern, CultureInfo.CurrentCulture);
    }

    private int StepperWidth => Metrics.Scale(this, 22);

    private int SuffixWidth => Suffix.Length == 0 ? 0 : TextRenderer.MeasureText(Suffix, Typography.Small).Width + Metrics.Scale(this, 6);

    protected override int TrailingInset => StepperWidth + SuffixWidth + Metrics.Scale(this, 6);

    protected override void LayoutEditor()
    {
        base.LayoutEditor();
        Editor.TextAlign = HorizontalAlignment.Left;
    }

    protected override void OnEditorKeyDown(KeyEventArgs e)
    {
        base.OnEditorKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Up:
                CommitEditorText();
                Step(1);
                e.Handled = true;
                break;
            case Keys.Down:
                CommitEditorText();
                Step(-1);
                e.Handled = true;
                break;
            case Keys.Enter:
                CommitEditorText();
                e.Handled = true;
                break;
        }
    }

    /// <summary>Reformats and clamps whatever the user typed.</summary>
    public void CommitEditorText()
    {
        decimal parsed = Parse(Editor.Text);
        decimal clamped = Math.Clamp(parsed, _minimum, _maximum);
        bool changed = clamped != _value;
        _value = clamped;
        string formatted = Format(clamped);
        if (Editor.Text != formatted) Editor.Text = formatted;
        if (changed) ValueChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private static decimal Parse(string text)
    {
        string cleaned = text.Replace(",", string.Empty).Replace(" ", string.Empty).Trim();
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal value) ? value : 0m;
    }

    private void Step(int direction)
    {
        decimal next = Math.Clamp(_value + direction * _increment, _minimum, _maximum);
        if (next == _value) return;
        _value = next;
        Editor.Text = Format(_value);
        ValueChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private void ApplyStep(int direction)
    {
        if (direction == 0) return;
        Step(direction);
    }

    private Rectangle StepperBounds()
    {
        int width = StepperWidth;
        return new Rectangle(ClientSize.Width - width - Metrics.Scale(this, 4), Metrics.Scale(this, 4),
            width, Math.Max(0, ClientSize.Height - Metrics.Scale(this, 8)));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Rectangle stepper = StepperBounds();
        bool up = stepper.Contains(e.X, e.Y) && e.Y < stepper.Top + stepper.Height / 2;
        bool down = stepper.Contains(e.X, e.Y) && !up;
        if (up != _hoverUp || down != _hoverDown)
        {
            _hoverUp = up;
            _hoverDown = down;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverUp = _hoverDown = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Rectangle stepper = StepperBounds();
        if (stepper.Contains(e.X, e.Y))
        {
            CommitEditorText();
            _stepDirection = e.Y < stepper.Top + stepper.Height / 2 ? 1 : -1;
            Step(_stepDirection);
            _repeat.Start();
            return;
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _repeat.Stop();
        _stepDirection = 0;
        base.OnMouseUp(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        CommitEditorText();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _repeat.Stop();
            _repeat.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics graphics = e.Graphics;
        Rectangle stepper = StepperBounds();
        if (stepper.Width <= 0) return;

        if (Suffix.Length > 0)
        {
            int suffixLeft = ClientSize.Width - StepperWidth - Metrics.Scale(this, 8) - SuffixWidth;
            TextRenderer.DrawText(graphics, Suffix, Typography.Small,
                new Rectangle(suffixLeft, 0, SuffixWidth, Height), Palette.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        using (Pen divider = new(Palette.Border, 1f))
        {
            graphics.DrawLine(divider, stepper.Left - Metrics.Scale(this, 3), stepper.Top, stepper.Left - Metrics.Scale(this, 3), stepper.Bottom);
        }

        int arrow = Metrics.Scale(this, 14);
        int half = stepper.Height / 2;
        Color upInk = !Enabled ? Palette.TextDisabled : _hoverUp ? Palette.Accent : Palette.TextSecondary;
        Color downInk = !Enabled ? Palette.TextDisabled : _hoverDown ? Palette.Accent : Palette.TextSecondary;

        Icons.Draw(graphics, AppIcon.ChevronUp,
            new Rectangle(stepper.Left + (stepper.Width - arrow) / 2, stepper.Top + (half - arrow) / 2, arrow, arrow), upInk, 2.6f);
        Icons.Draw(graphics, AppIcon.ChevronDown,
            new Rectangle(stepper.Left + (stepper.Width - arrow) / 2, stepper.Top + half + (half - arrow) / 2, arrow, arrow), downInk, 2.6f);
    }
}
