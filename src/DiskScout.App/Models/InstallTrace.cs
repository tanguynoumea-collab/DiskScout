namespace DiskScout.Models;

public enum InstallTraceEventKind
{
    FileCreated,
    FileModified,
    DirectoryCreated,
    RegistryKeyCreated,
    RegistryValueWritten,
}

public sealed record InstallTraceEvent(
    InstallTraceEventKind Kind,
    string Path,
    DateTime UtcWhen);

public sealed record InstallTraceHeader(
    string TraceId,
    string TrackerVersion,
    DateTime StartedUtc,
    DateTime StoppedUtc,
    string? InstallerCommandLine,
    string? InstallerProductHint);

public sealed record InstallTrace(
    InstallTraceHeader Header,
    IReadOnlyList<InstallTraceEvent> Events);
