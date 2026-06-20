using DiskGrowthMonitor.App;
using DiskGrowthMonitor.Core;

internal static class Program
{
    private static readonly string Workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static readonly string TestRoot = Path.Combine(Workspace, ".test-data", DateTime.Now.ToString("yyyyMMdd-HHmmss"));

    [STAThread]
    private static int Main()
    {
        ApplicationConfiguration.Initialize();
        Directory.CreateDirectory(TestRoot);

        var tests = new (string Name, Action Body)[]
        {
            ("安全路径校验", SafetyPathValidation),
            ("增长计算与目录聚合", GrowthCalculationAndGrouping),
            ("SQLite 持久化", SqlitePersistence),
            ("SQLite 并发访问", SqliteConcurrentAccess),
            ("目录累计阈值", DirectoryCumulativeThreshold),
            ("减少变化聚合", NegativeDeltaAggregate),
            ("溯源树", TraceTree),
            ("忽略列表", IgnoreList),
            ("暂停恢复", PauseResume),
            ("监听端到端", WatcherEndToEnd),
            ("UI 烟雾测试", UiSmoke),
            ("源码危险调用扫描", SourceSafetyScan)
        };

        var failures = new List<string>();
        foreach (var test in tests)
        {
            try
            {
                test.Body();
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception ex)
            {
                failures.Add($"{test.Name}: {ex.Message}");
                Console.WriteLine($"FAIL {test.Name}");
                Console.WriteLine(ex);
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Total: {tests.Length}, Passed: {tests.Length - failures.Count}, Failed: {failures.Count}");
        if (failures.Count > 0)
        {
            Console.WriteLine("Failures:");
            foreach (var failure in failures)
            {
                Console.WriteLine(" - " + failure);
            }
            return 1;
        }

        return 0;
    }

    private static void SafetyPathValidation()
    {
        var dataDir = CreateDirectory("appdata");
        var watch = CreateDirectory("watch");
        var duplicate = PathRules.ValidateWatchDirectory(watch, new[] { watch }, dataDir);
        AssertEqual(WatchPathValidationStatus.Duplicate, duplicate.Status, "重复路径应被拦截");

        var toolData = PathRules.ValidateWatchDirectory(dataDir, Array.Empty<string>(), dataDir);
        AssertEqual(WatchPathValidationStatus.ToolDataDirectory, toolData.Status, "工具数据目录不应被监听");

        if (Directory.Exists(@"C:\Windows"))
        {
            var windows = PathRules.ValidateWatchDirectory(@"C:\Windows", Array.Empty<string>(), dataDir);
            AssertEqual(WatchPathValidationStatus.SensitiveSystemRoot, windows.Status, "C:\\Windows 根目录应被拦截");
        }
    }

    private static void GrowthCalculationAndGrouping()
    {
        var root = CreateDirectory("group-root");
        var file = Path.Combine(root, "Cache", "Chrome", "item.bin");
        var metadata = new PathMetadata(file, true, false, 3200, DateTime.Now);
        var previous = new FileSnapshot(file, 1000, DateTime.Now.AddMinutes(-1), DateTime.Now.AddMinutes(-1), false);

        var (growthEvent, snapshot) = GrowthAnalyzer.Analyze(
            metadata,
            previous,
            GrowthEventType.Changed,
            new[] { root },
            DateTime.Now,
            10);

        AssertNotNull(growthEvent, "应产生增长事件");
        AssertEqual(2200L, growthEvent!.DeltaSize, "增长量错误");
        AssertTrue(growthEvent.GroupPath.Contains("Cache", StringComparison.OrdinalIgnoreCase), "聚合路径应停在缓存语义目录附近");
        AssertEqual(3200L, snapshot.Size, "快照应更新为当前大小");
    }

    private static void SqlitePersistence()
    {
        var store = CreateStore("sqlite");
        var path = Path.Combine(TestRoot, "sqlite", "file.bin");
        var item = new GrowthEvent(0, DateTime.Now, path, GrowthEventType.Created, 0, 3000, 3000, Path.GetDirectoryName(path)!, "测试", 0.9);
        store.InsertGrowthEvents(new[] { item });
        store.SetSetting("displayThresholdBytes", "2000");
        store.AddIgnorePath(Path.GetDirectoryName(path)!, "test");

        AssertEqual("2000", store.GetSetting("displayThresholdBytes"), "设置应持久化");
        AssertTrue(store.IsIgnored(path), "忽略目录下文件应被识别为忽略");
        AssertEqual(1, store.QueryRecentEvents().Count, "应保存一条增长事件");
    }

    private static void SqliteConcurrentAccess()
    {
        var store = CreateStore("sqlite-concurrent");
        var root = CreateDirectory("sqlite-concurrent", "watch");
        var errors = new List<Exception>();
        var tasks = Enumerable.Range(0, 6).Select(worker => Task.Run(() =>
        {
            try
            {
                for (var i = 0; i < 35; i++)
                {
                    var path = Path.Combine(root, $"worker-{worker}", $"file-{i}.bin");
                    var group = Path.GetDirectoryName(path)!;
                    var now = DateTime.Now;
                    store.UpsertSnapshot(new FileSnapshot(path, i * 1024, now, now, false));
                    store.InsertGrowthEvents(new[]
                    {
                        new GrowthEvent(0, now, path, GrowthEventType.Changed, 0, 2048, 2048, group, "测试", 0.5)
                    });
                    _ = store.QueryAggregates(TimeSpan.FromHours(1), 1);
                    _ = store.GetIgnoreList();
                }
            }
            catch (Exception exception)
            {
                lock (errors)
                {
                    errors.Add(exception);
                }
            }
        })).ToArray();

        Task.WaitAll(tasks);
        AssertEqual(0, errors.Count, "SQLite 并发读写不应抛出异常");
    }

    private static void DirectoryCumulativeThreshold()
    {
        var store = CreateStore("aggregate");
        var root = CreateDirectory("aggregate", "watch");
        var group = Path.Combine(root, "Temp");
        var events = new[]
        {
            NewEvent(Path.Combine(group, "a.tmp"), group, 800),
            NewEvent(Path.Combine(group, "b.tmp"), group, 700),
            NewEvent(Path.Combine(group, "c.tmp"), group, 600)
        };
        store.InsertGrowthEvents(events);

        var hidden = store.QueryAggregates(TimeSpan.FromHours(1), 2200);
        AssertEqual(0, hidden.Count, "累计低于阈值不应显示");

        var visible = store.QueryAggregates(TimeSpan.FromHours(1), 2000);
        AssertEqual(1, visible.Count, "累计超过阈值应显示目录");
        AssertEqual(2100L, visible[0].DeltaSize, "累计增长量错误");
    }

    private static void NegativeDeltaAggregate()
    {
        var store = CreateStore("negative");
        var root = CreateDirectory("negative", "watch");
        var group = Path.Combine(root, "Cache");
        store.InsertGrowthEvents(new[]
        {
            new GrowthEvent(0, DateTime.Now, Path.Combine(group, "old.bin"), GrowthEventType.Changed, 3000, 500, -2500, group, "缓存", 0.8)
        });

        var visible = store.QueryAggregates(TimeSpan.FromHours(1), 1000);
        AssertEqual(1, visible.Count, "减少变化也应进入列表");
        AssertEqual(-2500L, visible[0].DeltaSize, "减少量应保留负数");
    }

    private static void TraceTree()
    {
        var store = CreateStore("trace");
        var root = CreateDirectory("trace", "watch");
        var group = Path.Combine(root, "AppData");
        var child = Path.Combine(group, "Chrome");
        store.InsertGrowthEvents(new[]
        {
            new GrowthEvent(0, DateTime.Now, Path.Combine(child, "Cache", "a.bin"), GrowthEventType.Created, 0, 1800, 1800, group, "缓存", 0.8),
            new GrowthEvent(0, DateTime.Now, Path.Combine(child, "Cache", "b.bin"), GrowthEventType.Changed, 3000, 2000, -1000, group, "缓存", 0.8)
        });

        var trace = new GrowthTraceBuilder(store, new GrowthTraceOptions { MinDeltaBytes = 100, Window = TimeSpan.FromHours(1) }).Build(group);
        AssertTrue(trace.Children.Count > 0, "溯源树应包含下一级路径");
        AssertTrue(trace.Children[0].Path.Contains("Chrome", StringComparison.OrdinalIgnoreCase), "溯源树应定位到下一级目录");
    }

    private static void IgnoreList()
    {
        var store = CreateStore("ignore");
        var root = CreateDirectory("ignore", "watch");
        var child = Path.Combine(root, "child", "file.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(child)!);

        store.AddIgnorePath(root, "ignore root");
        AssertTrue(store.IsIgnored(child), "子路径应被忽略");
        store.RemoveIgnorePath(root);
        AssertFalse(store.IsIgnored(child), "取消忽略后应恢复");
    }

    private static void PauseResume()
    {
        var settings = CreateSettings("pause");
        var store = new SqliteMonitorStore(settings.DatabasePath);
        store.Initialize();
        using var service = new FileGrowthMonitorService(settings, store);
        service.Pause();
        service.RecordObservedPath(Path.Combine(settings.WatchDirectories[0], "paused.bin"), GrowthEventType.Created);
        AssertEqual(0, service.ProcessDueEvents(), "暂停期间不应处理事件");
        service.Resume();
        AssertFalse(service.IsPaused, "恢复后状态应为非暂停");
    }

    private static void WatcherEndToEnd()
    {
        var settings = CreateSettings("watcher");
        settings.DebounceDelay = TimeSpan.FromMilliseconds(250);
        settings.BatchInterval = TimeSpan.FromMilliseconds(250);
        var store = new SqliteMonitorStore(settings.DatabasePath);
        using var service = new FileGrowthMonitorService(settings, store);
        service.Start();

        var file = Path.Combine(settings.WatchDirectories[0], "growth.bin");
        File.WriteAllBytes(file, new byte[1800]);

        WaitUntil(() =>
        {
            service.ProcessDueEvents();
            service.FlushBufferedEvents();
            return store.QueryAggregates(TimeSpan.FromMinutes(10), settings.DisplayThresholdBytes).Any();
        }, TimeSpan.FromSeconds(5), "监听端到端未记录增长");
    }

    private static void UiSmoke()
    {
        var settings = CreateSettings("ui");
        var store = new SqliteMonitorStore(settings.DatabasePath);
        using var service = new FileGrowthMonitorService(settings, store);
        using var form = new MainForm(settings, store, service, false);

        AssertNotNull(FindControl(form, "TenMinuteList"), "缺少最近 10 分钟列表");
        AssertNotNull(FindControl(form, "DetailBox"), "缺少详情面板");
        AssertNotNull(FindControl(form, "PauseButton"), "缺少暂停按钮");
        AssertNotNull(FindControl(form, "ManualScanButton"), "缺少手动扫描按钮");
        AssertNotNull(FindControl(form, "DisplayThresholdInput"), "缺少显示阈值输入框");

        var allText = string.Join(" ", EnumerateControls(form).Select(c => c.Text));
        foreach (var word in form.ForbiddenUiWords)
        {
            AssertFalse(allText.Contains(word, StringComparison.OrdinalIgnoreCase), $"UI 不应出现危险文案：{word}");
        }
    }

    private static void SourceSafetyScan()
    {
        var sourceRoots = new[]
        {
            Path.Combine(Workspace, "src", "DiskGrowthMonitor.Core"),
            Path.Combine(Workspace, "src", "DiskGrowthMonitor.App")
        };
        var forbidden = new[]
        {
            "File.Delete(",
            "Directory.Delete(",
            "File.Move(",
            "Directory.Move(",
            "File.WriteAllText(",
            "File.WriteAllBytes("
        };

        foreach (var file in sourceRoots.SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)))
        {
            var text = File.ReadAllText(file);
            foreach (var token in forbidden)
            {
                AssertFalse(text.Contains(token, StringComparison.Ordinal), $"{file} 包含禁止调用 {token}");
            }
        }
    }

    private static MonitorSettings CreateSettings(string name)
    {
        var data = CreateDirectory(name, "appdata");
        var watch = CreateDirectory(name, "watch");
        var settings = new MonitorSettings
        {
            DataDirectory = data,
            PersistThresholdBytes = 100,
            DisplayThresholdBytes = 1000,
            AlertThresholdBytes = 5000,
            DebounceDelay = TimeSpan.FromMilliseconds(100),
            BatchInterval = TimeSpan.FromMilliseconds(100)
        };
        settings.WatchDirectories.Add(watch);
        return settings;
    }

    private static SqliteMonitorStore CreateStore(string name)
    {
        var data = CreateDirectory(name, "appdata");
        var store = new SqliteMonitorStore(Path.Combine(data, "monitor.db"));
        store.Initialize();
        return store;
    }

    private static GrowthEvent NewEvent(string path, string group, long delta)
    {
        var (source, confidence) = SourceGuesser.Guess(group);
        return new GrowthEvent(0, DateTime.Now, path, GrowthEventType.Created, 0, delta, delta, group, source, confidence);
    }

    private static string CreateDirectory(params string[] parts)
    {
        var path = Path.Combine(new[] { TestRoot }.Concat(parts).ToArray());
        Directory.CreateDirectory(path);
        return path;
    }

    private static Control? FindControl(Control root, string name)
    {
        return EnumerateControls(root).FirstOrDefault(control => control.Name == name);
    }

    private static IEnumerable<Control> EnumerateControls(Control root)
    {
        yield return root;
        foreach (Control child in root.Controls)
        {
            foreach (var item in EnumerateControls(child))
            {
                yield return item;
            }
        }
    }

    private static void WaitUntil(Func<bool> condition, TimeSpan timeout, string message)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }
            Thread.Sleep(100);
            Application.DoEvents();
        }
        throw new InvalidOperationException(message);
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertFalse(bool condition, string message)
    {
        AssertTrue(!condition, message);
    }

    private static void AssertNotNull(object? value, string message)
    {
        AssertTrue(value is not null, message);
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message}。预期：{expected}，实际：{actual}");
        }
    }
}
