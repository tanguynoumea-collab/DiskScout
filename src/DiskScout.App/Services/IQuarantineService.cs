namespace DiskScout.Services;

public sealed record QuarantineEntry(
    string OriginalPath,
    string QuarantinePath,
    long BytesFreed,
    DateTime QuarantinedUtc,
    string SessionId)
{
    public int AgeDays => (int)(DateTime.UtcNow - QuarantinedUtc).TotalDays;
}

public sealed record QuarantineRestoreResult(
    IReadOnlyList<string> Restored,
    IReadOnlyList<(string Path, string Error)> Failures)
{
    public int Count => Restored.Count;
}

public interface IQuarantineService
{
    Task<IReadOnlyList<QuarantineEntry>> MoveToQuarantineAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default);

    Task<QuarantineRestoreResult> RestoreAsync(
        IReadOnlyList<QuarantineEntry> entries,
        CancellationToken cancellationToken = default);

    Task<long> PurgeAsync(TimeSpan olderThan, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<QuarantineEntry>> ListAsync(CancellationToken cancellationToken = default);
}
