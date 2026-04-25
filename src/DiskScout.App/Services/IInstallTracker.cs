using DiskScout.Models;

namespace DiskScout.Services;

public interface IInstallTracker
{
    bool IsTracking { get; }
    Task StartAsync(string? installerCommandLine, string? installerProductHint, CancellationToken cancellationToken = default);
    Task<InstallTrace> StopAsync(CancellationToken cancellationToken = default);
}
