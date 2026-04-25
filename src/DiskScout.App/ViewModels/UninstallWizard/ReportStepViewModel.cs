using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskScout.Models;
using DiskScout.Services;

namespace DiskScout.ViewModels.UninstallWizard;

/// <summary>
/// Step 6 — final report. Built from the wizard state via <see cref="IUninstallReportService.BuildFromWizard"/>
/// and exportable as JSON or HTML through <see cref="IUninstallReportService.ExportAsync"/>.
///
/// The user selects the destination file via WPF's <c>SaveFileDialog</c>; the service writes the
/// document to disk. Status feedback (success / error) is surfaced via <see cref="ExportStatus"/>.
/// </summary>
public sealed partial class ReportStepViewModel : ObservableObject
{
    private readonly UninstallWizardViewModel _wizard;
    private readonly IUninstallReportService _reportService;
    private readonly Serilog.ILogger _logger;

    [ObservableProperty]
    private UninstallReport? _report;

    [ObservableProperty]
    private string _exportStatus = "";

    public ReportStepViewModel(
        UninstallWizardViewModel wizard,
        IUninstallReportService reportService,
        Serilog.ILogger logger)
    {
        _wizard = wizard ?? throw new ArgumentNullException(nameof(wizard));
        _reportService = reportService ?? throw new ArgumentNullException(nameof(reportService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Report = _reportService.BuildFromWizard(_wizard);
    }

    [RelayCommand]
    private async Task ExportJsonAsync()
    {
        if (Report is null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exporter le rapport JSON",
            FileName = $"DiskScout_uninstall_{SanitizeFileName(Report.ProgramName)}_{Report.GeneratedUtc:yyyyMMdd_HHmm}.json",
            Filter = "Rapport JSON (*.json)|*.json",
            DefaultExt = ".json",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            await _reportService.ExportAsync(Report, dlg.FileName, ReportFormat.Json);
            ExportStatus = $"Exporté : {dlg.FileName}";
            _logger.Information("Uninstall report (JSON) exported to {Path}", dlg.FileName);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Report export (JSON) failed");
            ExportStatus = $"Échec : {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportHtmlAsync()
    {
        if (Report is null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exporter le rapport HTML",
            FileName = $"DiskScout_uninstall_{SanitizeFileName(Report.ProgramName)}_{Report.GeneratedUtc:yyyyMMdd_HHmm}.html",
            Filter = "Rapport HTML (*.html)|*.html",
            DefaultExt = ".html",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            await _reportService.ExportAsync(Report, dlg.FileName, ReportFormat.Html);
            ExportStatus = $"Exporté : {dlg.FileName}";
            _logger.Information("Uninstall report (HTML) exported to {Path}", dlg.FileName);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Report export (HTML) failed");
            ExportStatus = $"Échec : {ex.Message}";
        }
    }

    [RelayCommand]
    private void Close() => _wizard.CloseCommand.Execute(null);

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "report";
        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
