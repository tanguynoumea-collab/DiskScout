using DiskScout.Models;

namespace DiskScout.Services;

public interface ICloudStorageAnalyzer
{
    Task<IReadOnlyList<CloudSyncRoot>> AnalyzeAsync(
        IReadOnlyList<FileSystemNode> nodes,
        CancellationToken cancellationToken);
}
