using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;
using TerrariaSeedRoller.App.Controls;
using TerrariaSeedRoller.App.Design;
using TerrariaSeedRoller.App.Dialogs;
using TerrariaSeedRoller.Core;

namespace TerrariaSeedRoller.App;

internal sealed class MainForm : Form
{
    private const int SettingsColumnWidth = 430;

    // ---- generator settings -------------------------------------------------
    private readonly FlatTextInput _serverPath = new() { PlaceholderText = "选择 TerrariaServer.exe", LeadingIcon = AppIcon.Terminal };
    private readonly FlatTextInput _outputPath = new() { PlaceholderText = "Roll 结果输出目录", LeadingIcon = AppIcon.Folder };
    private readonly FlatComboBox _size = new() { DisplaySelector = item => ((Choice<WorldSize>)item).Name, LeadingIcon = AppIcon.Earth };
    private readonly FlatComboBox _difficulty = new() { DisplaySelector = item => ((Choice<WorldDifficulty>)item).Name };
    private readonly FlatComboBox _evil = new() { DisplaySelector = item => ((Choice<WorldEvil>)item).Name };
    private readonly ChipGroup _specialSeeds = new();
    private readonly FlatNumericInput _startSeed = new() { Minimum = int.MinValue, Maximum = int.MaxValue, ThousandsSeparator = true };
    private readonly FlatNumericInput _endSeed = new() { Minimum = int.MinValue, Maximum = int.MaxValue, ThousandsSeparator = true };
    private readonly FlatCheckBox _randomOrder = new() { Text = "随机无重复顺序" };
    private readonly FlatNumericInput _attempts = new() { Minimum = 1, Maximum = 10_000_000, Value = 20 };
    private readonly FlatNumericInput _winners = new() { Minimum = 1, Maximum = 10_000, Value = 3 };
    private readonly FlatNumericInput _parallel = new() { Minimum = 1, Maximum = 8, Value = 1 };
    private readonly FlatComboBox _priority = new() { DisplaySelector = item => ((Choice<ServerProcessPriority>)item).Name };
    private readonly FlatNumericInput _timeout = new() { Minimum = 1, Maximum = 120, Value = 10, Suffix = "分钟" };
    private readonly FlatCheckBox _keepRejected = new() { Text = "保留未通过筛选的临时世界" };
    private readonly FlatComboBox _preset = new() { DisplaySelector = item => ((RollProfile)item).Name, LeadingIcon = AppIcon.Layers };
    private FieldRow _presetRow = null!;

    // ---- criteria -----------------------------------------------------------
    private readonly CriterionList _criteria = new();

    // ---- results ------------------------------------------------------------
    private readonly TableView _results = new();
    private readonly MapView _map = new();
    private readonly MapLegend _legend = new();
    private readonly DetailList _details = new();
    private readonly EmptyState _resultsEmpty = new()
    {
        Title = "还没有候选世界",
        Description = "设置好世界参数和筛选条件后点击“开始 Roll 种”，或先用“只读分析现有世界”查看一个已有世界的报告。",
        Icon = AppIcon.Mountain
    };
    private readonly FlatButton _openReport = new() { Text = "打开报告", Icon = AppIcon.ExternalLink, Variant = ButtonVariant.Ghost, Enabled = false };
    private readonly FlatButton _copySeed = new() { Text = "复制种子", Icon = AppIcon.Copy, Variant = ButtonVariant.Ghost, Enabled = false };

    // ---- log ----------------------------------------------------------------
    private readonly RichTextBox _log = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        BorderStyle = BorderStyle.None,
        BackColor = Palette.SurfaceSunken,
        ForeColor = Palette.TextSecondary,
        Font = Typography.Mono,
        WordWrap = false,
        DetectUrls = false,
        ScrollBars = RichTextBoxScrollBars.Both,
        ShortcutsEnabled = true
    };

    // ---- chrome -------------------------------------------------------------
    private readonly FooterBar _footer = new();
    private readonly FlatButton _start = new() { Text = "开始 Roll 种", Icon = AppIcon.Play, Variant = ButtonVariant.Primary };
    private readonly FlatButton _pause = new() { Text = "暂停", Icon = AppIcon.Pause, Variant = ButtonVariant.Secondary, Enabled = false };
    private readonly FlatButton _cancel = new() { Text = "取消", Icon = AppIcon.Stop, Variant = ButtonVariant.Danger, Enabled = false };
    private readonly FlatButton _analyze = new() { Text = "只读分析现有世界", Icon = AppIcon.Search, Variant = ButtonVariant.Secondary };
    private readonly SegmentedTabs _tabs = new();

    private readonly List<RollResult> _rollResults = [];
    private CancellationTokenSource? _cancellation;
    private PauseController? _pauseController;
    private string? _lastReportDirectory;

    public MainForm()
    {
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Palette.Canvas;
        ForeColor = Palette.TextPrimary;
        Font = Typography.Body;
        Text = "Terraria Seed Roller — 原版真实世界 Roll 种机";
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        DoubleBuffered = true;

        ApplyDpiSizing();

        _serverPath.Text = FindTerrariaServer();
        _outputPath.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "My Games", "Terraria", "SeedRollerOutput");

        SeedChoices();
        BuildLayout();
        WireEvents();
        LoadProfile(BuiltInProfiles.All[0]);
    }

    // =======================================================================
    // Layout
    // =======================================================================

    /// <summary>Number of workspace tabs; used by the diagnostic capture mode.</summary>
    internal int WorkspaceTabCount => _tabs.Pages.Count;

    /// <summary>Selects a workspace tab; used by the diagnostic capture mode.</summary>
    internal void SelectWorkspaceTab(int index) => _tabs.SelectedIndex = index;

    private void ApplyDpiSizing()
    {
        MinimumSize = new Size(Metrics.Scale(this, 960), Metrics.Scale(this, 620));
        Rectangle work = (Screen.FromPoint(Cursor.Position) ?? Screen.PrimaryScreen)?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        int width = Math.Min(Metrics.Scale(this, 1500), Math.Max(MinimumSize.Width, work.Width - Metrics.Scale(this, 40)));
        int height = Math.Min(Metrics.Scale(this, 940), Math.Max(MinimumSize.Height, work.Height - Metrics.Scale(this, 40)));
        Size = new Size(width, height);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyDpiSizing();
        PerformLayout();
        Invalidate(true);
    }

    private void SeedChoices()
    {
        _size.SetItems(new object[]
        {
            new Choice<WorldSize>("小世界 · 快速生成", WorldSize.Small),
            new Choice<WorldSize>("中世界 · 平衡", WorldSize.Medium),
            new Choice<WorldSize>("大世界 · 资源最多", WorldSize.Large)
        });
        _difficulty.SetItems(new object[]
        {
            new Choice<WorldDifficulty>("经典", WorldDifficulty.Classic),
            new Choice<WorldDifficulty>("专家", WorldDifficulty.Expert),
            new Choice<WorldDifficulty>("大师", WorldDifficulty.Master),
            new Choice<WorldDifficulty>("旅途", WorldDifficulty.Journey)
        });
        _evil.SetItems(new object[]
        {
            new Choice<WorldEvil>("随机", WorldEvil.Random),
            new Choice<WorldEvil>("腐化", WorldEvil.Corruption),
            new Choice<WorldEvil>("猩红", WorldEvil.Crimson)
        });
        _priority.SetItems(new object[]
        {
            new Choice<ServerProcessPriority>("均衡（推荐）", ServerProcessPriority.Balanced),
            new Choice<ServerProcessPriority>("最快", ServerProcessPriority.Fastest),
            new Choice<ServerProcessPriority>("较高", ServerProcessPriority.AboveNormal),
            new Choice<ServerProcessPriority>("低影响", ServerProcessPriority.LowImpact),
            new Choice<ServerProcessPriority>("空闲时运行", ServerProcessPriority.Idle)
        });
        _preset.SetItems(BuiltInProfiles.All.Cast<object>());

        _specialSeeds.SetItems(
            Enum.GetValues<SpecialSeedFlags>()
                .Where(value => value != SpecialSeedFlags.None)
                .Select(value => (DescribeSpecialSeed(value), (object)value)),
            _ => false);
    }

    private static string DescribeSpecialSeed(SpecialSeedFlags flag) => flag switch
    {
        SpecialSeedFlags.NotTheBees => "Not the Bees",
        SpecialSeedFlags.Drunk => "Drunk world",
        SpecialSeedFlags.Celebration => "Celebration",
        SpecialSeedFlags.TheConstant => "The Constant",
        SpecialSeedFlags.ForTheWorthy => "For the Worthy",
        SpecialSeedFlags.NoTraps => "No Traps",
        SpecialSeedFlags.Remix => "Remix",
        SpecialSeedFlags.Zenith => "Zenith",
        SpecialSeedFlags.Skyblock => "Skyblock",
        _ => flag.ToString()
    };

    private void BuildLayout()
    {
        SuspendLayout();

        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Palette.Canvas,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, Metrics.Scale(this, 72)));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, Metrics.Scale(this, 88)));

        HeaderBar header = new(
            "Terraria Seed Roller",
            "调用本机原版 TerrariaServer 真实生成世界，只读分析并排名",
            "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.2.0"))
        {
            Dock = DockStyle.Fill
        };

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(BuildBody(), 0, 1);
        root.Controls.Add(BuildFooter(), 0, 2);

        Controls.Add(root);
        ResumeLayout(true);
    }

    private Control BuildBody()
    {
        TableLayoutPanel body = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Palette.Canvas,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Metrics.Scale(this, SettingsColumnWidth)));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Metrics.Scale(this, 6)));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        body.Controls.Add(BuildSettingsColumn(), 0, 0);
        body.Controls.Add(new Spacer(6), 1, 0);
        body.Controls.Add(BuildWorkspace(), 2, 0);
        return body;
    }

    private Control BuildSettingsColumn()
    {
        VerticalStack stack = new() { Gap = Metrics.RowGap };

        FlatButton browseServer = new() { Text = "浏览", Icon = AppIcon.FolderOpen, Variant = ButtonVariant.Secondary };
        browseServer.Click += (_, _) => BrowseServer();
        FlatButton browseOutput = new() { Text = "浏览", Icon = AppIcon.FolderOpen, Variant = ButtonVariant.Secondary };
        browseOutput.Click += (_, _) => BrowseOutput();

        stack.Add(new SectionHeader("生成器", AppIcon.Terminal, "使用已安装的原版服务端"));
        stack.Add(new FieldRow("服务端", _serverPath, Metrics.LabelColumn)
        {
            Trailing = browseServer,
            TrailingWidth = 74
        });
        stack.Add(new FieldRow("输出目录", _outputPath, Metrics.LabelColumn)
        {
            Trailing = browseOutput,
            TrailingWidth = 74
        });

        stack.Add(new Spacer(6));
        stack.Add(new SectionHeader("世界参数", AppIcon.Earth));
        stack.Add(new FieldRow("世界大小", _size, Metrics.LabelColumn));
        stack.Add(new FieldRow("难度", _difficulty, Metrics.LabelColumn));
        stack.Add(new FieldRow("邪恶", _evil, Metrics.LabelColumn));
        stack.Add(new FieldRow("特殊种子", _specialSeeds, Metrics.LabelColumn) { AutoHeightFromField = true });

        stack.Add(new Spacer(6));
        stack.Add(new SectionHeader("种子范围", AppIcon.Dice));
        stack.Add(new FieldRow("起始种子", _startSeed, Metrics.LabelColumn));
        stack.Add(new FieldRow("结束种子", _endSeed, Metrics.LabelColumn));
        stack.Add(new FieldRow("顺序", _randomOrder, Metrics.LabelColumn) { FieldHeight = 24 });
        stack.Add(new FieldRow("最多尝试", _attempts, Metrics.LabelColumn) { Hint = "每个尝试都会真实生成并分析一个世界。" });
        stack.Add(new FieldRow("保留前 N", _winners, Metrics.LabelColumn));

        stack.Add(new Spacer(6));
        stack.Add(new SectionHeader("运行与资源", AppIcon.Sliders));
        stack.Add(new FieldRow("并发数", _parallel, Metrics.LabelColumn) { Hint = "并发 1 配合均衡策略最稳妥，提高会显著增加 CPU、内存和磁盘压力。" });
        stack.Add(new FieldRow("资源策略", _priority, Metrics.LabelColumn));
        stack.Add(new FieldRow("单世界超时", _timeout, Metrics.LabelColumn));
        stack.Add(new FieldRow("临时文件", _keepRejected, Metrics.LabelColumn) { FieldHeight = 24 });

        stack.Add(new Spacer(6));
        stack.Add(new SectionHeader("筛选预设", AppIcon.Layers));
        _presetRow = new FieldRow("预设", _preset, Metrics.LabelColumn) { Hint = BuiltInProfiles.All[0].Description };
        stack.Add(_presetRow);

        ScrollHost scroller = new(stack) { Dock = DockStyle.Fill, BackColor = Palette.Canvas };
        SurfaceCard card = new()
        {
            Dock = DockStyle.Fill,
            Title = "Roll 种设置",
            Subtitle = "全部参数都会写入结果目录的 session.json",
            HeaderIcon = AppIcon.Sliders
        };
        card.Controls.Add(scroller);
        scroller.Dock = DockStyle.Fill;

        Panel host = new() { Dock = DockStyle.Fill, BackColor = Palette.Canvas, Padding = new Padding(Metrics.Scale(this, 14), Metrics.Scale(this, 14), Metrics.Scale(this, 7), Metrics.Scale(this, 14)) };
        host.Controls.Add(card);
        return host;
    }

    private Control BuildWorkspace()
    {
        _tabs.AddPage(new SegmentedTabs.TabPage("筛选条件", AppIcon.ListChecks, BuildCriteriaTab()));
        _tabs.AddPage(new SegmentedTabs.TabPage("结果与地图", AppIcon.Map, BuildResultsTab()));
        _tabs.AddPage(new SegmentedTabs.TabPage("运行日志", AppIcon.ScrollText, BuildLogTab()));
        _tabs.Dock = DockStyle.Fill;

        Panel host = new() { Dock = DockStyle.Fill, BackColor = Palette.Canvas, Padding = new Padding(Metrics.Scale(this, 7), Metrics.Scale(this, 14), Metrics.Scale(this, 14), Metrics.Scale(this, 14)) };
        host.Controls.Add(_tabs);
        return host;
    }

    private Control BuildCriteriaTab()
    {
        TableLayoutPanel layout = Grid(
            [Col(SizeType.Percent, 100)],
            [Row(SizeType.Absolute, Metrics.Scale(this, 46)), Row(SizeType.Percent, 100)]);

        FlatButton add = new() { Text = "添加指标", Icon = AppIcon.Plus, Variant = ButtonVariant.Primary };
        FlatButton edit = new() { Text = "编辑", Icon = AppIcon.Sliders, Variant = ButtonVariant.Secondary };
        FlatButton remove = new() { Text = "删除", Icon = AppIcon.Trash, Variant = ButtonVariant.Ghost };
        FlatButton up = new() { Text = "上移", Icon = AppIcon.ChevronUp, Variant = ButtonVariant.Ghost };
        FlatButton down = new() { Text = "下移", Icon = AppIcon.ChevronDown, Variant = ButtonVariant.Ghost };
        FlatButton reset = new() { Text = "恢复预设", Icon = AppIcon.Reset, Variant = ButtonVariant.Ghost };

        add.Click += (_, _) => AddCriterion();
        edit.Click += (_, _) => EditSelectedCriterion();
        remove.Click += (_, _) => _criteria.RemoveSelected();
        up.Click += (_, _) => _criteria.MoveSelected(-1);
        down.Click += (_, _) => _criteria.MoveSelected(1);
        reset.Click += (_, _) =>
        {
            if (_preset.SelectedItem is RollProfile profile) LoadProfile(profile);
        };

        FlowLayoutPanel toolbar = new()
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            AutoSize = true,
            BackColor = Palette.Canvas,
            Padding = new Padding(0, 0, 0, Metrics.Scale(this, 8))
        };
        foreach (Control control in new Control[] { add, edit, remove, up, down, reset })
        {
            control.Margin = new Padding(0, 0, Metrics.Scale(this, 8), 0);
            toolbar.Controls.Add(control);
        }

        SurfaceCard card = new()
        {
            Dock = DockStyle.Fill,
            Title = "筛选条件",
            Subtitle = "硬条件负责淘汰，加权条件只影响排名得分",
            HeaderIcon = AppIcon.ListChecks
        };
        card.Controls.Add(_criteria);
        _criteria.Dock = DockStyle.Fill;

        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(card, 0, 1);
        return layout;
    }

    private Control BuildResultsTab()
    {
        TableLayoutPanel layout = Grid(
            [Col(SizeType.Percent, 100)],
            [Row(SizeType.Percent, 42), Row(SizeType.Percent, 58)]);

        // --- ranking table ---------------------------------------------------
        SurfaceCard tableCard = new()
        {
            Dock = DockStyle.Fill,
            Title = "候选世界排名",
            Subtitle = "双击一行可打开该世界的 HTML 报告",
            HeaderIcon = AppIcon.Chart,
            HeaderToolbarWidth = 200,
            Margin = new Padding(0, 0, 0, Metrics.Scale(this, 8))
        };
        ConfigureResultColumns();
        _results.Dock = DockStyle.Fill;
        _resultsEmpty.Dock = DockStyle.Fill;
        _resultsEmpty.Visible = false;
        Panel tableHost = new() { Dock = DockStyle.Fill, BackColor = Palette.Surface };
        tableHost.Controls.Add(_results);
        tableHost.Controls.Add(_resultsEmpty);
        tableCard.Controls.Add(tableHost);

        // --- map and details -------------------------------------------------
        TableLayoutPanel lower = Grid(
            [Col(SizeType.Percent, 58), Col(SizeType.Absolute, Metrics.Scale(this, 8)), Col(SizeType.Percent, 42)],
            [Row(SizeType.Percent, 100)]);

        SurfaceCard mapCard = new()
        {
            Dock = DockStyle.Fill,
            Title = "世界概览",
            Subtitle = "每像素代表 8×8 格 · 滚轮缩放，拖动平移，双击复位",
            HeaderIcon = AppIcon.Map,
            HeaderToolbarWidth = 130
        };
        TableLayoutPanel mapLayout = Grid(
            [Col(SizeType.Percent, 100)],
            [Row(SizeType.Percent, 100), Row(SizeType.Absolute, Metrics.Scale(this, 66))],
            Palette.Surface);
        _map.Dock = DockStyle.Fill;
        _legend.Dock = DockStyle.Fill;
        mapLayout.Controls.Add(_map, 0, 0);
        mapLayout.Controls.Add(_legend, 0, 1);
        mapCard.Controls.Add(mapLayout);

        SurfaceCard detailCard = new()
        {
            Dock = DockStyle.Fill,
            Title = "世界详情",
            Subtitle = "选中世界的关键指标",
            HeaderIcon = AppIcon.FileText,
            HeaderToolbarWidth = 190
        };
        ScrollHost detailScroll = new(_details) { Dock = DockStyle.Fill };
        detailCard.Controls.Add(detailScroll);

        lower.Controls.Add(mapCard, 0, 0);
        lower.Controls.Add(new Spacer(8), 1, 0);
        lower.Controls.Add(detailCard, 2, 0);

        layout.Controls.Add(tableCard, 0, 0);
        layout.Controls.Add(lower, 0, 1);
        return layout;
    }

    private void ConfigureResultColumns()
    {
        if (_results.Columns.Count > 0) return;
        _results.AddColumn(new TableView.Column { Header = "排名", Weight = 0.45f, MinWidth = 48, Alignment = ContentAlignment.MiddleCenter });
        _results.AddColumn(new TableView.Column { Header = "复制种子", Weight = 1.6f, MinWidth = 120, Emphasised = true });
        _results.AddColumn(new TableView.Column { Header = "得分", Weight = 0.7f, MinWidth = 60, Alignment = ContentAlignment.MiddleRight, Format = value => FormatNumber(value, "0.##") });
        _results.AddColumn(new TableView.Column { Header = "邪恶宽度", Weight = 0.8f, MinWidth = 70, Alignment = ContentAlignment.MiddleRight, Suffix = " 格", Format = value => FormatNumber(value, "0") });
        _results.AddColumn(new TableView.Column { Header = "肉前蔓延", Weight = 0.8f, MinWidth = 70, Alignment = ContentAlignment.MiddleRight, Suffix = " 格", Format = value => FormatNumber(value, "0") });
        _results.AddColumn(new TableView.Column { Header = "宝箱", Weight = 0.6f, MinWidth = 56, Alignment = ContentAlignment.MiddleRight, Format = value => FormatNumber(value, "0") });
        _results.AddColumn(new TableView.Column { Header = "生成秒", Weight = 0.7f, MinWidth = 66, Alignment = ContentAlignment.MiddleRight, Format = value => FormatNumber(value, "0.0") });
        _results.AddColumn(new TableView.Column { Header = "分析秒", Weight = 0.7f, MinWidth = 66, Alignment = ContentAlignment.MiddleRight, Format = value => FormatNumber(value, "0.0") });
    }

    private Control BuildLogTab()
    {
        TableLayoutPanel layout = Grid(
            [Col(SizeType.Percent, 100)],
            [Row(SizeType.Absolute, Metrics.Scale(this, 46)), Row(SizeType.Percent, 100)]);

        FlatButton clear = new() { Text = "清空", Icon = AppIcon.Trash, Variant = ButtonVariant.Ghost };
        FlatButton copy = new() { Text = "复制全部", Icon = AppIcon.Copy, Variant = ButtonVariant.Secondary };
        FlatButton openOutput = new() { Text = "打开输出目录", Icon = AppIcon.FolderOpen, Variant = ButtonVariant.Secondary };
        clear.Click += (_, _) => _log.Clear();
        copy.Click += (_, _) =>
        {
            if (_log.TextLength == 0) return;
            Clipboard.SetText(_log.Text);
            SetStatus("日志已复制到剪贴板", AppIcon.Success, Palette.Success);
        };
        openOutput.Click += (_, _) => OpenDirectory(_outputPath.Text);

        FlowLayoutPanel toolbar = new()
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            AutoSize = true,
            BackColor = Palette.Canvas,
            Padding = new Padding(0, 0, 0, Metrics.Scale(this, 8))
        };
        foreach (Control control in new Control[] { copy, clear, openOutput })
        {
            control.Margin = new Padding(0, 0, Metrics.Scale(this, 8), 0);
            toolbar.Controls.Add(control);
        }

        SurfaceCard card = new()
        {
            Dock = DockStyle.Fill,
            Title = "运行日志",
            Subtitle = "生成与分析过程的实时输出",
            HeaderIcon = AppIcon.ScrollText
        };
        Panel logHost = new() { Dock = DockStyle.Fill, BackColor = Palette.SurfaceSunken, Padding = new Padding(Metrics.Scale(this, 6)) };
        logHost.Controls.Add(_log);
        card.Controls.Add(logHost);

        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(card, 0, 1);
        return layout;
    }

    private Control BuildFooter()
    {
        _footer.Dock = DockStyle.Fill;
        _footer.AddButton(_start);
        _footer.AddButton(_cancel);
        _footer.AddButton(_pause);
        _footer.AddButton(_analyze);
        return _footer;
    }

    /// <summary>
    /// Builds a table whose row and column styles are supplied explicitly. The
    /// style collections are filled exactly once so a later <c>Add</c> can never
    /// be silently ignored, which is what made the old layout drift.
    /// </summary>
    private static TableLayoutPanel Grid(ColumnStyle[] columns, RowStyle[] rows, Color? background = null)
    {
        TableLayoutPanel table = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = columns.Length,
            RowCount = rows.Length,
            BackColor = background ?? Palette.Canvas,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize
        };
        foreach (ColumnStyle column in columns) table.ColumnStyles.Add(column);
        foreach (RowStyle row in rows) table.RowStyles.Add(row);
        return table;
    }

    private static ColumnStyle Col(SizeType type, float value) => new(type, value);

    private static RowStyle Row(SizeType type, float value) => new(type, value);

    // =======================================================================
    // Events
    // =======================================================================

    private void WireEvents()
    {
        _preset.SelectedIndexChanged += (_, _) =>
        {
            if (_preset.SelectedItem is RollProfile profile) LoadProfile(profile);
        };
        _start.Click += async (_, _) => await StartRollAsync();
        _pause.Click += (_, _) => TogglePause();
        _cancel.Click += (_, _) => _cancellation?.Cancel();
        _analyze.Click += async (_, _) => await AnalyzeWorldAsync();
        _results.SelectionChanged += (_, _) => ShowSelectedResult();
        _results.RowActivated += (_, _) => OpenSelectedReport();
        _criteria.ItemActivated += (_, _) => EditSelectedCriterion();
        _criteria.SelectionChanged += (_, _) => UpdateCriteriaButtons();
        _criteria.ItemsChanged += (_, _) => UpdateCriteriaButtons();
        _openReport.Click += (_, _) => OpenSelectedReport();
        _copySeed.Click += (_, _) => CopySelectedSeed();
        _specialSeeds.SelectionChanged += (_, _) => { };
        FormClosing += (_, _) => _cancellation?.Cancel();
    }

    private void UpdateCriteriaButtons()
    {
        bool hasSelection = _criteria.SelectedItem is not null;
        _openReport.Enabled = _results.SelectedRow is not null;
        _copySeed.Enabled = _results.SelectedRow is not null;
    }

    // =======================================================================
    // Roll workflow
    // =======================================================================

    private async Task StartRollAsync()
    {
        try
        {
            SetRunning(true);
            _rollResults.Clear();
            RefreshResults();
            _map.Image = null;
            _details.SetEntries([]);
            _log.Clear();
            _lastReportDirectory = null;

            GenerationSettings settings = ReadSettings();
            RollProfile profile = ReadProfile();
            _cancellation = new CancellationTokenSource();
            _pauseController = new PauseController();
            _footer.Progress.Maximum = settings.MaximumAttempts;
            _footer.Progress.Value = 0;
            _footer.Progress.Indeterminate = false;

            Progress<RollProgress> progress = new(p =>
            {
                _footer.Progress.Value = Math.Min(_footer.Progress.Maximum, p.Completed);
                SetStatus(
                    $"{p.Stage} — {p.Completed}/{p.MaximumAttempts}，通过 {p.Accepted}，失败 {p.Failed}，当前 seed {p.CurrentSeed}",
                    AppIcon.Spinner,
                    Palette.Info);
            });

            AppendLog($"开始 Roll 种：预设「{profile.Name}」，最多 {settings.MaximumAttempts} 个世界。", LogLevel.Info);
            RollSessionResult session = await new SeedRollerEngine().RunAsync(
                settings, profile, _pauseController, progress, message => AppendLog(message), _cancellation.Token);

            _rollResults.AddRange(session.Winners);
            RefreshResults();
            _lastReportDirectory = session.OutputDirectory;

            if (session.Cancelled)
            {
                SetStatus($"已取消，保存了 {session.Winners.Count} 个入选结果", AppIcon.Warning, Palette.Accent);
            }
            else
            {
                SetStatus($"完成：{session.Attempted} 次尝试，保存 {session.Winners.Count} 个结果", AppIcon.Success, Palette.Success);
            }
            AppendLog($"结果目录：{session.OutputDirectory}", LogLevel.Info);
        }
        catch (Exception ex)
        {
            SetStatus("失败：" + ex.Message, AppIcon.Error, Palette.Danger);
            AppendLog(ex.ToString(), LogLevel.Error);
            MessageBox.Show(this, ex.Message, "Roll 种失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            _pauseController = null;
            _footer.Progress.Indeterminate = false;
            SetRunning(false);
        }
    }

    private async Task AnalyzeWorldAsync()
    {
        using OpenFileDialog dialog = new()
        {
            Filter = "Terraria 世界 (*.wld)|*.wld|所有文件 (*.*)|*.*",
            Title = "选择要只读分析的世界"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            SetRunning(true);
            _footer.Progress.Indeterminate = true;
            SetStatus("只读分析中…", AppIcon.Spinner, Palette.Info);
            RollProfile profile = ReadProfile();
            WorldAnalysis analysis = await Task.Run(() => new WorldAnalyzer().Analyze(dialog.FileName, profile));
            string report = Path.Combine(
                Path.GetFullPath(_outputPath.Text),
                $"analysis_{Path.GetFileNameWithoutExtension(dialog.FileName)}_{DateTime.Now:yyyyMMdd_HHmmss}");
            ReportWriter.Write(analysis, report);
            _lastReportDirectory = report;

            _rollResults.Clear();
            _rollResults.Add(new RollResult(0, analysis.Metadata.SeedText, analysis.WorldPath, TimeSpan.Zero, TimeSpan.Zero, analysis));
            RefreshResults();
            SetStatus("分析完成，原世界文件未被修改", AppIcon.Success, Palette.Success);
            AppendLog($"分析报告：{Path.Combine(report, "report.html")}", LogLevel.Info);
        }
        catch (Exception ex)
        {
            SetStatus("分析失败：" + ex.Message, AppIcon.Error, Palette.Danger);
            AppendLog(ex.ToString(), LogLevel.Error);
            MessageBox.Show(this, ex.Message, "分析失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _footer.Progress.Indeterminate = false;
            SetRunning(false);
        }
    }

    private GenerationSettings ReadSettings()
    {
        int start = decimal.ToInt32(_startSeed.Value);
        int end = decimal.ToInt32(_endSeed.Value);
        if (end < start) throw new ArgumentException("结束种子不能小于起始种子。");

        SpecialSeedFlags special = SpecialSeedFlags.None;
        foreach (object value in _specialSeeds.CheckedValues)
        {
            if (value is SpecialSeedFlags flag) special |= flag;
        }

        return new GenerationSettings
        {
            TerrariaServerPath = Path.GetFullPath(_serverPath.Text.Trim()),
            CandidateDirectory = Path.GetFullPath(_outputPath.Text.Trim()),
            Size = ((Choice<WorldSize>)_size.SelectedItem!).Value,
            Difficulty = ((Choice<WorldDifficulty>)_difficulty.SelectedItem!).Value,
            Evil = ((Choice<WorldEvil>)_evil.SelectedItem!).Value,
            SpecialSeeds = special,
            Seeds = new SeedRange(start, end, _randomOrder.Checked),
            MaximumAttempts = decimal.ToInt32(_attempts.Value),
            WinnersToKeep = decimal.ToInt32(_winners.Value),
            TopResultsToTrack = Math.Max(decimal.ToInt32(_winners.Value), 50),
            Parallelism = decimal.ToInt32(_parallel.Value),
            ServerPriority = ((Choice<ServerProcessPriority>)_priority.SelectedItem!).Value,
            PerWorldTimeout = TimeSpan.FromMinutes(decimal.ToDouble(_timeout.Value)),
            KeepRejectedWorlds = _keepRejected.Checked
        };
    }

    private RollProfile ReadProfile()
    {
        RollProfile selected = (RollProfile)_preset.SelectedItem!;
        return new RollProfile
        {
            Name = selected.Name,
            Description = selected.Description,
            Criteria = _criteria.Items.ToList()
        };
    }

    private void LoadProfile(RollProfile profile)
    {
        _criteria.SetItems(profile.Criteria, profile.Criteria.Count > 0 ? 0 : -1);
        _presetRow.Hint = profile.Description;
        UpdateCriteriaButtons();
    }

    private void AddCriterion()
    {
        using CriterionDialog dialog = new(null, (string)((RollProfile)_preset.SelectedItem!).Name);
        if (dialog.ShowDialog(this) == DialogResult.OK) _criteria.Add(dialog.Result);
    }

    private void EditSelectedCriterion()
    {
        if (_criteria.SelectedItem is not { } current) return;
        using CriterionDialog dialog = new(current, (string)((RollProfile)_preset.SelectedItem!).Name);
        if (dialog.ShowDialog(this) == DialogResult.OK) _criteria.ReplaceSelected(dialog.Result);
    }

    private void TogglePause()
    {
        if (_pauseController is null) return;
        if (_pauseController.IsPaused)
        {
            _pauseController.Resume();
            _pause.Text = "暂停";
            SetStatus("继续运行", AppIcon.Play, Palette.Info);
        }
        else
        {
            _pauseController.Pause();
            _pause.Text = "继续";
            SetStatus("已暂停（当前世界生成完成后生效）", AppIcon.Pause, Palette.Accent);
        }
    }

    private void SetRunning(bool running)
    {
        _start.Enabled = !running;
        _analyze.Enabled = !running;
        _pause.Enabled = running;
        _cancel.Enabled = running;
        if (!running) _pause.Text = "暂停";
    }

    private void SetStatus(string text, AppIcon icon, Color color)
    {
        _footer.StatusText = text;
        _footer.StatusIcon = icon;
        _footer.StatusColor = color;
    }

    // =======================================================================
    // Results
    // =======================================================================

    private void RefreshResults()
    {
        List<TableView.Row> rows = [];
        for (int i = 0; i < _rollResults.Count; i++)
        {
            RollResult result = _rollResults[i];
            rows.Add(new TableView.Row
            {
                Cells =
                [
                    i + 1,
                    result.CopiedSeed,
                    result.Analysis.Evaluation?.Score ?? 0d,
                    Get(result, MetricKeys.EvilLargestWidthTiles),
                    Get(result, MetricKeys.EvilPreHardmodeClosureLargestWidth),
                    Get(result, MetricKeys.ChestCount),
                    result.GenerationDuration.TotalSeconds,
                    result.AnalysisDuration.TotalSeconds
                ],
                Tag = result,
                Accent = i == 0 ? Palette.Accent : null
            });
        }

        _results.SetRows(rows);
        _resultsEmpty.Visible = rows.Count == 0;
        _results.Visible = rows.Count > 0;
        UpdateCriteriaButtons();
    }

    private void ShowSelectedResult()
    {
        if (_results.SelectedRow?.Tag is not RollResult result)
        {
            _map.Image = null;
            _details.SetEntries([]);
            UpdateCriteriaButtons();
            return;
        }

        _map.Image = MapPalette.CreateOverviewBitmap(result.Analysis);
        _map.FitToWindow();
        _details.SetEntries(BuildDetailEntries(result));
        UpdateCriteriaButtons();
    }

    private static IEnumerable<DetailList.Entry> BuildDetailEntries(RollResult result)
    {
        WorldAnalysis analysis = result.Analysis;
        WorldMetadata metadata = analysis.Metadata;

        yield return new DetailList.Section("世界");
        yield return new DetailList.Field("标题", metadata.Title, Icon: AppIcon.Earth);
        yield return new DetailList.Field("复制种子", result.CopiedSeed, Palette.Accent, AppIcon.Copy);
        yield return new DetailList.Field("尺寸", $"{metadata.Width} × {metadata.Height}", Icon: AppIcon.Fit);
        yield return new DetailList.Field("出生点", $"{metadata.Spawn.X}, {metadata.Spawn.Y}", Icon: AppIcon.Target);

        yield return new DetailList.Spacer(4);
        yield return new DetailList.Section("评价");
        yield return new DetailList.Field("得分", (analysis.Evaluation?.Score ?? 0).ToString("0.##"), Palette.Accent, AppIcon.Chart);
        yield return new DetailList.Field("硬条件", analysis.Evaluation is null ? "未评估" : analysis.Evaluation.PassedHardCriteria ? "全部通过" : "未通过",
            analysis.Evaluation?.PassedHardCriteria == false ? Palette.Danger : Palette.Success, AppIcon.Shield);
        yield return new DetailList.Field("生成耗时", $"{result.GenerationDuration.TotalSeconds:0.00} 秒", Icon: AppIcon.Clock);
        yield return new DetailList.Field("分析耗时", $"{result.AnalysisDuration.TotalSeconds:0.00} 秒", Icon: AppIcon.Spinner);

        yield return new DetailList.Spacer(4);
        yield return new DetailList.Section("邪恶控制");
        yield return new DetailList.Field("实际最大宽度", $"{Get(result, MetricKeys.EvilLargestWidthTiles):0} 格", Icon: AppIcon.Warning);
        yield return new DetailList.Field("肉前蔓延宽度", $"{Get(result, MetricKeys.EvilPreHardmodeClosureLargestWidth):0} 格", Icon: AppIcon.Warning);
        yield return new DetailList.Field("邪恶—丛林间距", $"{Get(result, MetricKeys.EvilJungleGapTiles):0} 格", Icon: AppIcon.Mountain);
        yield return new DetailList.Field("邪恶区域数", $"{Get(result, MetricKeys.EvilRegionCount):0}", Icon: AppIcon.Layers);

        yield return new DetailList.Spacer(4);
        yield return new DetailList.Section("资源");
        yield return new DetailList.Field("宝箱", $"{analysis.Chests.Count}", Icon: AppIcon.Gem);
        yield return new DetailList.Field("生命水晶", $"{Get(result, MetricKeys.LifeCrystalCount):0}", Icon: AppIcon.Gem);
        yield return new DetailList.Field("微光液体格", $"{Get(result, MetricKeys.ShimmerLiquidTiles):0}", Icon: AppIcon.Sparkles);
        yield return new DetailList.Field("微光路线成本", $"{Get(result, MetricKeys.ShimmerAccessCost):0}", Icon: AppIcon.Chart);
        yield return new DetailList.Field("重要物品", $"{analysis.ImportantItems.Count} 项", Icon: AppIcon.Sparkles);

        if (analysis.Regions.Count > 0)
        {
            yield return new DetailList.Spacer(4);
            yield return new DetailList.Section("结构");
            foreach (IGrouping<RegionKind, WorldRegion> group in analysis.Regions.GroupBy(r => r.Kind))
            {
                yield return new DetailList.Field(
                    DescribeRegion(group.Key),
                    $"{group.Count()} 处 · 最大 {group.Max(r => r.Width)} 格宽");
            }
        }

        yield return new DetailList.Spacer(6);
        yield return new DetailList.Note("地图每像素代表 8×8 格，只显示分析目标，不会读取或解锁游戏内地图。白点为出生点。");
    }

    private static string DescribeRegion(RegionKind kind) => kind switch
    {
        RegionKind.Corruption => "腐化",
        RegionKind.Crimson => "猩红",
        RegionKind.Jungle => "丛林",
        RegionKind.Snow => "雪原",
        RegionKind.Desert => "沙漠",
        RegionKind.GlowingMushroom => "蘑菇地",
        RegionKind.Dungeon => "地牢",
        RegionKind.Temple => "神庙",
        RegionKind.Hive => "蜂巢",
        RegionKind.Marble => "大理石洞",
        RegionKind.Granite => "花岗岩洞",
        RegionKind.Spider => "蜘蛛洞",
        RegionKind.FloatingIsland => "浮空岛",
        RegionKind.LivingTree => "生命树",
        RegionKind.Shimmer => "微光",
        _ => kind.ToString()
    };

    private void OpenSelectedReport()
    {
        if (_results.SelectedRow?.Tag is not RollResult result) return;
        string directory = Path.GetDirectoryName(result.WorldPath) ?? _lastReportDirectory ?? string.Empty;
        string report = Path.Combine(directory, "report.html");
        if (File.Exists(report))
        {
            OpenPath(report);
            return;
        }
        if (Directory.Exists(directory))
        {
            OpenDirectory(directory);
            return;
        }
        SetStatus("找不到该世界的报告文件", AppIcon.Warning, Palette.Accent);
    }

    private void CopySelectedSeed()
    {
        if (_results.SelectedRow?.Tag is not RollResult result) return;
        Clipboard.SetText(result.CopiedSeed);
        SetStatus($"已复制种子 {result.CopiedSeed}", AppIcon.Success, Palette.Success);
    }

    private static void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "无法打开", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void OpenDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            MessageBox.Show($"目录不存在：{path}", "无法打开", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        OpenPath(path);
    }

    // =======================================================================
    // Log
    // =======================================================================

    private enum LogLevel
    {
        Info,
        Warning,
        Error
    }

    private void AppendLog(string message, LogLevel level = LogLevel.Info)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(message, level));
            return;
        }

        if (_log.TextLength > 400_000)
        {
            _log.Select(0, 120_000);
            _log.SelectedText = string.Empty;
        }

        Color color = level switch
        {
            LogLevel.Error => Palette.Danger,
            LogLevel.Warning => Palette.Accent,
            _ => Palette.TextSecondary
        };

        _log.SelectionStart = _log.TextLength;
        _log.SelectionLength = 0;
        _log.SelectionColor = Palette.TextMuted;
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] ");
        _log.SelectionColor = color;
        _log.AppendText(message + Environment.NewLine);
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }

    // =======================================================================
    // Browse helpers
    // =======================================================================

    private void BrowseServer()
    {
        using OpenFileDialog dialog = new()
        {
            Filter = "TerrariaServer.exe|TerrariaServer.exe|可执行文件 (*.exe)|*.exe",
            FileName = _serverPath.Text
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) _serverPath.Text = dialog.FileName;
    }

    private void BrowseOutput()
    {
        using FolderBrowserDialog dialog = new() { SelectedPath = _outputPath.Text, ShowNewFolderButton = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) _outputPath.Text = dialog.SelectedPath;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // WinForms applies its own DPI scale to the bounds when the handle is
        // created, so the authoritative sizing pass happens here, once
        // DeviceDpi reports the real monitor DPI.
        ApplyDpiSizing();
        DarkMode.ApplyTitleBar(this);
        DarkMode.Apply(_log);
        _log.BackColor = Palette.SurfaceSunken;
        _log.ForeColor = Palette.TextSecondary;
    }

    private static double Get(RollResult result, string key) => result.Analysis.Metrics.GetValueOrDefault(key);

    private static string FormatNumber(object? value, string format)
    {
        if (value is null) return string.Empty;
        double number = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
        return number.ToString(format, System.Globalization.CultureInfo.CurrentCulture);
    }

    private static string FindTerrariaServer()
    {
        List<string> candidates = [];
        try
        {
            object? steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null);
            if (steam is string steamPath && steamPath.Length > 0)
            {
                candidates.Add(Path.Combine(steamPath, "steamapps", "common", "Terraria", "TerrariaServer.exe"));
            }
        }
        catch (Exception)
        {
            // Registry access is best effort only.
        }

        foreach (string drive in new[] { "C:", "D:", "E:", "F:" })
        {
            candidates.Add($@"{drive}\Program Files (x86)\Steam\steamapps\common\Terraria\TerrariaServer.exe");
            candidates.Add($@"{drive}\Steam\steamapps\common\Terraria\TerrariaServer.exe");
            candidates.Add($@"{drive}\SteamLibrary\steamapps\common\Terraria\TerrariaServer.exe");
            candidates.Add($@"{drive}\Games\Steam\steamapps\common\Terraria\TerrariaServer.exe");
        }

        foreach (string candidate in candidates)
        {
            try
            {
                if (File.Exists(candidate)) return candidate;
            }
            catch (Exception)
            {
                // Ignore malformed paths.
            }
        }
        return string.Empty;
    }
}
