using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using DiskScout.Helpers;
using DiskScout.Models;
using DiskScout.ViewModels.UninstallWizard;

namespace DiskScout.Services;

/// <summary>
/// Default <see cref="IUninstallReportService"/> implementation. Produces:
/// <list type="bullet">
///   <item>JSON: indented, camelCase, UTF-8.</item>
///   <item>HTML: dark-themed self-contained document with inline CSS, no external assets,
///   and ZERO script tags (defense-in-depth via runtime guard +
///   <see cref="WebUtility.HtmlEncode"/> on every user-supplied string).</item>
/// </list>
/// </summary>
public sealed class UninstallReportService : IUninstallReportService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Serilog.ILogger _logger;

    public UninstallReportService(Serilog.ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public UninstallReport BuildFromWizard(UninstallWizardViewModel wizard)
    {
        if (wizard is null) throw new ArgumentNullException(nameof(wizard));

        var byCat = wizard.AllResidueFindings
            .GroupBy(f => f.Category.ToString())
            .ToDictionary(
                g => g.Key,
                g => new CategoryTotals(g.Count(), g.Sum(x => x.SizeBytes)));

        var deletedEntries = (wizard.DeletionOutcome?.Entries ?? Array.Empty<DeletionEntry>())
            .Select(e => new DeletedEntrySnapshot(e.Path, e.Success, e.BytesFreed, e.Error))
            .ToList();

        var matchedRuleIds = wizard.MatchedRules.Select(m => m.Rule.Id).ToArray();
        bool hadTrace = wizard.Trace is not null;

        int residueCount = wizard.AllResidueFindings.Count;
        long residueBytes = wizard.AllResidueFindings.Sum(f => f.SizeBytes);

        int deletedSuccess = wizard.DeletionOutcome?.SuccessCount ?? 0;
        int deletedFailure = wizard.DeletionOutcome?.FailureCount ?? 0;
        long deletedBytes = wizard.DeletionOutcome?.TotalBytesFreed ?? 0;

        return new UninstallReport(
            ProgramName: wizard.Target.DisplayName ?? string.Empty,
            Publisher: wizard.Target.Publisher,
            Version: wizard.Target.Version,
            RegistryKey: wizard.Target.RegistryKey ?? string.Empty,
            GeneratedUtc: DateTime.UtcNow,
            UninstallOutcomeStatus: wizard.UninstallOutcome?.Status.ToString() ?? "NotRun",
            UninstallExitCode: wizard.UninstallOutcome?.ExitCode,
            UninstallElapsedSeconds: (wizard.UninstallOutcome?.Elapsed ?? TimeSpan.Zero).TotalSeconds,
            ResidueCount: residueCount,
            ResidueBytes: residueBytes,
            ResidueByCategory: byCat,
            DeletedSuccessCount: deletedSuccess,
            DeletedFailureCount: deletedFailure,
            DeletedBytesFreed: deletedBytes,
            DeletedEntries: deletedEntries,
            MatchedPublisherRuleIds: matchedRuleIds,
            HadInstallTrace: hadTrace);
    }

    public async Task ExportAsync(
        UninstallReport report,
        string outputPath,
        ReportFormat format,
        CancellationToken cancellationToken = default)
    {
        if (report is null) throw new ArgumentNullException(nameof(report));
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("outputPath must be non-empty", nameof(outputPath));

        // Best-effort directory creation — never fail export because of a benign mkdir hiccup.
        try
        {
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Could not pre-create directory for {Path}", outputPath);
        }

        if (format == ReportFormat.Json)
        {
            using var stream = File.Create(outputPath);
            await JsonSerializer.SerializeAsync(stream, report, JsonOpts, cancellationToken).ConfigureAwait(false);
            _logger.Information("Uninstall report (JSON) written to {Path}", outputPath);
            return;
        }

        // HTML branch
        var html = BuildHtml(report);

        // Defense-in-depth: never let a script tag opener slip through. Split the literal
        // so static analysis (and humans) can clearly see the intent.
        Debug.Assert(!html.Contains("<scr" + "ipt"),
            "Uninstall report HTML must never contain a <script> tag opener (XSS guard).");

        await File.WriteAllTextAsync(outputPath, html, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        _logger.Information("Uninstall report (HTML) written to {Path}", outputPath);
    }

    private static string BuildHtml(UninstallReport r)
    {
        var sb = new StringBuilder(8192);

        sb.Append("<!DOCTYPE html>\n");
        sb.Append("<html lang=\"fr\"><head><meta charset=\"UTF-8\"><title>DiskScout — rapport de désinstallation</title>\n");
        sb.Append("<style>\n");
        sb.Append("body { font-family: 'Segoe UI', sans-serif; background:#1e1e1e; color:#e0e0e0; max-width:900px; margin:24px auto; padding:0 16px; }\n");
        sb.Append("h1 { color:#ffaa00; }\n");
        sb.Append("h2 { color:#88c; border-bottom:1px solid #333; padding-bottom:4px; margin-top:24px; }\n");
        sb.Append("table { width:100%; border-collapse:collapse; margin:12px 0; }\n");
        sb.Append("th, td { padding:6px 10px; text-align:left; border-bottom:1px solid #333; vertical-align:top; }\n");
        sb.Append("th { background:#2a2a2a; }\n");
        sb.Append(".ok { color:#80ff80; }\n");
        sb.Append(".fail { color:#ff8080; }\n");
        sb.Append(".meta { color:#888; font-size:0.9em; }\n");
        sb.Append("</style></head><body>\n");

        sb.Append("<h1>Rapport de désinstallation</h1>\n");
        sb.Append("<p class=\"meta\">Généré le ").Append(WebUtility.HtmlEncode(r.GeneratedUtc.ToString("yyyy-MM-dd HH:mm:ss")));
        sb.Append(" UTC</p>\n");

        // Section: Identité du programme
        sb.Append("<h2>Identité du programme</h2>\n");
        sb.Append("<table>\n");
        AppendKeyValue(sb, "Nom", r.ProgramName);
        AppendKeyValue(sb, "Éditeur", r.Publisher ?? "—");
        AppendKeyValue(sb, "Version", r.Version ?? "—");
        AppendKeyValue(sb, "Clé registre", r.RegistryKey);
        AppendKeyValue(sb, "Généré", r.GeneratedUtc.ToString("u"));
        sb.Append("</table>\n");

        // Section: Désinstalleur natif
        sb.Append("<h2>Désinstalleur natif</h2>\n");
        sb.Append("<table>\n");
        AppendKeyValue(sb, "Statut", r.UninstallOutcomeStatus);
        AppendKeyValue(sb, "Code de sortie", r.UninstallExitCode?.ToString() ?? "—");
        AppendKeyValue(sb, "Durée (s)", r.UninstallElapsedSeconds.ToString("F2"));
        sb.Append("</table>\n");

        // Section: Résidus détectés
        sb.Append("<h2>Résidus détectés</h2>\n");
        sb.Append("<p>Total : ").Append(r.ResidueCount).Append(" éléments — ");
        sb.Append(WebUtility.HtmlEncode(ByteFormat.Fmt(r.ResidueBytes))).Append("</p>\n");
        sb.Append("<table>\n");
        sb.Append("<tr><th>Catégorie</th><th>Nombre</th><th>Taille</th></tr>\n");
        foreach (var kv in r.ResidueByCategory.OrderBy(k => k.Key))
        {
            sb.Append("<tr><td>").Append(WebUtility.HtmlEncode(kv.Key)).Append("</td>");
            sb.Append("<td>").Append(kv.Value.Count).Append("</td>");
            sb.Append("<td>").Append(WebUtility.HtmlEncode(ByteFormat.Fmt(kv.Value.Bytes))).Append("</td></tr>\n");
        }
        sb.Append("</table>\n");

        // Section: Résultat de la suppression
        sb.Append("<h2>Résultat de la suppression</h2>\n");
        sb.Append("<p>Succès : ").Append(r.DeletedSuccessCount);
        sb.Append(" — Échecs : ").Append(r.DeletedFailureCount);
        sb.Append(" — Espace libéré : ").Append(WebUtility.HtmlEncode(ByteFormat.Fmt(r.DeletedBytesFreed)));
        sb.Append("</p>\n");
        sb.Append("<table>\n");
        sb.Append("<tr><th>Chemin</th><th>Statut</th><th>Octets libérés</th><th>Erreur</th></tr>\n");
        foreach (var e in r.DeletedEntries)
        {
            var cls = e.Success ? "ok" : "fail";
            var statusText = e.Success ? "OK" : "Échec";
            sb.Append("<tr class=\"").Append(cls).Append("\">");
            sb.Append("<td>").Append(WebUtility.HtmlEncode(e.Path)).Append("</td>");
            sb.Append("<td>").Append(statusText).Append("</td>");
            sb.Append("<td>").Append(e.BytesFreed).Append("</td>");
            sb.Append("<td>").Append(WebUtility.HtmlEncode(e.Error ?? "")).Append("</td></tr>\n");
        }
        sb.Append("</table>\n");

        // Section: Règles éditeur appliquées
        sb.Append("<h2>Règles éditeur appliquées</h2>\n");
        if (r.MatchedPublisherRuleIds.Count > 0)
        {
            var joined = string.Join(", ", r.MatchedPublisherRuleIds);
            sb.Append("<p>").Append(WebUtility.HtmlEncode(joined)).Append("</p>\n");
        }
        else
        {
            sb.Append("<p class=\"meta\">Aucune règle éditeur ne correspond à ce programme.</p>\n");
        }

        // Section: Trace d'installation
        sb.Append("<h2>Trace d'installation</h2>\n");
        sb.Append("<p>").Append(r.HadInstallTrace ? "Oui" : "Non").Append("</p>\n");

        sb.Append("</body></html>\n");
        return sb.ToString();
    }

    private static void AppendKeyValue(StringBuilder sb, string key, string value)
    {
        sb.Append("<tr><th>").Append(WebUtility.HtmlEncode(key)).Append("</th>");
        sb.Append("<td>").Append(WebUtility.HtmlEncode(value)).Append("</td></tr>\n");
    }
}
