using DiskScout.Models;

namespace DiskScout.Services.Stubs;

public sealed class StubFileSystemScanner : IFileSystemScanner
{
    public Task<IReadOnlyList<FileSystemNode>> ScanAsync(
        IReadOnlyList<string> drives,
        IProgress<ScanProgress> progress,
        CancellationToken cancellationToken)
    {
        progress.Report(new ScanProgress(0, 0, string.Empty, null, ScanPhase.Idle));
        return Task.FromResult<IReadOnlyList<FileSystemNode>>(Array.Empty<FileSystemNode>());
    }
}

public sealed class StubInstalledProgramsScanner : IInstalledProgramsScanner
{
    public Task<IReadOnlyList<InstalledProgram>> ScanAsync(
        IReadOnlyList<FileSystemNode> nodes,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<InstalledProgram>>(Array.Empty<InstalledProgram>());
}

public sealed class StubOrphanDetectorService : IOrphanDetectorService
{
    public Task<IReadOnlyList<OrphanCandidate>> DetectAsync(
        IReadOnlyList<FileSystemNode> nodes,
        IReadOnlyList<InstalledProgram> programs,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<OrphanCandidate>>(Array.Empty<OrphanCandidate>());
}

public sealed class StubPersistenceService : IPersistenceService
{
    public Task<string> SaveAsync(ScanResult result, CancellationToken cancellationToken) =>
        throw new NotImplementedException("Persistence arrives in Phase 6.");

    public Task<ScanResult?> LoadAsync(string filePath, CancellationToken cancellationToken) =>
        throw new NotImplementedException("Persistence arrives in Phase 6.");

    public Task<IReadOnlyList<ScanHistoryEntry>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ScanHistoryEntry>>(Array.Empty<ScanHistoryEntry>());
}

public sealed class StubDeltaComparator : IDeltaComparator
{
    public DeltaResult Compare(ScanResult before, ScanResult after) =>
        throw new NotImplementedException("Delta arrives in Phase 7.");
}

public sealed class StubExporter : IExporter
{
    public Task ExportAsync(
        ExportPane pane,
        ExportFormat format,
        string destinationPath,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException("Export arrives in Phase 8.");
}
