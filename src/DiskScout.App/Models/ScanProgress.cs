namespace DiskScout.Models;

public readonly record struct ScanProgress(
    long FilesProcessed,
    long BytesScanned,
    string CurrentPath,
    double? PercentComplete,
    ScanPhase Phase);

public enum ScanPhase
{
    Idle,
    EnumeratingDrives,
    ScanningFilesystem,
    ReadingRegistry,
    DetectingOrphans,
    Finalizing,
    Completed,
    Cancelling,
    Cancelled,
    Faulted,
}
