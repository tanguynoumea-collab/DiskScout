using System.Windows;
using DiskScout.Helpers;
using DiskScout.Services;
using DiskScout.Services.Stubs;
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

        // Composition root — manual DI, no container.
        IFileSystemScanner fileSystemScanner = new StubFileSystemScanner();
        IInstalledProgramsScanner installedProgramsScanner = new StubInstalledProgramsScanner();
        IOrphanDetectorService orphanDetectorService = new StubOrphanDetectorService();
        IPersistenceService persistenceService = new StubPersistenceService();
        IDeltaComparator deltaComparator = new StubDeltaComparator();
        IExporter exporter = new StubExporter();

        var mainViewModel = new MainViewModel(
            _logger,
            fileSystemScanner,
            installedProgramsScanner,
            orphanDetectorService,
            persistenceService,
            exporter: exporter,
            deltaComparator: deltaComparator);

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
