using CommunityToolkit.Mvvm.ComponentModel;
using DiskScout.Services;
using Serilog;

namespace DiskScout.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ILogger _logger;

    public ProgramsViewModel Programs { get; }
    public OrphansViewModel Orphans { get; }
    public TreeViewModel Tree { get; }

    [ObservableProperty]
    private string _windowTitle = "DiskScout [Admin]";

    [ObservableProperty]
    private int _selectedTabIndex;

    public MainViewModel(
        ILogger logger,
        IFileSystemScanner fileSystemScanner,
        IInstalledProgramsScanner installedProgramsScanner,
        IOrphanDetectorService orphanDetectorService,
        IPersistenceService persistenceService,
        IDeltaComparator deltaComparator,
        IExporter exporter)
    {
        _logger = logger;
        Programs = new ProgramsViewModel();
        Orphans = new OrphansViewModel();
        Tree = new TreeViewModel();

        _ = fileSystemScanner;
        _ = installedProgramsScanner;
        _ = orphanDetectorService;
        _ = persistenceService;
        _ = deltaComparator;
        _ = exporter;

        _logger.Information("MainViewModel initialised; services wired via manual DI.");
    }
}
