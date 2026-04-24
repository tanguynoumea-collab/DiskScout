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
    Task<DeletionResult> DeleteAsync(
        IReadOnlyList<string> paths,
        bool sendToRecycleBin,
        CancellationToken cancellationToken = default);
}
