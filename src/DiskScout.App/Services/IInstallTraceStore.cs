using DiskScout.Models;

namespace DiskScout.Services;

public interface IInstallTraceStore
{
    Task SaveAsync(InstallTrace trace, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InstallTraceHeader>> ListAsync(CancellationToken cancellationToken = default);
    Task<InstallTrace?> LoadAsync(string traceId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string traceId, CancellationToken cancellationToken = default);
}
