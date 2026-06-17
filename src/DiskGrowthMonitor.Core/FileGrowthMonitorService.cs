using System.Collections.Concurrent;

namespace DiskGrowthMonitor.Core;

public sealed class FileGrowthMonitorService : IDisposable
{
    private readonly MonitorSettings _settings;
    private readonly SqliteMonitorStore _store;
    private readonly IFileMetadataReader _metadataReader;
    private readonly ConcurrentDictionary<string, PendingChange> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<GrowthEvent> _buffer = new();
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly object _sync = new();
    private Timer? _debounceTimer;
    private Timer? _flushTimer;
    private bool _paused;

    public FileGrowthMonitorService(MonitorSettings settings, SqliteMonitorStore store, IFileMetadataReader? metadataReader = null)
    {
        _settings = settings;
        _store = store;
        _metadataReader = metadataReader ?? new FileMetadataReader();
    }

    public bool IsRunning { get; private set; }
    public bool IsPaused => _paused;

    public event EventHandler<GrowthEvent>? GrowthEventRecorded;
    public event EventHandler<string>? PathSkipped;

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        _store.Initialize();
        foreach (var directory in _settings.WatchDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            TryAddWatcher(directory);
        }

        _debounceTimer = new Timer(_ => ProcessDueEvents(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        _flushTimer = new Timer(_ => FlushBufferedEvents(), null, _settings.BatchInterval, _settings.BatchInterval);
        IsRunning = true;
    }

    public void Pause()
    {
        _paused = true;
    }

    public void Resume()
    {
        _paused = false;
    }

    public void RecordObservedPath(string path, GrowthEventType eventType)
    {
        if (_paused || _store.IsIgnored(path))
        {
            return;
        }

        var fullPath = Path.GetFullPath(path);
        _pending.AddOrUpdate(
            fullPath,
            _ => new PendingChange(fullPath, eventType, DateTime.Now),
            (_, existing) => existing with { EventType = eventType, LastObserved = DateTime.Now });
    }

    public int ProcessDueEvents()
    {
        if (_paused)
        {
            return 0;
        }

        var now = DateTime.Now;
        var processed = 0;
        foreach (var item in _pending.ToArray())
        {
            if (now - item.Value.LastObserved < _settings.DebounceDelay)
            {
                continue;
            }

            if (!_pending.TryRemove(item.Key, out var change))
            {
                continue;
            }

            ProcessChange(change, now);
            processed++;
        }
        return processed;
    }

    public int FlushBufferedEvents()
    {
        List<GrowthEvent> events;
        lock (_sync)
        {
            if (_buffer.Count == 0)
            {
                return 0;
            }
            events = _buffer.ToList();
            _buffer.Clear();
        }

        _store.InsertGrowthEvents(events);
        foreach (var item in events)
        {
            GrowthEventRecorded?.Invoke(this, item);
        }
        return events.Count;
    }

    public int ManualScan()
    {
        var count = 0;
        foreach (var root in _settings.WatchDirectories.ToArray())
        {
            count += ManualScan(root);
        }
        FlushBufferedEvents();
        return count;
    }

    public int ManualScan(string root)
    {
        if (_paused || !Directory.Exists(root) || PathRules.IsSensitiveSystemRoot(root))
        {
            return 0;
        }

        var count = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (_store.IsIgnored(file))
                {
                    continue;
                }
                var metadata = _metadataReader.Read(file);
                var previous = _store.GetSnapshot(metadata.Path);
                var (growthEvent, snapshot) = GrowthAnalyzer.Analyze(
                    metadata,
                    previous,
                    GrowthEventType.ManualScan,
                    _settings.WatchDirectories,
                    DateTime.Now,
                    _settings.PersistThresholdBytes);
                _store.UpsertSnapshot(snapshot);
                if (growthEvent is not null && growthEvent.DeltaSize > 0)
                {
                    AddBufferedEvent(growthEvent);
                }
                count++;
            }
        }
        catch (UnauthorizedAccessException)
        {
            PathSkipped?.Invoke(this, root);
        }
        catch (IOException)
        {
            PathSkipped?.Invoke(this, root);
        }

        return count;
    }

    public void Dispose()
    {
        _debounceTimer?.Dispose();
        _flushTimer?.Dispose();
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }
    }

    private void TryAddWatcher(string directory)
    {
        if (!Directory.Exists(directory) || PathRules.IsSensitiveSystemRoot(directory))
        {
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };
            watcher.Created += (_, e) => RecordObservedPath(e.FullPath, GrowthEventType.Created);
            watcher.Changed += (_, e) => RecordObservedPath(e.FullPath, GrowthEventType.Changed);
            watcher.Deleted += (_, e) => RecordObservedPath(e.FullPath, GrowthEventType.Deleted);
            watcher.Renamed += (_, e) => RecordObservedPath(e.FullPath, GrowthEventType.Renamed);
            watcher.Error += (_, _) => PathSkipped?.Invoke(this, directory);
            _watchers.Add(watcher);
        }
        catch (UnauthorizedAccessException)
        {
            PathSkipped?.Invoke(this, directory);
        }
        catch (IOException)
        {
            PathSkipped?.Invoke(this, directory);
        }
    }

    private void ProcessChange(PendingChange change, DateTime now)
    {
        var metadata = _metadataReader.Read(change.Path);
        var previous = _store.GetSnapshot(metadata.Path);
        var eventType = metadata.Exists ? change.EventType : GrowthEventType.Deleted;
        var (growthEvent, snapshot) = GrowthAnalyzer.Analyze(
            metadata,
            previous,
            eventType,
            _settings.WatchDirectories,
            now,
            _settings.PersistThresholdBytes);
        _store.UpsertSnapshot(snapshot);
        if (growthEvent is not null)
        {
            AddBufferedEvent(growthEvent);
        }
    }

    private void AddBufferedEvent(GrowthEvent growthEvent)
    {
        lock (_sync)
        {
            _buffer.Add(growthEvent);
        }
    }

    private sealed record PendingChange(string Path, GrowthEventType EventType, DateTime LastObserved);
}
