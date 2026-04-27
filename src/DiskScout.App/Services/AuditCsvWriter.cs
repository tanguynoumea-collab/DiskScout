using System.Globalization;
using System.IO;
using System.Text;
using DiskScout.Helpers;
using DiskScout.Models;
using Serilog;

namespace DiskScout.Services;

/// <summary>
/// Default <see cref="IAuditCsvWriter"/>. Manual CSV emission (no CsvHelper
/// dependency added — keeps the single-file binary footprint minimal,
/// honoring CLAUDE.md / STACK.md "no new dependency" guidance for a small
/// fixed-schema export).
///
/// Format: UTF-8 with BOM (Excel reads it cleanly without import wizard),
/// CRLF line endings, RFC-4180 quoting for fields containing comma / quote /
/// newline. Header is the literal:
/// <c>Path,SizeBytes,ConfidenceScore,RiskLevel,TriggeredRules,ExonerationRules,RecommendedAction</c>.
/// </summary>
/// <remarks>
/// <para>
/// "Exoneration rules" is the semicolon-joined list of <c>MatcherHit.Source:Evidence</c>
/// pairs — matchers REDUCE the base 100 confidence score (they're the reasons the
/// candidate is NOT a residue, hence "exoneration"). "Triggered rules" is the
/// semicolon-joined list of <c>RuleHit.RuleId</c> values from the PathRuleEngine.
/// </para>
/// <para>
/// NO File.Delete or Directory.Delete is ever called. The writer creates the
/// audit folder via <see cref="Directory.CreateDirectory(string)"/> if missing
/// (already idempotent in <see cref="AppPaths.AuditFolder"/>).
/// </para>
/// </remarks>
public sealed class AuditCsvWriter : IAuditCsvWriter
{
    /// <summary>Header row literal — pinned by acceptance tests.</summary>
    public const string HeaderLine =
        "Path,SizeBytes,ConfidenceScore,RiskLevel,TriggeredRules,ExonerationRules,RecommendedAction";

    private readonly ILogger _logger;
    private readonly Func<DateTime> _nowProvider;

    public AuditCsvWriter(ILogger logger)
        : this(logger, () => DateTime.Now) { }

    /// <summary>Test seam: lets unit tests pin the timestamp portion of the filename.</summary>
    internal AuditCsvWriter(ILogger logger, Func<DateTime> nowProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _nowProvider = nowProvider ?? throw new ArgumentNullException(nameof(nowProvider));
    }

    public async Task<string> WriteAsync(
        IEnumerable<AppDataOrphanCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        if (candidates is null) throw new ArgumentNullException(nameof(candidates));

        // AppPaths.AuditFolder calls Directory.CreateDirectory internally — no
        // additional create needed. Keeping the explicit call here as belt-and-
        // suspenders for the contract documented in the interface.
        var folder = AppPaths.AuditFolder;
        Directory.CreateDirectory(folder);

        var stamp = _nowProvider().ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var fileName = $"audit_{stamp}.csv";
        var fullPath = Path.Combine(folder, fileName);

        // UTF-8 WITH BOM — passing `new UTF8Encoding(true)` guarantees the BOM
        // (the static `Encoding.UTF8` already includes BOM but being explicit
        // future-proofs against shared-state changes).
        await using var stream = new FileStream(
            fullPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(true))
        {
            NewLine = "\r\n",
        };

        await writer.WriteLineAsync(HeaderLine.AsMemory(), cancellationToken).ConfigureAwait(false);

        int rowCount = 0;
        foreach (var c in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (c is null) continue;

            var triggered = string.Join(";", c.TriggeredRules.Select(r => r.RuleId));
            var exoneration = string.Join(";", c.MatchedSources.Select(m => $"{m.Source}:{m.Evidence}"));

            // Build row — invariant culture for SizeBytes / ConfidenceScore
            // (Excel locale handling on French + comma decimal separator
            // would otherwise mangle large size numbers).
            var row = string.Join(",",
                EscapeCsv(c.FullPath ?? string.Empty),
                c.SizeBytes.ToString(CultureInfo.InvariantCulture),
                c.ConfidenceScore.ToString(CultureInfo.InvariantCulture),
                c.Risk.ToString(),
                EscapeCsv(triggered),
                EscapeCsv(exoneration),
                c.Action.ToString());

            await writer.WriteLineAsync(row.AsMemory(), cancellationToken).ConfigureAwait(false);
            rowCount++;
        }

        await writer.FlushAsync().ConfigureAwait(false);
        _logger.Information("AuditCsvWriter: wrote {Rows} rows to {Path}", rowCount, fullPath);
        return fullPath;
    }

    /// <summary>
    /// RFC-4180 minimal escape: if the field contains comma / double-quote /
    /// CR / LF, wrap in double-quotes and double any embedded double-quote.
    /// Empty / non-special strings return as-is.
    /// </summary>
    internal static string EscapeCsv(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        bool needsQuoting =
            s.Contains(',') || s.Contains('"') || s.Contains('\r') || s.Contains('\n');
        if (!needsQuoting) return s;
        var escaped = s.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }
}
