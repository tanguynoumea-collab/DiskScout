namespace DiskScout.Models;

public enum DeltaChange
{
    Added,
    Removed,
    Grew,
    Shrank,
    Unchanged,
}

public sealed record DeltaEntry(
    string FullPath,
    DeltaChange Change,
    long? SizeBeforeBytes,
    long? SizeAfterBytes,
    long DeltaBytes,
    double? PercentChange);

public sealed record DeltaResult(
    string BeforeScanId,
    string AfterScanId,
    DateTime BeforeCompletedUtc,
    DateTime AfterCompletedUtc,
    IReadOnlyList<DeltaEntry> Entries);
