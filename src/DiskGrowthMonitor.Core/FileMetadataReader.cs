namespace DiskGrowthMonitor.Core;

public interface IFileMetadataReader
{
    PathMetadata Read(string path);
}

public sealed class FileMetadataReader : IFileMetadataReader
{
    public PathMetadata Read(string path)
    {
        var fullPath = Path.GetFullPath(path);

        if (Directory.Exists(fullPath))
        {
            var info = new DirectoryInfo(fullPath);
            return new PathMetadata(fullPath, true, true, 0, info.LastWriteTime);
        }

        if (File.Exists(fullPath))
        {
            var info = new FileInfo(fullPath);
            return new PathMetadata(fullPath, true, false, info.Length, info.LastWriteTime);
        }

        return new PathMetadata(fullPath, false, false, 0, DateTime.MinValue);
    }
}
