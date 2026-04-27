using DiskScout.Models;

namespace DiskScout.Services;

/// <summary>
/// Writes a CSV audit of <see cref="AppDataOrphanCandidate"/> records produced by
/// the Phase-10 AppData orphan detection pipeline. Output schema is fixed at:
/// <c>Path,SizeBytes,ConfidenceScore,RiskLevel,TriggeredRules,ExonerationRules,RecommendedAction</c>.
/// File is written as UTF-8 with BOM (Excel-friendly), CRLF line endings, RFC-4180
/// quoting on fields containing comma / quote / newline.
///
/// File path = <c>%LocalAppData%\DiskScout\audits\audit_YYYYMMDD_HHmmss.csv</c>.
/// Used by the <c>--audit</c> CLI mode in App.xaml.cs (Plan 10-05) for repeatable
/// offline review of detection precision on any machine.
/// </summary>
public interface IAuditCsvWriter
{
    /// <summary>
    /// Write <paramref name="candidates"/> to a fresh CSV under
    /// <see cref="DiskScout.Helpers.AppPaths.AuditFolder"/>. Returns the
    /// absolute path to the written file. Creates the audit folder if missing
    /// (Directory.Create — no Directory.Delete is ever called).
    /// </summary>
    Task<string> WriteAsync(
        IEnumerable<AppDataOrphanCandidate> candidates,
        CancellationToken cancellationToken = default);
}
