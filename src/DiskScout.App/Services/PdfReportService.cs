using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using DiskScout.Models;
using Serilog;

namespace DiskScout.Services;

public interface IPdfReportService
{
    Task<string> GenerateAsync(
        ScanResult result,
        int healthScore,
        string healthGrade,
        string summary,
        long remnantsBytes,
        long cleanupBytes,
        long duplicatesBytes,
        long oldFilesBytes,
        string destinationPath,
        CancellationToken cancellationToken = default);
}

public sealed class PdfReportService : IPdfReportService
{
    private readonly ILogger _logger;

    static PdfReportService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public PdfReportService(ILogger logger)
    {
        _logger = logger;
    }

    public Task<string> GenerateAsync(
        ScanResult result,
        int healthScore,
        string healthGrade,
        string summary,
        long remnantsBytes,
        long cleanupBytes,
        long duplicatesBytes,
        long oldFilesBytes,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var totalBytes = result.Nodes.Where(n => n.Kind == FileSystemNodeKind.File).Sum(n => n.SizeBytes);

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.DefaultTextStyle(t => t.FontFamily("Segoe UI"));

                    page.Header().Column(col =>
                    {
                        col.Item().Text("DiskScout — rapport de scan").FontSize(20).Bold();
                        col.Item().Text(
                            $"{result.CompletedUtc.ToLocalTime():yyyy-MM-dd HH:mm} — disques : {string.Join(", ", result.ScannedDrives)}"
                        ).FontSize(10).FontColor(Colors.Grey.Medium);
                    });

                    page.Content().PaddingVertical(15).Column(col =>
                    {
                        // Health summary
                        col.Item().Background(Colors.Grey.Lighten3).Padding(12).Row(row =>
                        {
                            row.ConstantItem(80).Text($"{healthGrade}").FontSize(40).Bold().FontColor(GradeColor(healthScore));
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text($"Score : {healthScore}/100").FontSize(16).Bold();
                                c.Item().Text(summary).FontSize(10);
                            });
                        });

                        col.Spacing(12);

                        // Key metrics table
                        col.Item().Text("Synthèse").FontSize(14).Bold();
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn();
                                c.ConstantColumn(100);
                            });
                            AddRow(table, "Total scanné",    FormatBytes(totalBytes));
                            AddRow(table, "Programmes",     $"{result.Programs.Count:n0}");
                            AddRow(table, "Rémanents",       FormatBytes(remnantsBytes));
                            AddRow(table, "Nettoyage",      FormatBytes(cleanupBytes));
                            AddRow(table, "Doublons",        FormatBytes(duplicatesBytes));
                            AddRow(table, "Vieux fichiers",  FormatBytes(oldFilesBytes));
                            AddRow(table, "Potentiel libérable", FormatBytes(remnantsBytes + cleanupBytes + duplicatesBytes));
                        });

                        col.Spacing(12);

                        // Top 20 biggest files
                        col.Item().Text("Top 20 plus gros fichiers").FontSize(14).Bold();
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(3);
                                c.ConstantColumn(80);
                            });
                            table.Header(h =>
                            {
                                h.Cell().Background(Colors.Grey.Lighten2).Padding(3).Text("Chemin").Bold();
                                h.Cell().Background(Colors.Grey.Lighten2).Padding(3).AlignRight().Text("Taille").Bold();
                            });
                            foreach (var n in result.Nodes
                                .Where(x => x.Kind == FileSystemNodeKind.File && x.SizeBytes > 0)
                                .OrderByDescending(x => x.SizeBytes)
                                .Take(20))
                            {
                                table.Cell().Padding(2).Text(n.FullPath).FontSize(9);
                                table.Cell().Padding(2).AlignRight().Text(FormatBytes(n.SizeBytes)).FontSize(9);
                            }
                        });

                        col.Spacing(12);

                        // Top 10 programs by size
                        col.Item().Text("Top 10 programmes (taille réelle)").FontSize(14).Bold();
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(2);
                                c.RelativeColumn(1);
                                c.ConstantColumn(80);
                            });
                            table.Header(h =>
                            {
                                h.Cell().Background(Colors.Grey.Lighten2).Padding(3).Text("Nom").Bold();
                                h.Cell().Background(Colors.Grey.Lighten2).Padding(3).Text("Éditeur").Bold();
                                h.Cell().Background(Colors.Grey.Lighten2).Padding(3).AlignRight().Text("Taille").Bold();
                            });
                            foreach (var p in result.Programs
                                .OrderByDescending(x => x.ComputedSizeBytes > 0 ? x.ComputedSizeBytes : x.RegistryEstimatedSizeBytes)
                                .Take(10))
                            {
                                var size = p.ComputedSizeBytes > 0 ? p.ComputedSizeBytes : p.RegistryEstimatedSizeBytes;
                                table.Cell().Padding(2).Text(p.DisplayName ?? "").FontSize(9);
                                table.Cell().Padding(2).Text(p.Publisher ?? "").FontSize(9);
                                table.Cell().Padding(2).AlignRight().Text(FormatBytes(size)).FontSize(9);
                            }
                        });
                    });

                    page.Footer().AlignCenter().Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(9).FontColor(Colors.Grey.Medium));
                        t.Span("DiskScout  —  page ");
                        t.CurrentPageNumber();
                        t.Span(" / ");
                        t.TotalPages();
                    });
                });
            }).GeneratePdf(destinationPath);

            _logger.Information("PDF report generated: {Path}", destinationPath);
            return destinationPath;
        }, cancellationToken);
    }

    private static void AddRow(TableDescriptor table, string label, string value)
    {
        table.Cell().Padding(2).Text(label).FontSize(10);
        table.Cell().Padding(2).AlignRight().Text(value).FontSize(10).Bold();
    }

    private static string GradeColor(int score) => score switch
    {
        >= 90 => Colors.Green.Darken1,
        >= 80 => Colors.LightGreen.Darken2,
        >= 70 => Colors.Yellow.Darken2,
        >= 60 => Colors.Orange.Darken2,
        _     => Colors.Red.Darken2,
    };

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 o";
        string[] u = { "o", "Ko", "Mo", "Go", "To" };
        double v = bytes;
        int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:F1} {u[i]}";
    }
}
