using DiskScout.Models;

namespace DiskScout.Services;

public interface IInstalledProgramsScanner
{
    Task<IReadOnlyList<InstalledProgram>> ScanAsync(
        IReadOnlyList<FileSystemNode> nodes,
        CancellationToken cancellationToken);
}
