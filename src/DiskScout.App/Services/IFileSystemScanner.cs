using DiskScout.Models;

namespace DiskScout.Services;

public interface IFileSystemScanner
{
    Task<IReadOnlyList<FileSystemNode>> ScanAsync(
        IReadOnlyList<string> drives,
        IProgress<ScanProgress> progress,
        CancellationToken cancellationToken);
}
