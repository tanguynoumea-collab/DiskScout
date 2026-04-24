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

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            _logger?.Fatal(args.ExceptionObject as Exception, "Unhandled AppDomain exception. Terminating={Terminating}", args.IsTerminating);
        };
        DispatcherUnhandledException += (_, args) =>
        {
            _logger?.Error(args.Exception, "Unhandled Dispatcher exception.");
            MessageBox.Show(
                args.Exception.Message + "\n\nVoir diskscout.log pour le détail.",
                "DiskScout — erreur",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _logger?.Error(args.Exception, "Unobserved task exception.");
            args.SetObserved();
        };

        // Composition root — manual DI.
        IDriveService driveService = new DriveService();
        IFileSystemScanner fileSystemScanner = new NativeFileSystemScanner(_logger);
        IInstalledProgramsScanner installedProgramsScanner = new RegistryInstalledProgramsScanner(_logger);
        IOrphanDetectorService orphanDetectorService = new OrphanDetectorService(_logger);
        IPersistenceService persistenceService = new JsonPersistenceService(_logger);
        IFileDeletionService fileDeletionService = new FileDeletionService(_logger);
        IQuarantineService quarantineService = new QuarantineService(_logger);
        IPdfReportService pdfReport = new PdfReportService(_logger);
        // Purge expired quarantine (>30 days) in background at startup
        _ = quarantineService.PurgeAsync(TimeSpan.FromDays(30));
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
            exporter,
            fileDeletionService,
            quarantineService,
            pdfReport);

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
