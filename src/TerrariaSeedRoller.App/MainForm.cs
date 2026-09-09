using System.Diagnostics;
using System.Drawing.Imaging;
using TerrariaSeedRoller.Core;

namespace TerrariaSeedRoller.App;

public sealed class MainForm : Form
{
    private readonly TextBox _serverPath = new() { Dock = DockStyle.Fill };
    private readonly TextBox _outputPath = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _size = DropDown();
    private readonly ComboBox _difficulty = DropDown();
    private readonly ComboBox _evil = DropDown();
    private readonly ComboBox _preset = DropDown();
    private readonly CheckedListBox _specialSeeds = new() { Height = 74, CheckOnClick = true };
    private readonly NumericUpDown _startSeed = Number(int.MinValue, int.MaxValue, 0);
    private readonly NumericUpDown _endSeed = Number(int.MinValue, int.MaxValue, int.MaxValue);
    private readonly NumericUpDown _attempts = Number(1, 10_000_000, 20);
    private readonly NumericUpDown _winners = Number(1, 10_000, 3);
    private readonly NumericUpDown _parallel = Number(1, 8, 1);
    private readonly NumericUpDown _timeout = Number(1, 120, 10);
    private readonly CheckBox _random = new() { Text = "随机无重复顺序", Checked = true, AutoSize = true };
    private readonly CheckBox _keepRejected = new() { Text = "保留未通过世界", AutoSize = true };
    private readonly DataGridView _criteria = new();
    private readonly DataGridView _results = new();
    private readonly PictureBox _map = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(7, 16, 25), SizeMode = PictureBoxSizeMode.Zoom };
    private readonly RichTextBox _details = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(20, 29, 39), ForeColor = Color.Gainsboro, BorderStyle = BorderStyle.None };
    private readonly RichTextBox _log = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(12, 18, 25), ForeColor = Color.LightGray, Font = new Font("Consolas", 9) };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill };
    private readonly Label _status = new() { Text = "就绪", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _start = new() { Text = "开始 Roll 种", AutoSize = true };
    private readonly Button _pause = new() { Text = "暂停", AutoSize = true, Enabled = false };
    private readonly Button _cancel = new() { Text = "取消", AutoSize = true, Enabled = false };
    private readonly Button _analyze = new() { Text = "只读分析现有世界", AutoSize = true };
    private readonly BindingSource _resultSource = new();
    private readonly List<RollResult> _rollResults = [];
    private CancellationTokenSource? _cancellation;
    private PauseController? _pauseController;

    public MainForm()
    {
        Text = "Terraria Seed Roller — 原版真实世界 Roll 种机";
        MinimumSize = new Size(1100, 720);
        Size = new Size(1450, 900);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9);
        BackColor = Color.FromArgb(19, 28, 38);
        ForeColor = Color.Gainsboro;

        _serverPath.Text = FindTerrariaServer();
        _outputPath.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "My Games", "Terraria", "SeedRollerOutput");
        _size.DataSource = new[] { new Choice<WorldSize>("小世界", WorldSize.Small), new("中世界", WorldSize.Medium), new("大世界", WorldSize.Large) };
        _difficulty.DataSource = new[] { new Choice<WorldDifficulty>("经典", WorldDifficulty.Classic), new("专家", WorldDifficulty.Expert), new("大师", WorldDifficulty.Master), new("旅途", WorldDifficulty.Journey) };
        _evil.DataSource = new[] { new Choice<WorldEvil>("随机", WorldEvil.Random), new("腐化", WorldEvil.Corruption), new("猩红", WorldEvil.Crimson) };
        foreach (SpecialSeedFlags value in Enum.GetValues<SpecialSeedFlags>().Where(v => v != SpecialSeedFlags.None))
            _specialSeeds.Items.Add(value);
        _preset.DataSource = BuiltInProfiles.All.ToList();
        _preset.DisplayMember = nameof(RollProfile.Name);

        Controls.Add(BuildLayout());
        ConfigureCriteriaGrid();
        ConfigureResultsGrid();
        LoadProfile(BuiltInProfiles.All[0]);
        WireEvents();
    }

    private Control BuildLayout()
    {
        SplitContainer split = new() { Dock = DockStyle.Fill, SplitterDistance = 450, BackColor = BackColor };
        split.Panel1.Padding = new Padding(12);
        split.Panel2.Padding = new Padding(0, 12, 12, 12);
        split.Panel1.Controls.Add(BuildSettingsPanel());

        TabControl tabs = new() { Dock = DockStyle.Fill };
        TabPage criteriaPage = new("筛选条件") { BackColor = BackColor };
        TableLayoutPanel criteriaLayout = new() { Dock = DockStyle.Fill, RowCount = 2 };
        criteriaLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        criteriaLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        FlowLayoutPanel criterionButtons = new() { Dock = DockStyle.Fill, AutoSize = true };
        Button add = new() { Text = "添加指标", AutoSize = true };
        Button remove = new() { Text = "删除选中", AutoSize = true };
        add.Click += (_, _) => AddCriterion();
        remove.Click += (_, _) => { foreach (DataGridViewRow row in _criteria.SelectedRows) if (!row.IsNewRow) _criteria.Rows.Remove(row); };
        criterionButtons.Controls.AddRange([add, remove, new Label { Text = "硬条件负责淘汰；加权条件负责排序。", AutoSize = true, Padding = new Padding(8, 7, 0, 0) }]);
        criteriaLayout.Controls.Add(criterionButtons, 0, 0);
        criteriaLayout.Controls.Add(_criteria, 0, 1);
        criteriaPage.Controls.Add(criteriaLayout);

        TabPage resultsPage = new("结果与地图") { BackColor = BackColor };
        SplitContainer resultSplit = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 250 };
        resultSplit.Panel1.Controls.Add(_results);
        SplitContainer mapSplit = new() { Dock = DockStyle.Fill, SplitterDistance = 700 };
        mapSplit.Panel1.Controls.Add(_map);
        mapSplit.Panel2.Controls.Add(_details);
        resultSplit.Panel2.Controls.Add(mapSplit);
        resultsPage.Controls.Add(resultSplit);

        TabPage logPage = new("运行日志") { BackColor = BackColor };
        logPage.Controls.Add(_log);
        tabs.TabPages.AddRange([criteriaPage, resultsPage, logPage]);
        split.Panel2.Controls.Add(tabs);
        return split;
    }

    private Control BuildSettingsPanel()
    {
        TableLayoutPanel table = new()
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = 3,
            Padding = new Padding(0),
            GrowStyle = TableLayoutPanelGrowStyle.AddRows
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Label title = new() { Text = "真实世界生成", Font = new Font(Font, FontStyle.Bold), AutoSize = true, Padding = new Padding(0, 0, 0, 8) };
        table.Controls.Add(title, 0, 0); table.SetColumnSpan(title, 3);
        AddPathRow(table, "服务端", _serverPath, "浏览…", BrowseServer);
        AddPathRow(table, "输出目录", _outputPath, "浏览…", BrowseOutput);
        AddRow(table, "世界大小", _size);
        AddRow(table, "难度", _difficulty);
        AddRow(table, "邪恶", _evil);
        AddRow(table, "特殊种子", _specialSeeds);
        AddRow(table, "起始种子", _startSeed);
        AddRow(table, "结束种子", _endSeed);
        AddRow(table, "顺序", _random);
        AddRow(table, "最多尝试", _attempts);
        AddRow(table, "保留前 N", _winners);
        AddRow(table, "并发数", _parallel);
        AddRow(table, "单世界超时", Inline(_timeout, new Label { Text = "分钟", AutoSize = true, Padding = new Padding(6, 6, 0, 0) }));
        AddRow(table, "临时文件", _keepRejected);
        AddRow(table, "筛选预设", _preset);

        Label note = new()
        {
            Text = "使用已安装的原版 TerrariaServer 后台真实生成。不会打开游戏窗口、不会读取或写入玩家存档目录；默认只保留最终入选世界。并发 1 最稳妥。",
            AutoSize = true,
            MaximumSize = new Size(410, 0),
            ForeColor = Color.LightSteelBlue,
            Padding = new Padding(0, 10, 0, 10)
        };
        int noteRow = table.RowCount++;
        table.Controls.Add(note, 0, noteRow); table.SetColumnSpan(note, 3);
        FlowLayoutPanel buttons = new() { AutoSize = true, Dock = DockStyle.Fill };
        buttons.Controls.AddRange([_start, _pause, _cancel, _analyze]);
        int buttonRow = table.RowCount++;
        table.Controls.Add(buttons, 0, buttonRow); table.SetColumnSpan(buttons, 3);
        int progressRow = table.RowCount++;
        table.Controls.Add(_progress, 0, progressRow); table.SetColumnSpan(_progress, 3);
        int statusRow = table.RowCount++;
        table.Controls.Add(_status, 0, statusRow); table.SetColumnSpan(_status, 3);
        return table;
    }

    private void ConfigureCriteriaGrid()
    {
        _criteria.Dock = DockStyle.Fill;
        _criteria.AllowUserToAddRows = false;
        _criteria.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _criteria.BackgroundColor = Color.FromArgb(14, 23, 32);
        _criteria.RowHeadersVisible = false;
        _criteria.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _criteria.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "启用", FillWeight = 35 });
        _criteria.Columns.Add(new DataGridViewTextBoxColumn { Name = "Label", HeaderText = "条件", FillWeight = 130 });
        _criteria.Columns.Add(new DataGridViewTextBoxColumn { Name = "Metric", HeaderText = "指标键", FillWeight = 150 });
        _criteria.Columns.Add(new DataGridViewComboBoxColumn { Name = "Kind", HeaderText = "类型", DataSource = Enum.GetValues<CriterionKind>(), FillWeight = 60 });
        _criteria.Columns.Add(new DataGridViewComboBoxColumn { Name = "Comparison", HeaderText = "比较", DataSource = Enum.GetValues<MetricComparison>(), FillWeight = 70 });
        _criteria.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "值", FillWeight = 55 });
        _criteria.Columns.Add(new DataGridViewTextBoxColumn { Name = "Second", HeaderText = "第二值", FillWeight = 55 });
        _criteria.Columns.Add(new DataGridViewTextBoxColumn { Name = "Weight", HeaderText = "权重", FillWeight = 45 });
    }

    private void ConfigureResultsGrid()
    {
        _results.Dock = DockStyle.Fill;
        _results.ReadOnly = true;
        _results.AllowUserToAddRows = false;
        _results.AutoGenerateColumns = false;
        _results.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _results.BackgroundColor = Color.FromArgb(14, 23, 32);
        _results.RowHeadersVisible = false;
        _results.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _results.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "排名", DataPropertyName = "Rank", FillWeight = 35 });
        _results.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "复制种子", DataPropertyName = "Seed", FillWeight = 130 });
        _results.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "得分", DataPropertyName = "Score", FillWeight = 50 });
        _results.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "邪恶宽度", DataPropertyName = "EvilWidth", FillWeight = 60 });
        _results.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "肉前蔓延宽度", DataPropertyName = "SpreadWidth", FillWeight = 70 });
        _results.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "宝箱", DataPropertyName = "Chests", FillWeight = 45 });
        _results.DataSource = _resultSource;
    }

    private void WireEvents()
    {
        _preset.SelectedIndexChanged += (_, _) => { if (_preset.SelectedItem is RollProfile p) LoadProfile(p); };
        _start.Click += async (_, _) => await StartRollAsync();
        _pause.Click += (_, _) => TogglePause();
        _cancel.Click += (_, _) => _cancellation?.Cancel();
        _analyze.Click += async (_, _) => await AnalyzeWorldAsync();
        _results.SelectionChanged += (_, _) => ShowSelectedResult();
        FormClosing += (_, _) => _cancellation?.Cancel();
    }

    private async Task StartRollAsync()
    {
        try
        {
            SetRunning(true);
            _rollResults.Clear(); RefreshResultGrid();
            _map.Image?.Dispose(); _map.Image = null; _details.Clear(); _log.Clear();
            GenerationSettings settings = ReadSettings();
            RollProfile profile = ReadProfile();
            _cancellation = new CancellationTokenSource();
            _pauseController = new PauseController();
            _progress.Maximum = settings.MaximumAttempts;
            Progress<RollProgress> progress = new(p =>
            {
                _progress.Value = Math.Min(_progress.Maximum, p.Completed);
                _status.Text = $"{p.Stage} — {p.Completed}/{p.MaximumAttempts}，已通过 {p.Accepted}，失败 {p.Failed}，seed {p.CurrentSeed}";
            });
            AppendLog($"开始：{profile.Name}，最多 {settings.MaximumAttempts} 个世界。");
            RollSessionResult session = await new SeedRollerEngine().RunAsync(settings, profile,
                _pauseController, progress, AppendLog, _cancellation.Token);
            _rollResults.AddRange(session.Winners);
            RefreshResultGrid();
            _status.Text = session.Cancelled
                ? $"已取消；保存了 {session.Winners.Count} 个入选结果"
                : $"完成；{session.Attempted} 次尝试，保存 {session.Winners.Count} 个结果";
            AppendLog($"结果目录：{session.OutputDirectory}");
        }
        catch (Exception ex)
        {
            _status.Text = "失败：" + ex.Message;
            AppendLog(ex.ToString());
            MessageBox.Show(this, ex.Message, "Roll 种失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _cancellation?.Dispose(); _cancellation = null; _pauseController = null;
            SetRunning(false);
        }
    }

    private async Task AnalyzeWorldAsync()
    {
        using OpenFileDialog dialog = new() { Filter = "Terraria 世界 (*.wld)|*.wld", Title = "选择要只读分析的世界" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            SetRunning(true);
            _status.Text = "只读分析中…";
            RollProfile profile = ReadProfile();
            WorldAnalysis analysis = await Task.Run(() => new WorldAnalyzer().Analyze(dialog.FileName, profile));
            string report = Path.Combine(Path.GetFullPath(_outputPath.Text),
                $"analysis_{Path.GetFileNameWithoutExtension(dialog.FileName)}_{DateTime.Now:yyyyMMdd_HHmmss}");
            ReportWriter.Write(analysis, report);
            RollResult displayed = new(0, analysis.Metadata.SeedText, analysis.WorldPath,
                TimeSpan.Zero, TimeSpan.Zero, analysis);
            _rollResults.Clear(); _rollResults.Add(displayed); RefreshResultGrid();
            _status.Text = "分析完成；原世界未被修改";
            AppendLog($"分析报告：{Path.Combine(report, "report.html")}");
        }
        catch (Exception ex)
        {
            _status.Text = "分析失败：" + ex.Message;
            MessageBox.Show(this, ex.Message, "分析失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetRunning(false); }
    }

    private GenerationSettings ReadSettings()
    {
        int start = decimal.ToInt32(_startSeed.Value), end = decimal.ToInt32(_endSeed.Value);
        if (end < start) throw new ArgumentException("结束种子不能小于起始种子。");
        SpecialSeedFlags special = SpecialSeedFlags.None;
        foreach (SpecialSeedFlags item in _specialSeeds.CheckedItems) special |= item;
        return new GenerationSettings
        {
            TerrariaServerPath = Path.GetFullPath(_serverPath.Text.Trim()),
            CandidateDirectory = Path.GetFullPath(_outputPath.Text.Trim()),
            Size = ((Choice<WorldSize>)_size.SelectedItem!).Value,
            Difficulty = ((Choice<WorldDifficulty>)_difficulty.SelectedItem!).Value,
            Evil = ((Choice<WorldEvil>)_evil.SelectedItem!).Value,
            SpecialSeeds = special,
            Seeds = new SeedRange(start, end, _random.Checked),
            MaximumAttempts = decimal.ToInt32(_attempts.Value),
            WinnersToKeep = decimal.ToInt32(_winners.Value),
            TopResultsToTrack = Math.Max(decimal.ToInt32(_winners.Value), 50),
            Parallelism = decimal.ToInt32(_parallel.Value),
            PerWorldTimeout = TimeSpan.FromMinutes(decimal.ToDouble(_timeout.Value)),
            KeepRejectedWorlds = _keepRejected.Checked
        };
    }

    private RollProfile ReadProfile()
    {
        List<CriterionDefinition> criteria = [];
        foreach (DataGridViewRow row in _criteria.Rows)
        {
            string metric = Convert.ToString(row.Cells["Metric"].Value)?.Trim() ?? string.Empty;
            if (metric.Length == 0) continue;
            criteria.Add(new CriterionDefinition
            {
                Enabled = Convert.ToBoolean(row.Cells["Enabled"].Value ?? true),
                Label = Convert.ToString(row.Cells["Label"].Value),
                MetricKey = metric,
                Kind = ParseEnum(row.Cells["Kind"].Value, CriterionKind.Hard),
                Comparison = ParseEnum(row.Cells["Comparison"].Value, MetricComparison.AtMost),
                Value = ParseDouble(row.Cells["Value"].Value),
                SecondValue = ParseDouble(row.Cells["Second"].Value),
                Weight = ParseDouble(row.Cells["Weight"].Value, 1)
            });
        }
        RollProfile selected = (RollProfile)_preset.SelectedItem!;
        return new RollProfile { Name = selected.Name, Description = selected.Description, Criteria = criteria };
    }

    private void LoadProfile(RollProfile profile)
    {
        _criteria.Rows.Clear();
        foreach (CriterionDefinition c in profile.Criteria)
            _criteria.Rows.Add(c.Enabled, c.Label, c.MetricKey, c.Kind, c.Comparison, c.Value, c.SecondValue, c.Weight);
    }

    private void AddCriterion()
    {
        using Form picker = new() { Text = "添加指标", Width = 650, Height = 150, StartPosition = FormStartPosition.CenterParent };
        ComboBox combo = DropDown(); combo.Dock = DockStyle.Top;
        combo.DataSource = MetricCatalog.All.ToList(); combo.DisplayMember = nameof(MetricDefinition.ChineseName);
        Button ok = new() { Text = "添加", Dock = DockStyle.Bottom, DialogResult = DialogResult.OK };
        picker.Controls.Add(combo); picker.Controls.Add(ok); picker.AcceptButton = ok;
        if (picker.ShowDialog(this) == DialogResult.OK && combo.SelectedItem is MetricDefinition metric)
        {
            MetricComparison comparison = metric.Preference == MetricPreference.LargerIsBetter
                ? MetricComparison.AtLeast : MetricComparison.AtMost;
            _criteria.Rows.Add(true, metric.ChineseName, metric.Key, CriterionKind.Weighted, comparison, 1d, 0d, 1d);
        }
    }

    private void RefreshResultGrid()
    {
        _resultSource.DataSource = _rollResults.Select((r, i) => new ResultRow
        {
            Rank = i + 1,
            Seed = r.CopiedSeed,
            Score = r.Analysis.Evaluation?.Score ?? 0,
            EvilWidth = Get(r, MetricKeys.EvilLargestWidthTiles),
            SpreadWidth = Get(r, MetricKeys.EvilPreHardmodeClosureLargestWidth),
            Chests = Get(r, MetricKeys.ChestCount),
            Result = r
        }).ToList();
        _resultSource.ResetBindings(false);
        if (_results.Rows.Count > 0) _results.Rows[0].Selected = true;
    }

    private void ShowSelectedResult()
    {
        if (_results.CurrentRow?.DataBoundItem is not ResultRow row) return;
        RollResult result = row.Result;
        _map.Image?.Dispose(); _map.Image = CreateOverviewBitmap(result.Analysis);
        _details.Text = $"世界：{result.Analysis.Metadata.Title}\n复制种子：{result.CopiedSeed}\n" +
            $"尺寸：{result.Analysis.Metadata.Width} × {result.Analysis.Metadata.Height}\n" +
            $"得分：{result.Analysis.Evaluation?.Score:0.##}\n" +
            $"邪恶实际最大宽度：{Get(result, MetricKeys.EvilLargestWidthTiles):0} 格\n" +
            $"肉前自由蔓延最大宽度：{Get(result, MetricKeys.EvilPreHardmodeClosureLargestWidth):0} 格\n" +
            $"邪恶—丛林间距：{Get(result, MetricKeys.EvilJungleGapTiles):0} 格\n" +
            $"宝箱：{result.Analysis.Chests.Count}\n生命水晶：{Get(result, MetricKeys.LifeCrystalCount):0}\n" +
            $"微光液体格：{Get(result, MetricKeys.ShimmerLiquidTiles):0}\n\n" +
            "白点为出生点。地图每像素代表 8×8 格，只显示分析目标，不会解锁游戏地图。";
    }

    private static Bitmap CreateOverviewBitmap(WorldAnalysis analysis)
    {
        Color[] colors = [Color.Transparent, Color.MediumPurple, Color.IndianRed, Color.SeaGreen,
            Color.LightBlue, Color.Goldenrod, Color.RoyalBlue, Color.DarkOrange, Color.Peru,
            Color.DarkGray, Color.Gainsboro, Color.SlateGray, Color.MediumAquamarine,
            Color.AliceBlue, Color.SaddleBrown, Color.HotPink, Color.OrangeRed, Color.Tan];
        Bitmap bitmap = new(analysis.OverviewWidth, analysis.OverviewHeight, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.FromArgb(7, 16, 25));
        for (int y = 0; y < analysis.OverviewHeight; y++)
        {
            int x = 0;
            while (x < analysis.OverviewWidth)
            {
                byte color = analysis.Overview[x * analysis.OverviewHeight + y];
                int start = x++;
                while (x < analysis.OverviewWidth && analysis.Overview[x * analysis.OverviewHeight + y] == color) x++;
                if (color == 0 || color >= colors.Length) continue;
                using SolidBrush brush = new(colors[color]);
                graphics.FillRectangle(brush, start, y, x - start, 1);
            }
        }
        float sx = analysis.Metadata.Spawn.X / 8f, sy = analysis.Metadata.Spawn.Y / 8f;
        graphics.FillEllipse(Brushes.White, sx - 2, sy - 2, 5, 5);
        graphics.DrawEllipse(Pens.Black, sx - 2, sy - 2, 5, 5);
        return bitmap;
    }

    private void TogglePause()
    {
        if (_pauseController is null) return;
        if (_pauseController.IsPaused) { _pauseController.Resume(); _pause.Text = "暂停"; _status.Text = "继续运行"; }
        else { _pauseController.Pause(); _pause.Text = "继续"; _status.Text = "已暂停（正在生成的世界完成后生效）"; }
    }

    private void SetRunning(bool running)
    {
        _start.Enabled = !running; _analyze.Enabled = !running; _pause.Enabled = running; _cancel.Enabled = running;
        if (!running) _pause.Text = "暂停";
    }

    private void AppendLog(string message)
    {
        if (InvokeRequired) { BeginInvoke(() => AppendLog(message)); return; }
        if (_log.TextLength > 400_000) _log.Select(0, 100_000);
        if (_log.SelectionLength > 0) _log.SelectedText = string.Empty;
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        _log.SelectionStart = _log.TextLength; _log.ScrollToCaret();
    }

    private void BrowseServer()
    {
        using OpenFileDialog dialog = new() { Filter = "TerrariaServer.exe|TerrariaServer.exe", FileName = _serverPath.Text };
        if (dialog.ShowDialog(this) == DialogResult.OK) _serverPath.Text = dialog.FileName;
    }

    private void BrowseOutput()
    {
        using FolderBrowserDialog dialog = new() { SelectedPath = _outputPath.Text, ShowNewFolderButton = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) _outputPath.Text = dialog.SelectedPath;
    }

    private static void AddPathRow(TableLayoutPanel table, string label, Control field, string buttonText, Action browse)
    {
        int row = table.RowCount++;
        table.Controls.Add(LabelFor(label), 0, row); table.Controls.Add(field, 1, row);
        Button button = new() { Text = buttonText, AutoSize = true }; button.Click += (_, _) => browse();
        table.Controls.Add(button, 2, row);
    }

    private static void AddRow(TableLayoutPanel table, string label, Control field)
    {
        int row = table.RowCount++;
        table.Controls.Add(LabelFor(label), 0, row); table.Controls.Add(field, 1, row); table.SetColumnSpan(field, 2);
    }

    private static Label LabelFor(string text) => new() { Text = text, AutoSize = true, Padding = new Padding(0, 6, 4, 0) };
    private static FlowLayoutPanel Inline(params Control[] controls) { FlowLayoutPanel p = new() { AutoSize = true, Dock = DockStyle.Fill }; p.Controls.AddRange(controls); return p; }
    private static ComboBox DropDown() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private static NumericUpDown Number(decimal min, decimal max, decimal value) => new() { Minimum = min, Maximum = max, Value = value, Dock = DockStyle.Fill, ThousandsSeparator = true };
    private static T ParseEnum<T>(object? value, T fallback) where T : struct, Enum => Enum.TryParse(Convert.ToString(value), out T result) ? result : fallback;
    private static double ParseDouble(object? value, double fallback = 0) => double.TryParse(Convert.ToString(value), out double result) ? result : fallback;
    private static double Get(RollResult result, string key) => result.Analysis.Metrics.GetValueOrDefault(key);
    private static string FindTerrariaServer()
    {
        string known = @"D:\Program Files (x86)\Steam\steamapps\common\Terraria\TerrariaServer.exe";
        return File.Exists(known) ? known : string.Empty;
    }

    private sealed record Choice<T>(string Name, T Value) { public override string ToString() => Name; }
    private sealed class ResultRow
    {
        public int Rank { get; init; }
        public required string Seed { get; init; }
        public double Score { get; init; }
        public double EvilWidth { get; init; }
        public double SpreadWidth { get; init; }
        public double Chests { get; init; }
        public required RollResult Result { get; init; }
    }
}
