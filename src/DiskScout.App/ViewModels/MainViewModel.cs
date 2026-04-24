using CommunityToolkit.Mvvm.ComponentModel;
using DiskScout.Models;
using DiskScout.Services;
using Serilog;

namespace DiskScout.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ILogger _logger;

    public ProgramsViewModel Programs { get; }
    public OrphansViewModel Orphans { get; }
    public TreeViewModel Tree { get; }
    public ScanOrchestratorViewModel Orchestrator { get; }

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
        IExporter exporter)
    {
        _logger = logger;
        _ = deltaComparator;
        _ = exporter;

        Programs = new ProgramsViewModel();
        Orphans = new OrphansViewModel();
        Tree = new TreeViewModel();

        Orchestrator = new ScanOrchestratorViewModel(
            logger, driveService, fileSystemScanner, installedProgramsScanner, orphanDetectorService, persistenceService);

        Orchestrator.ScanCompleted += OnScanCompleted;
        _logger.Information("MainViewModel initialised; services wired via manual DI.");
    }

    private void OnScanCompleted(object? sender, ScanResult result)
    {
        Programs.Load(result.Programs);
        Orphans.Load(result.Orphans);
        Tree.Load(result.Nodes);
    }
}
