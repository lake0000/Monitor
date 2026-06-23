using System.Diagnostics;
using System.Text;
using DiskGrowthMonitor.Core;

namespace DiskGrowthMonitor.App;

public sealed class MainForm : Form
{
    private readonly MonitorSettings _settings;
    private readonly SqliteMonitorStore _store;
    private readonly FileGrowthMonitorService _service;
    private readonly bool _ownsService;
    private readonly NotifyIcon _trayIcon;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly bool _startService;
    private readonly Icon _appIcon;
    private bool _serviceStartRequested;
    private int _skippedPaths;

    private readonly Label _statusLabel = new();
    private readonly Label _spaceLabel = new();
    private readonly Label _todayLabel = new();
    private readonly ListView _tenMinuteList = new SmoothListView();
    private readonly ListView _hourList = new SmoothListView();
    private readonly ListView _todayList = new SmoothListView();
    private readonly TextBox _detailBox = new();
    private readonly TextBox _traceBox = new();
    private readonly NumericUpDown _displayThresholdInput = new();
    private readonly Dictionary<string, string> _listSnapshots = new(StringComparer.Ordinal);
    private GrowthAggregate? _selectedAggregate;
    private bool _loadingThreshold;
    private bool _isRefreshingDashboard;

    public MainForm()
        : this(MonitorSettings.CreateDefault(), null, null, true)
    {
    }

    public MainForm(MonitorSettings settings, SqliteMonitorStore? store, FileGrowthMonitorService? service, bool startService)
    {
        _settings = settings;
        _store = store ?? new SqliteMonitorStore(settings.DatabasePath);
        StartupLog.Write("Initializing store.");
        _store.Initialize();
        LoadPersistedSettings();
        _service = service ?? new FileGrowthMonitorService(settings, _store);
        _ownsService = service is null;
        _startService = startService;
        _appIcon = AppIconFactory.CreateIcon(32);

        Text = "C盘增长监视";
        Name = "MainForm";
        Icon = _appIcon;
        MinimumSize = new Size(1120, 720);
        Size = new Size(1180, 760);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular);
        BackColor = Color.FromArgb(241, 247, 248);
        ForeColor = Color.FromArgb(24, 39, 44);
        DoubleBuffered = true;

        BuildLayout();

        _trayIcon = BuildTrayIcon();
        _trayIcon.Visible = true;

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _refreshTimer.Tick += (_, _) => RefreshDashboard();
        _refreshTimer.Start();

        _service.GrowthEventRecorded += (_, item) => BeginInvoke((Action)(() =>
        {
            if (item.DeltaSize >= _settings.AlertThresholdBytes)
            {
                _trayIcon.ShowBalloonTip(
                    3500,
                    "C盘空间增长异常",
                    $"{item.GroupPath} 最近增长 +{FormatBytes(item.DeltaSize)}。工具仅用于定位，不会自动处理文件。",
                    ToolTipIcon.Warning);
            }
            RefreshDashboard();
        }));
        _service.PathSkipped += (_, _) =>
        {
            _skippedPaths++;
            BeginInvoke((Action)RefreshDashboard);
        };
        _service.MonitorError += (_, exception) =>
        {
            StartupLog.Write(exception);
        };

        RefreshDashboard();
        StartupLog.Write("Main window constructed.");
    }

    public IReadOnlyList<string> ForbiddenUiWords => new[] { "一键清理", "立即释放", "安全删除", "自动优化", "系统瘦身" };

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            _trayIcon.ShowBalloonTip(1800, "C盘增长监视", "工具仍在托盘后台观察文件变化。", ToolTipIcon.Info);
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        StartupLog.Write("Main window shown.");
        if (!_startService || _serviceStartRequested)
        {
            return;
        }

        _serviceStartRequested = true;
        BeginInvoke((Action)(() =>
        {
            try
            {
                StartupLog.Write("Starting monitor service.");
                _service.Start();
                StartupLog.Write("Monitor service started.");
                RefreshDashboard();
            }
            catch (Exception exception)
            {
                StartupLog.Write(exception);
                MessageBox.Show(this, exception.Message, "监听启动异常", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _appIcon.Dispose();
            if (_ownsService)
            {
                _service.Dispose();
            }
        }
        base.Dispose(disposing);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(22),
            BackColor = BackColor,
            Name = "RootLayout"
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var header = BuildHeader();
        root.Controls.Add(header, 0, 0);
        root.SetColumnSpan(header, 2);
        root.Controls.Add(BuildRankings(), 0, 1);
        root.Controls.Add(BuildDetailPanel(), 1, 1);
    }

    private Control BuildHeader()
    {
        var panel = CreateGlassPanel("HeaderPanel");
        panel.Padding = new Padding(24, 18, 24, 18);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, BackColor = Color.Transparent };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 17));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 17));
        panel.Controls.Add(layout);

        var titleBlock = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        titleBlock.Controls.Add(new Label
        {
            Text = "C盘增长监视",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 22F, FontStyle.Bold),
            ForeColor = Color.FromArgb(13, 76, 91)
        });
        titleBlock.Controls.Add(new Label
        {
            Text = "观察增长与减少 · 点击条目可溯源",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 10F, FontStyle.Regular),
            ForeColor = Color.FromArgb(91, 109, 114),
            Margin = new Padding(2, 6, 0, 0)
        });
        layout.Controls.Add(titleBlock, 0, 0);

        _spaceLabel.Name = "SpaceLabel";
        _todayLabel.Name = "TodayGrowthLabel";
        _statusLabel.Name = "StatusLabel";
        layout.Controls.Add(CreateMetric("C盘剩余空间", _spaceLabel), 1, 0);
        layout.Controls.Add(CreateMetric("今日空间变化", _todayLabel), 2, 0);
        layout.Controls.Add(CreateMetric("监听状态", _statusLabel), 3, 0);
        layout.Controls.Add(CreateThresholdControl(), 4, 0);
        return panel;
    }

    private Control BuildRankings()
    {
        var panel = CreateGlassPanel("RankingPanel");
        panel.Padding = new Padding(18);

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Name = "RankingTabs",
            Font = new Font(Font.FontFamily, 10F, FontStyle.Bold)
        };

        ConfigureList(_tenMinuteList, "TenMinuteList");
        ConfigureList(_hourList, "HourList");
        ConfigureList(_todayList, "TodayList");

        tabs.TabPages.Add(CreateTab("最近 10 分钟", _tenMinuteList));
        tabs.TabPages.Add(CreateTab("最近 1 小时", _hourList));
        tabs.TabPages.Add(CreateTab("今日", _todayList));
        panel.Controls.Add(tabs);
        return panel;
    }

    private Control BuildDetailPanel()
    {
        var panel = CreateGlassPanel("DetailPanel");
        panel.Padding = new Padding(18);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, BackColor = Color.Transparent };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 166));
        panel.Controls.Add(layout);

        layout.Controls.Add(new Label
        {
            Text = "详情与溯源",
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 15F, FontStyle.Bold),
            ForeColor = Color.FromArgb(13, 76, 91)
        }, 0, 0);

        _detailBox.Name = "DetailBox";
        _detailBox.Dock = DockStyle.Fill;
        _detailBox.Multiline = true;
        _detailBox.ReadOnly = true;
        _detailBox.BorderStyle = BorderStyle.None;
        _detailBox.BackColor = Color.FromArgb(248, 252, 251);
        _detailBox.ForeColor = Color.FromArgb(36, 54, 59);
        _detailBox.ScrollBars = ScrollBars.Vertical;
        _detailBox.WordWrap = true;
        _detailBox.Text = "选择一条记录查看完整路径、具体文件和溯源树。";
        layout.Controls.Add(_detailBox, 0, 1);

        _traceBox.Name = "TraceTree";
        _traceBox.Dock = DockStyle.Fill;
        _traceBox.Multiline = true;
        _traceBox.ReadOnly = true;
        _traceBox.BorderStyle = BorderStyle.None;
        _traceBox.BackColor = Color.FromArgb(248, 252, 251);
        _traceBox.ForeColor = Color.FromArgb(24, 39, 44);
        _traceBox.ScrollBars = ScrollBars.Vertical;
        _traceBox.WordWrap = true;
        _traceBox.Text = "溯源将在选择条目后显示。";
        layout.Controls.Add(_traceBox, 0, 2);

        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 2, BackColor = Color.Transparent, Padding = new Padding(0, 14, 0, 0) };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        layout.Controls.Add(actions, 0, 3);

        actions.Controls.Add(CreateButton("打开所在文件夹", OpenSelectedFolder, "OpenFolderButton"), 0, 0);
        actions.Controls.Add(CreateButton("复制路径", CopySelectedPath, "CopyPathButton"), 1, 0);
        actions.Controls.Add(CreateButton("加入忽略列表", IgnoreSelectedPath, "IgnorePathButton"), 0, 1);
        actions.Controls.Add(CreateButton("导出今日 CSV", ExportToday, "ExportButton"), 1, 1);
        actions.Controls.Add(CreateButton("暂停监听", TogglePause, "PauseButton"), 0, 2);
        actions.Controls.Add(CreateButton("手动扫描", ManualScan, "ManualScanButton"), 1, 2);
        return panel;
    }

    private NotifyIcon BuildTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开主界面", null, (_, _) => ShowMainWindow());
        menu.Items.Add("暂停监听", null, (_, _) => PauseFromTray());
        menu.Items.Add("恢复监听", null, (_, _) => ResumeFromTray());
        menu.Items.Add("手动扫描一次", null, (_, _) => ManualScan());
        menu.Items.Add("设置", null, (_, _) => ShowMainWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApplication());

        var icon = new NotifyIcon
        {
            Text = "C盘增长监视",
            Icon = _appIcon,
            ContextMenuStrip = menu
        };
        icon.DoubleClick += (_, _) => ShowMainWindow();
        return icon;
    }

    private void RefreshDashboard()
    {
        if (_isRefreshingDashboard)
        {
            return;
        }

        _isRefreshingDashboard = true;
        try
        {
            UpdateMetrics();
            FillList(_tenMinuteList, _store.QueryAggregates(TimeSpan.FromMinutes(10), _settings.DisplayThresholdBytes));
            FillList(_hourList, _store.QueryAggregates(TimeSpan.FromHours(1), _settings.DisplayThresholdBytes));
            FillList(_todayList, _store.QueryAggregates(TimeSpan.FromDays(1), _settings.DisplayThresholdBytes));
        }
        finally
        {
            _isRefreshingDashboard = false;
        }
    }

    private void UpdateMetrics()
    {
        try
        {
            var drive = new DriveInfo("C");
            var currentFree = drive.AvailableFreeSpace;
            _spaceLabel.Text = FormatBytes(currentFree);
            _store.RecordDriveSpaceSample("C", drive.TotalSize, currentFree, DateTime.Now);
            var freeSpaceChange = _store.GetDriveFreeSpaceChange("C", DateTime.Today, currentFree);
            _todayLabel.Text = freeSpaceChange is null ? "记录中" : FormatSignedBytes(freeSpaceChange.Value);
            _todayLabel.ForeColor = freeSpaceChange switch
            {
                < 0 => Color.FromArgb(183, 84, 42),
                > 0 => Color.FromArgb(15, 126, 112),
                _ => Color.FromArgb(13, 76, 91)
            };
        }
        catch
        {
            _spaceLabel.Text = "无法读取";
            _todayLabel.Text = "无法读取";
            _todayLabel.ForeColor = Color.FromArgb(13, 76, 91);
        }

        _statusLabel.Text = _service.IsPaused ? "已暂停" : $"观察中 · 跳过 {_skippedPaths}";
    }

    private void FillList(ListView list, IReadOnlyList<GrowthAggregate> items)
    {
        var snapshot = BuildListSnapshot(items);
        if (_listSnapshots.TryGetValue(list.Name, out var previousSnapshot) &&
            string.Equals(previousSnapshot, snapshot, StringComparison.Ordinal))
        {
            return;
        }

        _listSnapshots[list.Name] = snapshot;
        var selectedGroupPath = list.SelectedItems.Count > 0
            ? (list.SelectedItems[0].Tag as GrowthAggregate)?.GroupPath
            : null;

        list.BeginUpdate();
        try
        {
            list.Items.Clear();
            foreach (var aggregate in items)
            {
                var item = new ListViewItem(new[]
                {
                    aggregate.GroupPath,
                    FormatSignedBytes(aggregate.DeltaSize),
                    aggregate.DeltaSize >= 0 ? "增加" : "减少",
                    aggregate.SourceGuess,
                    aggregate.LastSeen.ToString("HH:mm:ss")
                })
                {
                    Tag = aggregate,
                    ForeColor = aggregate.DeltaSize >= 0 ? Color.FromArgb(183, 84, 42) : Color.FromArgb(15, 126, 112)
                };

                list.Items.Add(item);
                if (!string.IsNullOrWhiteSpace(selectedGroupPath) &&
                    string.Equals(selectedGroupPath, aggregate.GroupPath, StringComparison.OrdinalIgnoreCase))
                {
                    item.Selected = true;
                    item.Focused = true;
                }
            }
        }
        finally
        {
            list.EndUpdate();
        }
    }

    private static string BuildListSnapshot(IReadOnlyList<GrowthAggregate> items)
    {
        var builder = new StringBuilder();
        foreach (var item in items)
        {
            builder
                .Append(item.GroupPath).Append('|')
                .Append(item.DeltaSize).Append('|')
                .Append(item.EventCount).Append('|')
                .Append(item.LastSeen.Ticks).Append('|')
                .Append(item.SourceGuess).Append('\n');
        }
        return builder.ToString();
    }

    private void ConfigureList(ListView list, string name)
    {
        list.Name = name;
        list.Dock = DockStyle.Fill;
        list.View = View.Details;
        list.FullRowSelect = true;
        list.BorderStyle = BorderStyle.None;
        list.BackColor = Color.FromArgb(248, 252, 251);
        list.ForeColor = Color.FromArgb(24, 39, 44);
        list.Columns.Add("路径", 430);
        list.Columns.Add("变化量", 112);
        list.Columns.Add("方向", 68);
        list.Columns.Add("推测来源", 122);
        list.Columns.Add("最后变化", 92);
        list.SelectedIndexChanged += (_, _) =>
        {
            if (list.SelectedItems.Count == 0)
            {
                return;
            }
            _selectedAggregate = list.SelectedItems[0].Tag as GrowthAggregate;
            UpdateDetailAndTrace();
        };
    }

    private static TabPage CreateTab(string title, Control content)
    {
        var tab = new TabPage(title) { BackColor = Color.FromArgb(248, 252, 251), Padding = new Padding(12) };
        tab.Controls.Add(content);
        return tab;
    }

    private Panel CreateGlassPanel(string name)
    {
        return new RoundedPanel
        {
            Name = name,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 18, 18),
            BackColor = Color.FromArgb(232, 244, 243),
            Radius = 18
        };
    }

    private Control CreateMetric(string label, Label value)
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(10, 10, 0, 0) };
        panel.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = Color.FromArgb(91, 109, 114), Font = new Font(Font.FontFamily, 9F) });
        value.AutoSize = true;
        value.Font = new Font(Font.FontFamily, 18F, FontStyle.Bold);
        value.ForeColor = Color.FromArgb(13, 76, 91);
        value.Margin = new Padding(0, 8, 0, 0);
        panel.Controls.Add(value);
        return panel;
    }

    private Control CreateThresholdControl()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(10, 10, 0, 0)
        };
        panel.Controls.Add(new Label
        {
            Text = "显示阈值 MB",
            AutoSize = true,
            ForeColor = Color.FromArgb(91, 109, 114),
            Font = new Font(Font.FontFamily, 9F)
        });

        _displayThresholdInput.Name = "DisplayThresholdInput";
        _displayThresholdInput.Minimum = 1;
        _displayThresholdInput.Maximum = 102400;
        _displayThresholdInput.Increment = 50;
        _displayThresholdInput.DecimalPlaces = 0;
        _displayThresholdInput.ThousandsSeparator = true;
        _displayThresholdInput.Width = 118;
        _displayThresholdInput.Height = 34;
        _displayThresholdInput.Margin = new Padding(0, 8, 0, 0);
        _displayThresholdInput.Font = new Font(Font.FontFamily, 14F, FontStyle.Bold);
        _displayThresholdInput.ForeColor = Color.FromArgb(13, 76, 91);
        _displayThresholdInput.BackColor = Color.FromArgb(248, 252, 251);
        _loadingThreshold = true;
        _displayThresholdInput.Value = Math.Max(1, _settings.DisplayThresholdBytes / 1024 / 1024);
        _loadingThreshold = false;
        _displayThresholdInput.ValueChanged += (_, _) =>
        {
            if (_loadingThreshold)
            {
                return;
            }

            _settings.DisplayThresholdBytes = (long)_displayThresholdInput.Value * 1024 * 1024;
            _store.SetSetting("displayThresholdBytes", _settings.DisplayThresholdBytes.ToString());
            RefreshDashboard();
        };
        panel.Controls.Add(_displayThresholdInput);
        return panel;
    }

    private Button CreateButton(string text, Action action, string name)
    {
        var button = new Button
        {
            Text = text,
            Name = name,
            Dock = DockStyle.Fill,
            Margin = new Padding(5),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(13, 120, 133),
            ForeColor = Color.White,
            Font = new Font(Font.FontFamily, 9.5F, FontStyle.Bold)
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) => action();
        return button;
    }

    private void UpdateDetailAndTrace()
    {
        _traceBox.Clear();
        if (_selectedAggregate is null)
        {
            _detailBox.Text = "选择一条记录查看完整路径、具体文件和溯源树。";
            _traceBox.Text = "溯源将在选择条目后显示。";
            return;
        }

        var recent = _store.QueryEventsForGroup(_selectedAggregate.GroupPath, TimeSpan.FromDays(1), 8);
        var builder = new GrowthTraceBuilder(_store, new GrowthTraceOptions
        {
            Window = TimeSpan.FromDays(1),
            MinDeltaBytes = Math.Max(1, _settings.PersistThresholdBytes),
            MaxDepth = 8,
            MaxEventsPerNode = 200,
            MaxChildrenPerNode = 10
        });
        var trace = builder.Build(_selectedAggregate.GroupPath);

        var detail = new StringBuilder();
        detail.AppendLine($"完整路径：{_selectedAggregate.GroupPath}");
        detail.AppendLine($"净变化：{FormatSignedBytes(_selectedAggregate.DeltaSize)}");
        detail.AppendLine($"方向：{(_selectedAggregate.DeltaSize >= 0 ? "增加" : "减少")}");
        detail.AppendLine($"首次发现：{_selectedAggregate.FirstSeen:yyyy-MM-dd HH:mm:ss}");
        detail.AppendLine($"最后变化：{_selectedAggregate.LastSeen:yyyy-MM-dd HH:mm:ss}");
        detail.AppendLine($"事件数量：{_selectedAggregate.EventCount}");
        detail.AppendLine($"推测来源：{_selectedAggregate.SourceGuess}");
        detail.AppendLine($"可信度：{_selectedAggregate.Confidence:P0}");
        detail.AppendLine();
        detail.AppendLine("最近具体文件：");
        if (recent.Count == 0)
        {
            detail.AppendLine("  暂无具体文件事件。");
        }
        foreach (var item in recent)
        {
            detail.AppendLine($"  {FormatSignedBytes(item.DeltaSize),10}  {Path.GetFileName(item.Path)}");
            detail.AppendLine(Indent(WrapPath(item.Path, 34), "      "));
        }
        detail.AppendLine();
        detail.AppendLine("说明：如果某个文件 A 是作为独立文件写入目录 B，可以看到 A 的文件名；如果 A 被写入压缩包、数据库、缓存容器等单个文件 B 内部，工具不读取正文，通常只能看到 B 变大。");
        _detailBox.Text = detail.ToString();

        _traceBox.Text = BuildTraceText(trace);
    }

    private string BuildTraceText(GrowthTraceNode trace)
    {
        var builder = new StringBuilder();
        AppendTraceNode(builder, trace, 0);
        return builder.ToString();
    }

    private void AppendTraceNode(StringBuilder builder, GrowthTraceNode trace, int depth)
    {
        var name = Path.GetFileName(trace.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(name))
        {
            name = trace.Path;
        }

        var prefix = new string(' ', depth * 2);
        builder.AppendLine($"{prefix}{name}  {FormatSignedBytes(trace.DeltaSize)}  ({trace.EventCount} 条)");
        builder.AppendLine(Indent(WrapPath(trace.Path, 36), prefix + "  "));

        foreach (var child in trace.Children)
        {
            AppendTraceNode(builder, child, depth + 1);
        }

        foreach (var item in trace.RecentEvents.Take(5))
        {
            builder.AppendLine($"{prefix}  {Path.GetFileName(item.Path)}  {FormatSignedBytes(item.DeltaSize)}");
            builder.AppendLine(Indent(WrapPath(item.Path, 34), prefix + "    "));
        }

        if (!string.IsNullOrWhiteSpace(trace.StopReason))
        {
            builder.AppendLine($"{prefix}  停止：{trace.StopReason}");
        }
    }

    private static string WrapPath(string path, int preferredLineLength)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length <= preferredLineLength)
        {
            return path;
        }

        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var builder = new StringBuilder();
        var line = new StringBuilder();
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part))
            {
                continue;
            }

            var next = line.Length == 0 ? part : line + "\\" + part;
            if (next.Length > preferredLineLength && line.Length > 0)
            {
                builder.AppendLine(line.ToString());
                line.Clear();
                line.Append(part);
            }
            else
            {
                if (line.Length > 0)
                {
                    line.Append('\\');
                }
                line.Append(part);
            }
        }

        if (line.Length > 0)
        {
            builder.Append(line);
        }
        return builder.ToString();
    }

    private static string Indent(string text, string prefix)
    {
        return string.Join(
            Environment.NewLine,
            text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Select(line => prefix + line));
    }

    private void OpenSelectedFolder()
    {
        if (_selectedAggregate is null)
        {
            return;
        }

        var path = Directory.Exists(_selectedAggregate.GroupPath)
            ? _selectedAggregate.GroupPath
            : Path.GetDirectoryName(_selectedAggregate.GroupPath);
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    private void CopySelectedPath()
    {
        if (_selectedAggregate is null)
        {
            return;
        }

        Clipboard.SetText(_selectedAggregate.GroupPath);
    }

    private void IgnoreSelectedPath()
    {
        if (_selectedAggregate is null)
        {
            return;
        }

        _store.AddIgnorePath(_selectedAggregate.GroupPath, "用户从主界面加入");
        RefreshDashboard();
    }

    private void ExportToday()
    {
        var output = _store.ExportCsv(TimeSpan.FromDays(1), Path.Combine(_settings.DataDirectory, "exports"));
        MessageBox.Show(this, $"已导出：{output}", "导出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void TogglePause()
    {
        if (_service.IsPaused)
        {
            _service.Resume();
        }
        else
        {
            _service.Pause();
        }
        RefreshDashboard();
    }

    private void ManualScan()
    {
        Cursor = Cursors.WaitCursor;
        try
        {
            _service.ManualScan();
            RefreshDashboard();
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void PauseFromTray()
    {
        _service.Pause();
        RefreshDashboard();
    }

    private void ResumeFromTray()
    {
        _service.Resume();
        RefreshDashboard();
    }

    private void ShowMainWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _trayIcon.Visible = false;
        Application.ExitThread();
    }

    private void LoadPersistedSettings()
    {
        var displayThreshold = _store.GetSetting("displayThresholdBytes");
        if (long.TryParse(displayThreshold, out var bytes) && bytes > 0)
        {
            _settings.DisplayThresholdBytes = bytes;
        }
    }

    private static string FormatSignedBytes(long bytes)
    {
        var prefix = bytes > 0 ? "+" : bytes < 0 ? "-" : "";
        return prefix + FormatBytes(Math.Abs(bytes));
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    private sealed class SmoothListView : ListView
    {
        public SmoothListView()
        {
            DoubleBuffered = true;
        }
    }
}
