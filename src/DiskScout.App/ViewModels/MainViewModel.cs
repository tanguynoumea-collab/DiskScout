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
    public LargestFilesViewModel LargestFiles { get; }
    public ExtensionsViewModel Extensions { get; }
    public DuplicatesViewModel Duplicates { get; }
    public OldFilesViewModel OldFiles { get; }
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
        IExporter exporter,
        IFileDeletionService fileDeletionService)
    {
        _logger = logger;
        _ = deltaComparator;
        _ = exporter;

        Programs = new ProgramsViewModel();
        Orphans = new OrphansViewModel(fileDeletionService, logger);
        Tree = new TreeViewModel(fileDeletionService, logger);
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

    private void OnScanCompleted(object? sender, ScanResult result)
    {
        void DoLoad()
        {
            Programs.Load(result.Programs);
            Orphans.Load(result.Orphans);
            Tree.Load(result.Nodes);
            LargestFiles.Load(result.Nodes);
            Extensions.Load(result.Nodes);
            Duplicates.Load(result.Nodes);
            OldFiles.Load(result.Nodes);
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
