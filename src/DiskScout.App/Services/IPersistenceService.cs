using DiskScout.Models;

namespace DiskScout.Services;

public interface IPersistenceService
{
    Task<string> SaveAsync(ScanResult result, CancellationToken cancellationToken);
    Task<ScanResult?> LoadAsync(string filePath, CancellationToken cancellationToken);
    Task<IReadOnlyList<ScanHistoryEntry>> ListAsync(CancellationToken cancellationToken);
}

public sealed record ScanHistoryEntry(
    string FilePath,
    string ScanId,
    DateTime CompletedUtc,
    IReadOnlyList<string> ScannedDrives,
    long TotalBytes);
