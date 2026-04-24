using System.IO;
using System.Text;
using DiskScout.Models;

namespace DiskScout.Services;

public sealed class CsvHtmlExporter : IExporter
{
    private readonly Func<ScanResult?> _currentScanProvider;

    public CsvHtmlExporter(Func<ScanResult?> currentScanProvider)
    {
        _currentScanProvider = currentScanProvider;
    }

    public async Task ExportAsync(ExportPane pane, ExportFormat format, string destinationPath, CancellationToken cancellationToken)
    {
        var scan = _currentScanProvider();
        if (scan is null) throw new InvalidOperationException("Aucun scan à exporter.");

        var content = format switch
        {
            ExportFormat.Csv => BuildCsv(pane, scan),
            ExportFormat.Html => BuildHtml(pane, scan),
            _ => throw new NotSupportedException($"Format {format} non pris en charge."),
        };

        await File.WriteAllTextAsync(destinationPath, content, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildCsv(ExportPane pane, ScanResult scan)
    {
        var sb = new StringBuilder();
        switch (pane)
        {
            case ExportPane.Programs:
                sb.AppendLine("Nom,Éditeur,Version,InstallDate,Chemin,TailleOctets,Hive");
                foreach (var p in scan.Programs)
                {
                    sb.Append(Csv(p.DisplayName)).Append(',');
                    sb.Append(Csv(p.Publisher)).Append(',');
                    sb.Append(Csv(p.Version)).Append(',');
                    sb.Append(Csv(p.InstallDate?.ToString("yyyy-MM-dd"))).Append(',');
                    sb.Append(Csv(p.InstallLocation)).Append(',');
                    var size = p.ComputedSizeBytes > 0 ? p.ComputedSizeBytes : p.RegistryEstimatedSizeBytes;
                    sb.Append(size).Append(',');
                    sb.AppendLine(p.Hive.ToString());
                }
                break;
            case ExportPane.Orphans:
                sb.AppendLine("Catégorie,Chemin,TailleOctets,Raison");
                foreach (var o in scan.Orphans)
                {
                    sb.Append(o.Category).Append(',');
                    sb.Append(Csv(o.FullPath)).Append(',');
                    sb.Append(o.SizeBytes).Append(',');
                    sb.AppendLine(Csv(o.Reason));
                }
                break;
            case ExportPane.Tree:
                sb.AppendLine("Id,ParentId,Nom,Chemin,Kind,TailleOctets,Depth,IsReparsePoint");
                foreach (var n in scan.Nodes)
                {
                    sb.Append(n.Id).Append(',');
                    sb.Append(n.ParentId?.ToString() ?? "").Append(',');
                    sb.Append(Csv(n.Name)).Append(',');
                    sb.Append(Csv(n.FullPath)).Append(',');
                    sb.Append(n.Kind).Append(',');
                    sb.Append(n.SizeBytes).Append(',');
                    sb.Append(n.Depth).Append(',');
                    sb.AppendLine(n.IsReparsePoint ? "1" : "0");
                }
                break;
        }
        return sb.ToString();
    }

    private static string BuildHtml(ExportPane pane, ScanResult scan)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang='fr'><head><meta charset='utf-8'>");
        sb.AppendLine($"<title>DiskScout — Export {pane}</title>");
        sb.AppendLine("<style>body{font-family:Segoe UI,sans-serif;background:#1a1b1e;color:#e6e6e6;margin:24px}table{border-collapse:collapse;width:100%}th,td{border-bottom:1px solid #3a3c42;padding:6px 10px;text-align:left}th{color:#4c9aff}tr:nth-child(even){background:#25272b}</style>");
        sb.AppendLine("</head><body>");
        sb.AppendLine($"<h1>DiskScout — Export {pane}</h1>");
        sb.AppendLine($"<p>Scan {scan.ScanId} — {scan.CompletedUtc:yyyy-MM-dd HH:mm:ss UTC}</p>");
        sb.AppendLine(BuildCsv(pane, scan).Replace(",", "</td><td>").Replace("\r\n", "</td></tr>\n<tr><td>"));
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static string Csv(string? v)
    {
        if (v is null) return string.Empty;
        if (v.Contains(',') || v.Contains('"') || v.Contains('\n'))
        {
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        }
        return v;
    }
}
