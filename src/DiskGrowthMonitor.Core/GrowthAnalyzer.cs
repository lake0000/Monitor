namespace DiskGrowthMonitor.Core;

public static class GrowthAnalyzer
{
    public static (GrowthEvent? Event, FileSnapshot Snapshot) Analyze(
        PathMetadata metadata,
        FileSnapshot? previous,
        GrowthEventType eventType,
        IReadOnlyCollection<string> watchRoots,
        DateTime now,
        long persistThresholdBytes)
    {
        var oldSize = previous?.Size ?? 0;
        var newSize = metadata.Exists ? metadata.Size : 0;
        var delta = newSize - oldSize;
        var snapshot = new FileSnapshot(metadata.Path, newSize, metadata.LastWriteTime, now, !metadata.Exists);

        if (eventType == GrowthEventType.Deleted)
        {
            delta = -oldSize;
        }

        if (Math.Abs(delta) < persistThresholdBytes && eventType != GrowthEventType.Deleted)
        {
            return (null, snapshot);
        }

        var groupPath = DirectoryGrouper.GetGroupPath(metadata.Path, watchRoots);
        var (source, confidence) = SourceGuesser.Guess(groupPath);
        var growthEvent = new GrowthEvent(
            0,
            now,
            metadata.Path,
            eventType,
            oldSize,
            newSize,
            delta,
            groupPath,
            source,
            confidence);

        return (growthEvent, snapshot);
    }
}
