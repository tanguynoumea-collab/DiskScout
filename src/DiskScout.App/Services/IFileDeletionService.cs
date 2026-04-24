using DiskScout.Helpers;

namespace DiskScout.Services;

public sealed record DeletionEntry(string Path, bool Success, long BytesFreed, string? Error);

public sealed record DeletionResult(IReadOnlyList<DeletionEntry> Entries)
{
    public int SuccessCount => Entries.Count(e => e.Success);
    public int FailureCount => Entries.Count(e => !e.Success);
    public long TotalBytesFreed => Entries.Where(e => e.Success).Sum(e => e.BytesFreed);
}

public interface IFileDeletionService
{
    /// <summary>
    /// Delete a batch of paths using the chosen mode.
    /// DiskScoutQuarantine → moved to managed quarantine dir (restorable 30 days)
    /// RecycleBin → Windows Recycle Bin (reversible via explorer)
    /// Permanent → irreversible
    /// </summary>
    Task<DeletionResult> DeleteAsync(
        IReadOnlyList<string> paths,
        DeleteMode mode,
        CancellationToken cancellationToken = default);
}
