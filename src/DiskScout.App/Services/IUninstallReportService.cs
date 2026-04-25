using DiskScout.Models;

namespace DiskScout.Services;

/// <summary>
/// Builds and persists the final HTML/JSON report at the end of the Uninstall Wizard flow.
/// Plan 09-06 deliverable.
/// </summary>
public interface IUninstallReportService
{
    /// <summary>
    /// Aggregates wizard state into a <see cref="UninstallReport"/> ready for serialization.
    /// </summary>
    UninstallReport BuildFromWizard(DiskScout.ViewModels.UninstallWizard.UninstallWizardViewModel wizard);

    /// <summary>
    /// Writes the report to <paramref name="outputPath"/> in the requested <paramref name="format"/>.
    /// HTML output is single-file (inline CSS, no external assets, no script tags).
    /// </summary>
    Task ExportAsync(UninstallReport report, string outputPath, ReportFormat format, CancellationToken cancellationToken = default);
}
