using System.Windows;
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
    public CloudViewModel Cloud { get; }
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
        ICloudStorageAnalyzer cloudStorageAnalyzer,
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
        Cloud = new CloudViewModel();

        Orchestrator = new ScanOrchestratorViewModel(
            logger, driveService, fileSystemScanner, installedProgramsScanner,
            orphanDetectorService, cloudStorageAnalyzer, persistenceService);

        Orchestrator.ScanCompleted += OnScanCompleted;
        _logger.Information("MainViewModel initialised; services wired via manual DI.");
    }

    private void OnScanCompleted(object? sender, ScanCompletedEventArgs e)
    {
        void DoLoad()
        {
            Programs.Load(e.Result.Programs);
            Orphans.Load(e.Result.Orphans);
            Tree.Load(e.Result.Nodes);
            Cloud.Load(e.CloudRoots);
            _logger.Information(
                "UI loaded: {Programs} programs, {Orphans} orphans, {Roots} tree roots, {Cloud} cloud roots",
                Programs.Count, Orphans.Count, Tree.Roots.Count, Cloud.Count);
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

public sealed class ScanCompletedEventArgs : EventArgs
{
    public ScanCompletedEventArgs(ScanResult result, IReadOnlyList<CloudSyncRoot> cloudRoots)
    {
        Result = result;
        CloudRoots = cloudRoots;
    }

    public ScanResult Result { get; }
    public IReadOnlyList<CloudSyncRoot> CloudRoots { get; }
}
