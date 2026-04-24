using System.Windows;
using DiskScout.Helpers;
using DiskScout.Services;
using DiskScout.Services.Stubs;
using DiskScout.Models;
using DiskScout.ViewModels;
using Serilog;
using Serilog.Core;

namespace DiskScout;

public partial class App : Application
{
    private Logger? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _logger = LogService.CreateLogger();
        Log.Logger = _logger;

        _logger.Information("DiskScout starting (version {Version}).", typeof(App).Assembly.GetName().Version);

        // Composition root — manual DI.
        IDriveService driveService = new DriveService();
        IFileSystemScanner fileSystemScanner = new NativeFileSystemScanner(_logger);
        IInstalledProgramsScanner installedProgramsScanner = new RegistryInstalledProgramsScanner(_logger);
        IOrphanDetectorService orphanDetectorService = new OrphanDetectorService(_logger);
        IPersistenceService persistenceService = new JsonPersistenceService(_logger);
        IDeltaComparator deltaComparator = new PathKeyedDeltaComparator();
        ScanResult? lastScan = null;
        IExporter exporter = new CsvHtmlExporter(() => lastScan);

        var mainViewModel = new MainViewModel(
            _logger,
            driveService,
            fileSystemScanner,
            installedProgramsScanner,
            orphanDetectorService,
            persistenceService,
            deltaComparator,
            exporter);

        var mainWindow = new MainWindow(mainViewModel);
        mainWindow.Show();

        _logger.Information("DiskScout shell shown.");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.Information("DiskScout exiting (code {ExitCode}).", e.ApplicationExitCode);
        _logger?.Dispose();
        base.OnExit(e);
    }
}
