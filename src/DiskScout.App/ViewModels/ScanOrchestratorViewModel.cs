using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskScout.Models;
using DiskScout.Services;
using Serilog;

namespace DiskScout.ViewModels;

public sealed partial class ScanOrchestratorViewModel : ObservableObject
{
    private readonly ILogger _logger;
    private readonly IDriveService _driveService;
    private readonly IFileSystemScanner _fileSystemScanner;
    private readonly IInstalledProgramsScanner _installedProgramsScanner;
    private readonly IOrphanDetectorService _orphanDetectorService;
    private readonly IPersistenceService _persistenceService;

    private CancellationTokenSource? _cts;

    public event EventHandler<ScanResult>? ScanCompleted;

    public ObservableCollection<DriveSelectionItemViewModel> Drives { get; } = new();

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _canCancel;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _progressText = "Prêt.";

    [ObservableProperty]
    private string _currentPath = string.Empty;

    [ObservableProperty]
    private long _filesProcessed;

    [ObservableProperty]
    private long _bytesScanned;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ScanOrchestratorViewModel(
        ILogger logger,
        IDriveService driveService,
        IFileSystemScanner fileSystemScanner,
        IInstalledProgramsScanner installedProgramsScanner,
        IOrphanDetectorService orphanDetectorService,
        IPersistenceService persistenceService)
    {
        _logger = logger;
        _driveService = driveService;
        _fileSystemScanner = fileSystemScanner;
        _installedProgramsScanner = installedProgramsScanner;
        _orphanDetectorService = orphanDetectorService;
        _persistenceService = persistenceService;

        RefreshDrives();
    }

    [RelayCommand]
    private void RefreshDrives()
    {
        Drives.Clear();
        foreach (var drive in _driveService.GetFixedDrives())
        {
            if (!drive.IsReady) continue;
            var sizeGb = drive.TotalSizeBytes / (1024.0 * 1024.0 * 1024.0);
            var freeGb = drive.FreeSpaceBytes / (1024.0 * 1024.0 * 1024.0);
            var label = string.IsNullOrWhiteSpace(drive.Label) ? "(sans label)" : drive.Label;
            var display = $"{drive.RootPath.TrimEnd('\\')}  {label}  {freeGb:F1} Go libres sur {sizeGb:F1} Go  [{drive.Format}]";
            Drives.Add(new DriveSelectionItemViewModel(drive.RootPath, display, selectedByDefault: true));
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartScan))]
    private async Task StartScanAsync()
    {
        if (IsScanning) return;

        var selected = Drives.Where(d => d.IsSelected).Select(d => d.RootPath).ToArray();
        if (selected.Length == 0)
        {
            StatusMessage = "Sélectionne au moins un disque.";
            return;
        }

        IsScanning = true;
        CanCancel = true;
        ProgressPercent = 0;
        ProgressText = "Scan en cours...";
        StatusMessage = string.Empty;
        StartScanCommand.NotifyCanExecuteChanged();
        CancelScanCommand.NotifyCanExecuteChanged();

        _cts = new CancellationTokenSource();
        IProgress<ScanProgress> progress = new Progress<ScanProgress>(OnProgress);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.Information("Starting scan on {Drives}", string.Join(", ", selected));

            var nodes = await _fileSystemScanner.ScanAsync(selected, progress, _cts.Token).ConfigureAwait(false);

            progress.Report(new ScanProgress(FilesProcessed, BytesScanned, "Lecture du registre...", null, ScanPhase.ReadingRegistry));
            var programs = await _installedProgramsScanner.ScanAsync(nodes, _cts.Token).ConfigureAwait(false);

            progress.Report(new ScanProgress(FilesProcessed, BytesScanned, "Détection des rémanents...", null, ScanPhase.DetectingOrphans));
            var orphans = await _orphanDetectorService.DetectAsync(nodes, programs, _cts.Token).ConfigureAwait(false);

            stopwatch.Stop();

            var result = new ScanResult(
                SchemaVersion: ScanResult.CurrentSchemaVersion,
                ScanId: Guid.NewGuid().ToString("N"),
                StartedUtc: DateTime.UtcNow - stopwatch.Elapsed,
                CompletedUtc: DateTime.UtcNow,
                ScannedDrives: selected,
                Nodes: nodes,
                Programs: programs,
                Orphans: orphans);

            try
            {
                var savePath = await _persistenceService.SaveAsync(result, _cts.Token).ConfigureAwait(false);
                _logger.Information("Scan saved to {Path}", savePath);
            }
            catch (NotImplementedException)
            {
                _logger.Information("Persistence not yet wired; skipping save.");
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Persistence failed.");
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                ScanCompleted?.Invoke(this, result);
                StatusMessage = $"Scan terminé en {stopwatch.Elapsed:mm\\:ss} — {nodes.Count:n0} nœuds, {programs.Count:n0} programmes, {orphans.Count:n0} rémanents.";
                ProgressText = "Terminé.";
                ProgressPercent = 100;
            });
        }
        catch (OperationCanceledException)
        {
            _logger.Information("Scan cancelled by user.");
            StatusMessage = "Scan annulé.";
            ProgressText = "Annulé.";
            ProgressPercent = 0;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Scan failed.");
            StatusMessage = $"Scan échoué : {ex.Message}";
            ProgressText = "Erreur.";
        }
        finally
        {
            IsScanning = false;
            CanCancel = false;
            _cts?.Dispose();
            _cts = null;
            StartScanCommand.NotifyCanExecuteChanged();
            CancelScanCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanStartScan() => !IsScanning;

    [RelayCommand(CanExecute = nameof(CanCancelScan))]
    private void CancelScan()
    {
        if (_cts is { IsCancellationRequested: false }) _cts.Cancel();
        CanCancel = false;
        ProgressText = "Annulation...";
    }

    private bool CanCancelScan() => CanCancel;

    private void OnProgress(ScanProgress p)
    {
        FilesProcessed = p.FilesProcessed;
        BytesScanned = p.BytesScanned;
        CurrentPath = p.CurrentPath;
        ProgressText = p.Phase switch
        {
            ScanPhase.EnumeratingDrives => "Enumération des disques...",
            ScanPhase.ScanningFilesystem => $"{p.FilesProcessed:n0} fichiers, {p.BytesScanned / (1024.0 * 1024.0 * 1024.0):F2} Go",
            ScanPhase.ReadingRegistry => "Lecture du registre...",
            ScanPhase.DetectingOrphans => "Détection des rémanents...",
            ScanPhase.Finalizing => "Finalisation...",
            ScanPhase.Completed => "Terminé.",
            ScanPhase.Cancelling => "Annulation...",
            _ => ProgressText,
        };
        ProgressPercent = p.PercentComplete.HasValue ? p.PercentComplete.Value * 100.0 : ProgressPercent;
    }
}
