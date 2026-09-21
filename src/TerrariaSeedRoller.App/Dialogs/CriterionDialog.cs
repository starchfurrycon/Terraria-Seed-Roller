using TerrariaSeedRoller.App.Controls;
using TerrariaSeedRoller.App.Design;
using TerrariaSeedRoller.Core;

namespace TerrariaSeedRoller.App.Dialogs;

/// <summary>Adds or edits a single hard or weighted criterion.</summary>
internal sealed class CriterionDialog : ThemedDialog
{
    private readonly FlatComboBox _metric = new() { DisplaySelector = item => ((MetricDefinition)item).ChineseName };
    private readonly FlatComboBox _kind = new() { DisplaySelector = item => ((Choice<CriterionKind>)item).Name };
    private readonly FlatComboBox _comparison = new() { DisplaySelector = item => ((Choice<MetricComparison>)item).Name };
    private readonly FlatNumericInput _value = new() { Minimum = -1_000_000_000m, Maximum = 1_000_000_000m, DecimalPlaces = 3 };
    private readonly FlatNumericInput _second = new() { Minimum = -1_000_000_000m, Maximum = 1_000_000_000m, DecimalPlaces = 3 };
    private readonly FlatNumericInput _weight = new() { Minimum = 0m, Maximum = 1000m, DecimalPlaces = 2, Increment = 0.5m };
    private readonly FlatCheckBox _enabled = new() { Text = "启用这条条件" };
    private readonly DetailList _description = new();
    private readonly FieldRow _secondRow;
    private readonly FieldRow _weightRow;
    private bool _loading;

    public CriterionDialog(CriterionDefinition? existing, string profileName)
    {
        Text = existing is null ? "添加筛选条件" : "编辑筛选条件";
        SetLogicalClientSize(620, 660);

        _metric.SetItems(MetricCatalog.All.Cast<object>());
        _kind.SetItems(new object[]
        {
            new Choice<CriterionKind>("硬条件 — 不满足直接淘汰", CriterionKind.Hard),
            new Choice<CriterionKind>("加权条件 — 只影响排名得分", CriterionKind.Weighted)
        });
        _comparison.SetItems(new object[]
        {
            new Choice<MetricComparison>("至少 ≥", MetricComparison.AtLeast),
            new Choice<MetricComparison>("至多 ≤", MetricComparison.AtMost),
            new Choice<MetricComparison>("等于 =", MetricComparison.Equal),
            new Choice<MetricComparison>("不等于 ≠", MetricComparison.NotEqual),
            new Choice<MetricComparison>("介于区间 ~", MetricComparison.Between)
        });

        _secondRow = new FieldRow("第二值", _second, 96);
        _weightRow = new FieldRow("权重", _weight, 96) { Hint = "加权条件之间按权重比例累加得分。" };

        VerticalStack stack = new() { Gap = Metrics.RowGap };
        stack.Add(new SectionHeader("条件设置", AppIcon.Filter, profileName));
        stack.Add(new FieldRow("指标", _metric, 96));
        stack.Add(new FieldRow("类型", _kind, 96));
        stack.Add(new FieldRow("比较", _comparison, 96));
        stack.Add(new FieldRow("阈值", _value, 96) { Hint = "支持小数。硬条件表示“必须满足”，加权条件用于换算得分。" });
        stack.Add(_secondRow);
        stack.Add(_weightRow);
        stack.Add(new FieldRow("状态", _enabled, 96));
        stack.Add(new Spacer(6));
        stack.Add(new SectionHeader("指标说明", AppIcon.Info));
        stack.Add(_description);
        _description.Height = Metrics.Scale(this, 130);

        ScrollHost scroller = new(stack) { Dock = DockStyle.Fill, BackColor = Palette.Canvas };
        Panel padded = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Canvas,
            Padding = new Padding(Metrics.Scale(this, 18))
        };
        padded.Controls.Add(scroller);

        FlatButton cancel = new() { Text = "取消", Variant = ButtonVariant.Ghost };
        FlatButton confirm = new() { Text = existing is null ? "添加" : "保存", Variant = ButtonVariant.Primary, Icon = AppIcon.Check };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        confirm.Click += (_, _) => Confirm();

        FlowLayoutPanel bar = CreateButtonBar(cancel, confirm);
        Panel barHost = new()
        {
            Dock = DockStyle.Bottom,
            BackColor = Palette.Canvas,
            Padding = new Padding(Metrics.Scale(this, 18), 0, Metrics.Scale(this, 18), Metrics.Scale(this, 16)),
            Height = Metrics.Scale(this, 68)
        };
        bar.Dock = DockStyle.Right;
        barHost.Controls.Add(bar);

        Controls.Add(padded);
        Controls.Add(barHost);

        LoadValues(existing);
        _metric.SelectedIndexChanged += (_, _) => { if (!_loading) ApplyMetricDefaults(); UpdateDescription(); };
        _kind.SelectedIndexChanged += (_, _) => { if (!_loading) UpdateEnabledState(); };
        _comparison.SelectedIndexChanged += (_, _) => { if (!_loading) UpdateEnabledState(); };
    }

    public CriterionDefinition Result { get; private set; } = new() { MetricKey = MetricCatalog.All[0].Key };

    private void LoadValues(CriterionDefinition? existing)
    {
        _loading = true;
        try
        {
            int metricIndex = 0;
            if (existing is not null)
            {
                for (int i = 0; i < MetricCatalog.All.Count; i++)
                {
                    if (string.Equals(MetricCatalog.All[i].Key, existing.MetricKey, StringComparison.OrdinalIgnoreCase))
                    {
                        metricIndex = i;
                        break;
                    }
                }
            }
            _metric.SelectedIndex = metricIndex;

            CriterionKind kind = existing?.Kind ?? CriterionKind.Hard;
            _kind.SelectedIndex = kind == CriterionKind.Hard ? 0 : 1;

            MetricComparison comparison = existing?.Comparison ?? DefaultComparison(MetricCatalog.All[metricIndex]);
            _comparison.SelectedIndex = ComparisonIndex(comparison);

            _value.Value = (decimal)(existing?.Value ?? 1);
            _second.Value = (decimal)(existing?.SecondValue ?? 0);
            _weight.Value = (decimal)(existing?.Weight ?? 1);
            _enabled.Checked = existing?.Enabled ?? true;
        }
        finally
        {
            _loading = false;
        }

        UpdateEnabledState();
        UpdateDescription();
    }

    private static int ComparisonIndex(MetricComparison comparison) => comparison switch
    {
        MetricComparison.AtLeast => 0,
        MetricComparison.AtMost => 1,
        MetricComparison.Equal => 2,
        MetricComparison.NotEqual => 3,
        _ => 4
    };

    private static MetricComparison DefaultComparison(MetricDefinition definition) => definition.Preference switch
    {
        MetricPreference.LargerIsBetter => MetricComparison.AtLeast,
        MetricPreference.TargetOnly => MetricComparison.Equal,
        _ => MetricComparison.AtMost
    };

    private void ApplyMetricDefaults()
    {
        if (SelectedMetric is { } metric) _comparison.SelectedIndex = ComparisonIndex(DefaultComparison(metric));
        UpdateEnabledState();
    }

    private MetricDefinition? SelectedMetric
        => _metric.SelectedItem as MetricDefinition
           ?? (_metric.SelectedIndex >= 0 && _metric.SelectedIndex < MetricCatalog.All.Count ? MetricCatalog.All[_metric.SelectedIndex] : null);

    private CriterionKind SelectedKind => _kind.SelectedItem is Choice<CriterionKind> choice ? choice.Value : CriterionKind.Hard;

    private MetricComparison SelectedComparison
        => _comparison.SelectedItem is Choice<MetricComparison> choice ? choice.Value : MetricComparison.AtMost;

    private void UpdateEnabledState()
    {
        _secondRow.Enabled = SelectedComparison == MetricComparison.Between;
        _weightRow.Enabled = SelectedKind == CriterionKind.Weighted;
    }

    private void UpdateDescription()
    {
        MetricDefinition? metric = SelectedMetric;
        if (metric is null)
        {
            _description.SetEntries([]);
            return;
        }

        _description.SetEntries(
        [
            new DetailList.Field("指标键", metric.Key),
            new DetailList.Field("英文名", metric.EnglishName),
            new DetailList.Field("单位", metric.Unit.Length > 0 ? metric.Unit : "无"),
            new DetailList.Field("方向", metric.Preference switch
            {
                MetricPreference.LargerIsBetter => "越大越好",
                MetricPreference.SmallerIsBetter => "越小越好",
                _ => "只看目标值"
            }),
            new DetailList.Field("分类", metric.Category),
            new DetailList.Divider(),
            new DetailList.Note(metric.Description)
        ]);
    }

    private void Confirm()
    {
        MetricDefinition? metric = SelectedMetric;
        if (metric is null) return;

        Result = new CriterionDefinition
        {
            Enabled = _enabled.Checked,
            MetricKey = metric.Key,
            Label = metric.ChineseName,
            Kind = SelectedKind,
            Comparison = SelectedComparison,
            Value = (double)_value.Value,
            SecondValue = (double)_second.Value,
            Weight = SelectedKind == CriterionKind.Weighted ? Math.Max(0.01, (double)_weight.Value) : 1
        };
        DialogResult = DialogResult.OK;
        Close();
    }
}
