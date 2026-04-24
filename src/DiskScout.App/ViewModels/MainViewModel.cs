using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskScout.Models;
using DiskScout.Services;
using Serilog;

namespace DiskScout.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ILogger _logger;

    public HealthViewModel Health { get; }
    public ProgramsViewModel Programs { get; }
    public OrphansViewModel Orphans { get; }
    public OrphansViewModel Cleanup { get; }
    public TreeViewModel Tree { get; }
    public TreeMapViewModel TreeMap { get; }
    public LargestFilesViewModel LargestFiles { get; }
    public ExtensionsViewModel Extensions { get; }
    public DuplicatesViewModel Duplicates { get; }
    public OldFilesViewModel OldFiles { get; }
    public QuarantineViewModel Quarantine { get; }
    public ScanOrchestratorViewModel Orchestrator { get; }

    private readonly IDriveService _driveService;
    private readonly IPdfReportService _pdfReport;
    private ScanResult? _lastResult;

    [ObservableProperty]
    private string _windowTitle = "DiskScout [Admin]";

    [ObservableProperty]
    private int _selectedTabIndex;

    public MainViewModel(
        ILogger logger,
        IDriveService driveService,
        IFileSystemScanner fileSystemScanner,
        IInstalledProgramsScanner installedProgramsScanner,
        IOrphanDetectorService orphanDetectorService,
        IPersistenceService persistenceService,
        IDeltaComparator deltaComparator,
        IExporter exporter,
        IFileDeletionService fileDeletionService,
        IQuarantineService quarantineService,
        IPdfReportService pdfReport)
    {
        _logger = logger;
        _driveService = driveService;
        _pdfReport = pdfReport;
        _ = deltaComparator;
        _ = exporter;

        Health = new HealthViewModel();
        Programs = new ProgramsViewModel();

        // Base 'Rémanents' = historical 4 categories
        Orphans = new OrphansViewModel(
            fileDeletionService, logger,
            acceptedCategories: new[]
            {
                OrphanCategory.AppDataOrphan,
                OrphanCategory.EmptyProgramFiles,
                OrphanCategory.StaleTemp,
                OrphanCategory.OrphanInstallerPatch,
            },
            emptyStateMessage: "Aucun scan effectué. Lance un scan pour détecter les fichiers rémanents (AppData orphelins, Program Files vides, Temp anciens, patches MSI).");

        // 'Nettoyage' = new categories merged post-MVP
        Cleanup = new OrphansViewModel(
            fileDeletionService, logger,
            acceptedCategories: new[]
            {
                OrphanCategory.SystemArtifact,
                OrphanCategory.DevCache,
                OrphanCategory.BrowserCache,
            },
            emptyStateMessage: "Aucun scan effectué. Lance un scan pour voir les artefacts système, caches de dev et caches de navigateurs.");

        Tree = new TreeViewModel(fileDeletionService, logger);
        TreeMap = new TreeMapViewModel(fileDeletionService, logger);
        Quarantine = new QuarantineViewModel(quarantineService, logger);
        LargestFiles = new LargestFilesViewModel(fileDeletionService, logger);
        Extensions = new ExtensionsViewModel();
        Duplicates = new DuplicatesViewModel(fileDeletionService, logger);
        OldFiles = new OldFilesViewModel(fileDeletionService, logger);

        Orchestrator = new ScanOrchestratorViewModel(
            logger, driveService, fileSystemScanner, installedProgramsScanner,
            orphanDetectorService, persistenceService);

        Orchestrator.ScanCompleted += OnScanCompleted;
        _logger.Information("MainViewModel initialised; services wired via manual DI.");
    }

    [RelayCommand(CanExecute = nameof(CanExportPdf))]
    private async Task ExportPdfAsync()
    {
        if (_lastResult is null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exporter le rapport PDF",
            FileName = $"DiskScout_rapport_{DateTime.Now:yyyyMMdd_HHmm}.pdf",
            Filter = "Rapport PDF (*.pdf)|*.pdf",
            DefaultExt = ".pdf",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            await _pdfReport.GenerateAsync(
                _lastResult,
                Health.HealthScore,
                Health.HealthGrade,
                Health.Summary,
                Orphans.TotalBytes,
                Cleanup.TotalBytes,
                Duplicates.WastedBytes,
                OldFiles.TotalBytes,
                dlg.FileName);

            MessageBox.Show($"Rapport PDF généré :\n{dlg.FileName}",
                "DiskScout — export PDF", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "PDF export failed");
            MessageBox.Show($"Export PDF échoué : {ex.Message}",
                "DiskScout", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool CanExportPdf() => _lastResult is not null;

    private void OnScanCompleted(object? sender, ScanResult result)
    {
        void DoLoad()
        {
            Programs.Load(result.Programs);
            Orphans.Load(result.Orphans);
            Cleanup.Load(result.Orphans);
            Tree.Load(result.Nodes);
            TreeMap.Load(result.Nodes);
            LargestFiles.Load(result.Nodes);
            Extensions.Load(result.Nodes);
            Duplicates.Load(result.Nodes);
            OldFiles.Load(result.Nodes);

            Health.Load(
                result,
                _driveService.GetFixedDrives(),
                remnantsBytes: Orphans.TotalBytes,
                cleanupBytes: Cleanup.TotalBytes,
                duplicatesBytes: Duplicates.WastedBytes,
                oldFilesBytes: OldFiles.TotalBytes);

            _lastResult = result;
            ExportPdfCommand.NotifyCanExecuteChanged();
            _ = Quarantine.RefreshAsync();
            _logger.Information(
                "UI loaded: {Programs} progs, {Orphans} orphans, {Roots} roots, {Top} largest, {Exts} exts, {Dups} dup groups, {Old} old files",
                Programs.Count, Orphans.Count, Tree.Roots.Count, LargestFiles.Count,
                Extensions.Count, Duplicates.GroupCount, OldFiles.Count);
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            DoLoad();
        }
        else
        {
            dispatcher.Invoke(DoLoad);
        }
    }
}
