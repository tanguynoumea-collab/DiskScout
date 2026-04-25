namespace DiskScout.Models;

/// <summary>
/// Output format for the final uninstall report.
/// </summary>
public enum ReportFormat { Json, Html }

/// <summary>
/// Per-category aggregation in the final report (count of findings + total bytes).
/// </summary>
public sealed record CategoryTotals(int Count, long Bytes);

/// <summary>
/// Snapshot of one entry that was processed during the deletion phase.
/// Captures Path/Success/BytesFreed/Error so the report can show per-path outcome.
/// </summary>
public sealed record DeletedEntrySnapshot(string Path, bool Success, long BytesFreed, string? Error);

/// <summary>
/// Final, self-contained snapshot of an uninstall wizard run. Built from the
/// <see cref="DiskScout.ViewModels.UninstallWizard.UninstallWizardViewModel"/> at the end of
/// the wizard and serialized to JSON or HTML by <see cref="DiskScout.Services.IUninstallReportService"/>.
/// </summary>
public sealed record UninstallReport(
    string ProgramName,
    string? Publisher,
    string? Version,
    string RegistryKey,
    DateTime GeneratedUtc,
    string UninstallOutcomeStatus,
    int? UninstallExitCode,
    double UninstallElapsedSeconds,
    int ResidueCount,
    long ResidueBytes,
    Dictionary<string, CategoryTotals> ResidueByCategory,
    int DeletedSuccessCount,
    int DeletedFailureCount,
    long DeletedBytesFreed,
    IReadOnlyList<DeletedEntrySnapshot> DeletedEntries,
    IReadOnlyList<string> MatchedPublisherRuleIds,
    bool HadInstallTrace);
