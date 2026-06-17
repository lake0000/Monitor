namespace DiskGrowthMonitor.Core;

public enum GrowthEventType
{
    Created,
    Changed,
    Deleted,
    Renamed,
    ManualScan
}

public sealed record FileSnapshot(
    string Path,
    long Size,
    DateTime LastWriteTime,
    DateTime LastSeenTime,
    bool IsDeleted);

public sealed record GrowthEvent(
    long Id,
    DateTime EventTime,
    string Path,
    GrowthEventType EventType,
    long OldSize,
    long NewSize,
    long DeltaSize,
    string GroupPath,
    string SourceGuess,
    double Confidence);

public sealed record GrowthAggregate(
    string GroupPath,
    long DeltaSize,
    DateTime FirstSeen,
    DateTime LastSeen,
    int EventCount,
    string SourceGuess,
    double Confidence);

public sealed record IgnoreEntry(long Id, string Path, DateTime CreatedTime, string Note);

public sealed record PathMetadata(string Path, bool Exists, bool IsDirectory, long Size, DateTime LastWriteTime);

public sealed record GrowthTraceNode(
    string Path,
    long DeltaSize,
    int EventCount,
    int Depth,
    string StopReason,
    IReadOnlyList<GrowthTraceNode> Children,
    IReadOnlyList<GrowthEvent> RecentEvents);
