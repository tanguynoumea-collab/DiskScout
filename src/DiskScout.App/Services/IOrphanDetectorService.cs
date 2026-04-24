using DiskScout.Models;

namespace DiskScout.Services;

public interface IOrphanDetectorService
{
    Task<IReadOnlyList<OrphanCandidate>> DetectAsync(
        IReadOnlyList<FileSystemNode> nodes,
        IReadOnlyList<InstalledProgram> programs,
        CancellationToken cancellationToken);
}
