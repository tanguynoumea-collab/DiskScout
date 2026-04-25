using DiskScout.Models;

namespace DiskScout.Services;

/// <summary>
/// Post-uninstall residue scanner. Aligned with CONTEXT.md D-02 ("Revo Pro level"):
/// scans seven surfaces (registry, filesystem, shortcuts, MSI patches, services, scheduled tasks,
/// shell extensions) for artifacts left behind by a uninstalled program.
/// </summary>
/// <remarks>
/// Honors CONTEXT.md D-03: this scanner produces findings only — deletion happens in Plan 09-05.
/// Every emitted <see cref="ResidueFinding"/> is filtered through
/// <see cref="DiskScout.Helpers.ResiduePathSafety.IsSafeToPropose"/> before being returned.
/// </remarks>
public interface IResidueScanner
{
    /// <summary>
    /// Scan all seven residue surfaces for the given target.
    /// </summary>
    /// <param name="target">Identity of the program (Publisher + DisplayName + optional InstallLocation).</param>
    /// <param name="installTrace">
    /// Optional InstallTrace from Plan 09-01. When provided, the scanner emits
    /// <see cref="ResidueSource.TraceMatch"/> findings (highest trust) for trace events
    /// whose paths still exist on disk after the native uninstaller has run.
    /// </param>
    /// <param name="progress">Progress reporter; receives the current scan area name.</param>
    /// <param name="cancellationToken">Cancellation token honored within ~2 seconds across every scan branch.</param>
    /// <returns>Read-only list of findings, deduplicated by Category+Path, all whitelist-filtered.</returns>
    Task<IReadOnlyList<ResidueFinding>> ScanAsync(
        ResidueScanTarget target,
        InstallTrace? installTrace,
        IProgress<string>? progress,
        CancellationToken cancellationToken);
}
